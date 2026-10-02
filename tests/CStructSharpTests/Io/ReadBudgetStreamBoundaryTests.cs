namespace CStructSharp.Tests;

using System.Collections.Immutable;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;
using CStructSharp.Syntax;

/// <summary>Checks memory-backed and delegated read-budget paths at slice, position and exact-budget boundaries.</summary>
[TestClass]
public class ReadBudgetStreamBoundaryTests
{
    /// <summary>A memory-backed flush failure reports the wrapper's logical cursor before it is copied to the source.</summary>
    [TestMethod]
    public void MemoryFlushFailure_ReportsTheLogicalPosition()
    {
        using var source = new FailedFlushMemoryStream();
        using var reader = new ReadBudgetStream(source, 100, 100);
        reader.Position = 1;
        Assert.AreEqual(0L, source.Position);

        // Memory reads own a separate cursor, so the physical stream's stale position is not diagnostic context.
        CStructReadException failure = Assert.ThrowsExactly<CStructReadException>(() => reader.Flush());
        Assert.AreSame(source.Failure, failure.InnerException);
        Assert.AreEqual(1L, failure.Offset);
    }

    /// <summary>
    ///     No position or requested count wraps into a successful preflight or an invalid array slice: a position beyond
    ///     the memory window is rejected before it is set, and a huge count at the end of the window reports a shortfall.
    /// </summary>
    /// <param name="operation">The availability check to exercise at the end of the window or with a large count.</param>
    [TestMethod]
    [DataRow("shortfall")]
    [DataRow("span")]
    [DataRow("budgeted-span")]
    [DataRow("large-count")]
    public void MemoryPreflight_LargePositionCannotWrap(string operation)
    {
        using var source = new MemoryStream([11, 12, 13,], 0, 3, writable: false, publiclyVisible: true);
        using var reader = new ReadBudgetStream(source, 100, 1);
        Assert.ThrowsExactly<CStructReadException>(() => reader.Position = long.MaxValue);
        Assert.AreEqual(0L, reader.Position);
        long position = operation == "large-count" ? 1 : 3;
        reader.Position = position;
        if (operation == "shortfall")
        {
            Assert.IsTrue(reader.IsShortBy(1));
        }
        else if (operation == "large-count")
        {
            Assert.IsTrue(reader.IsShortBy(long.MaxValue));
        }
        else if (operation == "span")
        {
            Assert.IsFalse(reader.TryReadSpan(1, out _));
        }
        else
        {
            Assert.IsFalse(reader.TryReadSpanWithinBudget(1, out _));
        }

        Assert.AreEqual(position, reader.Position);
        reader.Position = 0;
        Assert.IsTrue(reader.TryReadSpanWithinBudget(1, out ReadOnlySpan<byte> bytes));
        Assert.AreEqual((byte)11, bytes[0], "Failed availability checks must not consume the byte budget.");
    }

    /// <summary>A full cumulative budget cannot wrap and permit an optimized read to consume another byte.</summary>
    /// <param name="memoryBacked">Whether preflight uses a borrowed memory span or a delegated stream block.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void PreflightBudget_CannotWrapTheCumulativeCount(bool memoryBacked)
    {
        using var source = new MemoryStream([11, 12,], 0, 2, writable: false, publiclyVisible: memoryBacked);
        using var reader = new ReadBudgetStream(source, 100, long.MaxValue);

        // Seed a valid cumulative count; physically reading this many bytes is not practical in a regression test.
        SeedBytesRead(reader, long.MaxValue);
        byte[] destination = [99];
        bool available = memoryBacked
                             ? reader.TryReadSpanWithinBudget(1, out _)
                             : reader.TryReadBlockWithinBudget(destination);
        Assert.IsFalse(available);
        Assert.AreEqual(0L, reader.Position);
        Assert.AreEqual(0L, source.Position);
        CollectionAssert.AreEqual(new byte[] { 99, }, destination);
    }

