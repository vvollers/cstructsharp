namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>Checks memory-backed and delegated read-budget paths at slice, position and exact-budget boundaries.</summary>
[TestClass]
public class ReadBudgetStreamBoundaryTests
{
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

    /// <summary>MemoryStream positions may pass the end but cannot be negative, and relative seek arithmetic cannot overflow.</summary>
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
        reader.Position = 4;
        Assert.AreEqual(-1, reader.ReadByte());
        Assert.IsFalse(reader.TryPeekRemaining(out _));
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
        using var source = new CStructSharpTests.AsyncStreamBufferTests.NonSeekableStream([11, 12,]);
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
        typeof(ReadBudgetStream).GetField("bytesRead", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(reader, long.MaxValue - 1);
        Assert.AreEqual(11, reader.ReadByte());

        // The underlying read occurs first; only its subsequent accounting exceeds the representable total.
        CStructReadLimitException failure = Assert.Throws<CStructReadLimitException>(() => reader.ReadByte());
        Assert.IsInstanceOfType<OverflowException>(failure.InnerException);
        Assert.AreEqual("Read operation exceeded the supported read-byte accounting range.", failure.Message);
        Assert.AreEqual(2L, source.Position);
    }
}