    /// <summary>Exposed MemoryStream slices retain their origin while peek, span reads and position flush share one cursor.</summary>
    [TestMethod]
    public void ExposedSlice_UsesItsArrayOffsetAndCurrentPosition()
    {
        using var source = new MemoryStream([99, 98, 11, 12, 13, 97,], 2, 3, writable: false, publiclyVisible: true);
        using var reader = new ReadBudgetStream(source, 100, 2);
        Assert.AreEqual(3L, reader.Length);
        Assert.IsTrue(reader.CanRead);
        Assert.IsTrue(reader.CanSeek);
        Assert.IsFalse(reader.CanWrite);
        reader.Position = 1;
        Assert.IsTrue(reader.TryPeekRemaining(out ReadOnlySpan<byte> remaining));
        CollectionAssert.AreEqual(new byte[] { 12, 13, }, remaining.ToArray());
        Assert.AreEqual(1L, reader.Position);
        Assert.AreEqual(0L, source.Position);
        Assert.IsTrue(reader.TryReadSpanWithinBudget(2, out ReadOnlySpan<byte> selected));
        CollectionAssert.AreEqual(new byte[] { 12, 13, }, selected.ToArray());
        Assert.AreEqual(3L, reader.Position);
        Assert.IsTrue(reader.TryPeekRemaining(out ReadOnlySpan<byte> empty));
        Assert.IsTrue(empty.IsEmpty);
        reader.FlushPosition();
        Assert.AreEqual(3L, source.Position);
    }

    /// <summary>Span preflight rejects short input and excessive budget requests without consuming or charging bytes.</summary>
    [TestMethod]
    public void MemoryPreflight_RejectsUnavailableAndUnaffordableSpans()
    {
        using var source = new MemoryStream([11, 12, 13,], 0, 3, writable: false, publiclyVisible: true);
        using var reader = new ReadBudgetStream(source, 100, 1);
        reader.Position = 1;
        Assert.IsFalse(reader.TryReadSpanWithinBudget(3, out _));
        Assert.IsFalse(reader.TryReadSpanWithinBudget(2, out _));
        Assert.AreEqual(1L, reader.Position);
        Assert.IsTrue(reader.TryReadSpanWithinBudget(1, out ReadOnlySpan<byte> selected));
        Assert.AreEqual((byte)12, selected[0]);
        Assert.IsFalse(reader.TryReadSpanWithinBudget(1, out _));
        Assert.AreEqual(2L, reader.Position);
    }

    /// <summary>A MemoryStream position may reach the end but neither pass it nor be negative, and relative seek arithmetic cannot overflow.</summary>
    [TestMethod]
    public void MemoryPosition_EnforcesItsBackingContract()
    {
        using var source = new MemoryStream([11, 12, 13,], 0, 3, writable: false, publiclyVisible: true);
        using var reader = new ReadBudgetStream(source, 100, 100);
        Assert.AreEqual(2L, reader.Seek(-1, SeekOrigin.End));
        Assert.AreEqual(1L, reader.Seek(-1, SeekOrigin.Current));

        // Relative addition must fail before assigning a wrapped, negative cursor position.
        Assert.Throws<OverflowException>(() => reader.Seek(long.MaxValue, SeekOrigin.Current));
        Assert.Throws<CStructReadException>(() => reader.Position = -1);
        Assert.AreEqual(1L, reader.Position);
        Assert.Throws<CStructReadException>(() => reader.Position = 4);
        Assert.AreEqual(1L, reader.Position);
        reader.Position = 3;
        Assert.AreEqual(-1, reader.ReadByte());
        Assert.IsTrue(reader.TryPeekRemaining(out ReadOnlySpan<byte> remaining));
        Assert.AreEqual(0, remaining.Length);
    }

    /// <summary>A fixed region rejects positions beyond its end, even though an ordinary MemoryStream permits them.</summary>
    [TestMethod]
    public unsafe void FixedRegion_RejectsOutOfRangePositions()
    {
        byte* bytes = stackalloc byte[] { 11, 12, };
        using var source = new FixedBufferStream(bytes, 2, writable: false);
        using var reader = new ReadBudgetStream(source, 100, 100);

        // The bounded region must remain bounded when the budget wrapper owns the cursor.
        Assert.Throws<CStructReadException>(() => reader.Position = 3);
        Assert.Throws<CStructReadException>(() => reader.Position = -1);
        Assert.AreEqual(0L, reader.Position);
        Assert.IsTrue(reader.TryReadSpan(2, out ReadOnlySpan<byte> selected));
        CollectionAssert.AreEqual(new byte[] { 11, 12, }, selected.ToArray());
    }

    /// <summary>Delegated block reads use remaining length and accept an exact byte-budget match.</summary>
    [TestMethod]
    public void StreamBlock_UsesRemainingLengthAndExactBudget()
    {
        using var source = new MemoryStream(new byte[] { 11, 12, 13, }, writable: false);
        source.Position = 1;
        using var reader = new ReadBudgetStream(source, 100, 2);
        Assert.IsTrue(reader.IsShortBy(3));
        Assert.IsFalse(reader.IsShortBy(2));
        Assert.IsFalse(reader.TryReadSpanWithinBudget(1, out _));
        Assert.IsFalse(reader.TryReadBlockWithinBudget(new byte[3]));
        Assert.AreEqual(1L, source.Position);
        byte[] destination = new byte[2];
        Assert.IsTrue(reader.TryReadBlockWithinBudget(destination));
        CollectionAssert.AreEqual(new byte[] { 12, 13, }, destination);
        Assert.AreEqual(3L, source.Position);
        Assert.IsFalse(reader.TryReadBlockWithinBudget(Span<byte>.Empty));
    }

    /// <summary>A non-seekable source cannot promise enough bytes for the optimized block-read path.</summary>
    [TestMethod]
    public void ForwardOnlySource_DeclinesPreflightWithoutReading()
    {
        using var source = new AsyncStreamBufferTests.NonSeekableStream([11, 12,]);
        using var reader = new ReadBudgetStream(source, 100, 100);
        Assert.IsFalse(reader.IsShortBy(3));
        Assert.IsFalse(reader.TryReadBlockWithinBudget(new byte[1]));
        Assert.AreEqual(11, reader.ReadByte());
    }

    /// <summary>Mutation and length-changing operations explain the wrapper's read-only contract.</summary>
    [TestMethod]
    public void WritesAndLengthChanges_ReportReadOnlyContract()
    {
        using var source = new MemoryStream();
        using var reader = new ReadBudgetStream(source, 100, 100);

        // Neither operation is allowed to mutate the caller-owned source.
        NotSupportedException write = Assert.Throws<NotSupportedException>(() => reader.Write(new byte[1], 0, 1));
        NotSupportedException length = Assert.Throws<NotSupportedException>(() => reader.SetLength(1));
        Assert.AreEqual("The parse budget stream is read-only.", write.Message);
        Assert.AreEqual(write.Message, length.Message);
        Assert.AreEqual(0L, source.Length);
    }

    /// <summary>Empty reads spend no additional budget, even after a prior nonempty read exceeded its limit.</summary>
    [TestMethod]
    public void EmptyRead_DoesNotReapplyAnAlreadyExceededBudget()
    {
        using var source = new MemoryStream(new byte[] { 11, 12, }, writable: false);
        using var reader = new ReadBudgetStream(source, 100, 1);

        // The stream supplied two bytes before the accounting boundary could report the exceeded limit.
        Assert.Throws<CStructReadLimitException>(() => reader.Read(new byte[2], 0, 2));
        Assert.AreEqual(0, reader.Read(Span<byte>.Empty));
        Assert.AreEqual(2L, source.Position);
    }

    /// <summary>Cumulative read accounting rejects overflow instead of wrapping into an affordable negative count.</summary>
    [TestMethod]
    public void CumulativeRead_ReportsAccountingOverflow()
    {
        using var source = new MemoryStream(new byte[] { 11, 12, }, writable: false);
        using var reader = new ReadBudgetStream(source, 100, long.MaxValue);

        // Seed a valid prior cumulative count; replaying that many reads would not be a practical regression test.
        SeedBytesRead(reader, long.MaxValue - 1);
        Assert.AreEqual(11, reader.ReadByte());

        // The underlying read occurs first; only its subsequent accounting exceeds the representable total.
        CStructReadLimitException failure = Assert.Throws<CStructReadLimitException>(() => reader.ReadByte());
        Assert.IsInstanceOfType<OverflowException>(failure.InnerException);
        Assert.AreEqual("Read operation exceeded the supported read-byte accounting range.", failure.Message);
        Assert.AreEqual(2L, source.Position);
    }

    /// <summary>
    ///     With a budget of 2 over <c>11 12 13</c>, two single-byte reads exactly reach the budget; the third byte is still
    ///     consumed but fails, and its charge stays recorded, so the remaining budget is -1. Later charges of zero or less
    ///     cost nothing, while a further byte fails again and is recorded again.
    /// </summary>
    /// <param name="memoryBacked">Whether the bytes are read from memory or delegated to the stream.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Charge_AcceptsTheExactBudgetAndRecordsTheOvershoot(bool memoryBacked)
    {
        using var source = new MemoryStream([11, 12, 13,], 0, 3, writable: false, publiclyVisible: memoryBacked);
        using var reader = new ReadBudgetStream(source, 100, 2);

        Assert.AreEqual(11, reader.ReadByte());
        Assert.AreEqual(12, reader.ReadByte());
        Assert.AreEqual(0L, reader.RemainingReadBudget);

        // The byte is read before it is charged, so the failing read has consumed it.
        CStructReadLimitException failure = Assert.ThrowsExactly<CStructReadLimitException>(() => reader.ReadByte());
        Assert.AreEqual(ReadFailures.TotalBytesLimit, failure.Message);
        Assert.IsNull(failure.InnerException);
        Assert.AreEqual(3L, reader.Position);
        Assert.AreEqual(-1L, reader.RemainingReadBudget);

        // A caller that refunds the budget change it observed relies on the overshoot staying recorded.
        reader.Charge(0);
        reader.Charge(-1);
        Assert.AreEqual(-1L, reader.RemainingReadBudget);
        Assert.AreEqual(ReadFailures.TotalBytesLimit, Assert.ThrowsExactly<CStructReadLimitException>(() => reader.Charge(1)).Message);
        Assert.AreEqual(-2L, reader.RemainingReadBudget);
    }

    /// <summary>
    ///     A charge that would overflow the cumulative count reports the accounting range and leaves the count unchanged, so
    ///     the remaining budget is still the 1 byte it was before the failure.
    /// </summary>
    [TestMethod]
    public void Charge_AccountingOverflowLeavesTheCountUnchanged()
    {
        using var source = new MemoryStream([11,], 0, 1, writable: false, publiclyVisible: true);
        using var reader = new ReadBudgetStream(source, 100, long.MaxValue);
        SeedBytesRead(reader, long.MaxValue - 1);

        CStructReadLimitException failure = Assert.ThrowsExactly<CStructReadLimitException>(() => reader.Charge(long.MaxValue));
        Assert.IsInstanceOfType<OverflowException>(failure.InnerException);
        Assert.AreEqual(1L, reader.RemainingReadBudget);
        reader.Charge(1);
        Assert.AreEqual(0L, reader.RemainingReadBudget);
    }

    /// <summary>
    ///     A path read of <c>s.b</c> in <c>struct s { uint32 a; uint32 b; }</c> through the engine's memory cursor succeeds
    ///     with the smallest budget it needs and fails with the total-limit message one byte below it, from both a byte
    ///     array and an exposable memory stream.
    /// </summary>
    [TestMethod]
    public void PathRead_ExactBudgetSucceedsAndOneLessFails()
    {
        var layout = new CStruct("struct s { uint32 a; uint32 b; };");
        byte[] data = [1, 0, 0, 0, 2, 0, 0, 0,];

        // A path read is charged the bytes from the struct start through its target, as a parse up to b would be.
        const int Needed = 8;
        var enough = new ReadOptions { MaxTotalBytesRead = Needed, };
        var tooSmall = new ReadOptions { MaxTotalBytesRead = Needed - 1, };
        Assert.AreEqual(2u, layout.ReadValue(data, "s.b", options: enough));
        Assert.AreEqual(2u, layout.ReadValue(ExposedStream(data), "s.b", options: enough));

        // The failure message may carry the field context after the shared total-limit wording.
        string limitPrefix = ReadFailures.TotalBytesLimit.TrimEnd('.');
        CStructReadLimitException arrayFailure = Assert.ThrowsExactly<CStructReadLimitException>(
            () => layout.ReadValue(data, "s.b", options: tooSmall));
        CStructReadLimitException streamFailure = Assert.ThrowsExactly<CStructReadLimitException>(
            () => layout.ReadValue(ExposedStream(data), "s.b", options: tooSmall));
        StringAssert.StartsWith(arrayFailure.Message, limitPrefix);
        StringAssert.StartsWith(streamFailure.Message, limitPrefix);
    }

    /// <summary>
    ///     An exposable memory stream may be positioned past its end before the operation starts. Reading there returns no
    ///     bytes and charges nothing, instead of addressing the buffer beyond its slice.
    /// </summary>
    [TestMethod]
    public void MemoryRead_PositionedPastTheEndReturnsNothing()
    {
        using var source = new MemoryStream([11, 12, 13,], 0, 3, writable: false, publiclyVisible: true) { Position = 10, };
        using var reader = new ReadBudgetStream(source, 100, 5);
        byte[] destination = [99, 99,];

        Assert.AreEqual(0, reader.Read(destination, 0, destination.Length));
        Assert.AreEqual(-1, reader.ReadByte());
        CollectionAssert.AreEqual(new byte[] { 99, 99, }, destination);
        Assert.AreEqual(5L, reader.RemainingReadBudget);
    }

    /// <summary>
    ///     The wrapper must read 11,22,33 through array, span, and single-byte APIs, report end-of-stream correctly,
    ///     and permit seeking.
    /// </summary>
    /// <remarks>
    ///     Writes and resizing must be rejected. Disposing it leaves the original stream readable because the caller
    ///     still owns that stream.
    /// </remarks>
    [TestMethod]
    public void ReadBudgetStream_ImplementsItsReadOnlyNonOwningContract()
    {
        var options = new ReadOptions { MaxStringBytes = 7, MaxTotalBytesRead = 100, };
        Assert.Throws<ArgumentNullException>(() => new ReadBudgetStream(null!, options));

        var inner = new MemoryStream([0x11, 0x22, 0x33,]);
        var budget = new ReadBudgetStream(inner, options);
        Assert.IsTrue(budget.CanRead);
        Assert.IsTrue(budget.CanSeek);
        Assert.IsFalse(budget.CanWrite);
        Assert.AreEqual(3, budget.Length);
        Assert.AreEqual(0, budget.Position);
        Assert.AreEqual(7, budget.MaxStringBytes);

        budget.Flush();
        byte[] first = new byte[1];
        Assert.AreEqual(1, budget.Read(first, 0, first.Length));
        Assert.AreEqual((byte)0x11, first[0]);
        Span<byte> second = stackalloc byte[1];
        Assert.AreEqual(1, budget.Read(second));
        Assert.AreEqual((byte)0x22, second[0]);
        Assert.AreEqual(0x33, budget.ReadByte());
        Assert.AreEqual(-1, budget.ReadByte());
        Assert.AreEqual(0, budget.Read(first, 0, first.Length));

        Assert.AreEqual(0, budget.Seek(0, SeekOrigin.Begin));
        budget.Position = 1;
        Assert.AreEqual(1, budget.Position);
        Assert.Throws<NotSupportedException>(() => budget.SetLength(1));
        Assert.Throws<NotSupportedException>(() => budget.Write(first, 0, first.Length));

        budget.Dispose();
        Assert.IsTrue(inner.CanRead);
        Assert.AreEqual(0x22, inner.ReadByte());
        inner.Dispose();
    }

    /// <summary>
    ///     Sets the operation's cumulative read count, which the stream keeps in its <see cref="MemoryReadCore"/> (shared
    ///     with the engine's memory cursor); the core is a struct, so the updated copy is stored back.
    /// </summary>
    /// <param name="reader">The budget stream.</param>
    /// <param name="bytesRead">The count to seed.</param>
    private static void SeedBytesRead(ReadBudgetStream reader, long bytesRead)
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        System.Reflection.FieldInfo coreField = typeof(ReadBudgetStream).GetField("core", Private)!;
        object core = coreField.GetValue(reader)!;
        typeof(MemoryReadCore).GetField("bytesRead", Private)!.SetValue(core, bytesRead);
        coreField.SetValue(reader, core);
    }

    /// <summary>Wraps <paramref name="data"/> in a read-only memory stream whose buffer the reader may use directly.</summary>
    /// <param name="data">The bytes to expose; the stream does not copy them.</param>
    /// <returns>A stream positioned at byte 0.</returns>
    private static MemoryStream ExposedStream(byte[] data) => new(data, 0, data.Length, writable: false, publiclyVisible: true);

    /// <summary>Exposes its array for borrowed reads but fails the independent flush operation.</summary>
    private sealed class FailedFlushMemoryStream : MemoryStream
    {
        /// <summary>Creates two readable, publicly exposed bytes at position zero.</summary>
        public FailedFlushMemoryStream()
            : base([11, 12,], 0, 2, writable: false, publiclyVisible: true)
        {
        }

        /// <summary>Gets the stable underlying flush failure.</summary>
        public IOException Failure { get; } = new("Flush failed.");

        /// <summary>Rejects flushing without changing the physical cursor.</summary>
        /// <exception cref="IOException">Always reports the configured flush failure.</exception>
        public override void Flush() => throw this.Failure;
    }
}
