namespace CStructSharp.Tests;

/// <summary>Verifies the internal stream boundary that applies physical-output and extent budgets.</summary>
[TestClass]
public class WriteBudgetStreamTests
{
    /// <summary>
    ///     This internal stream wrapper has a write budget of one byte, but ordinary reads and seeks must still work.
    /// </summary>
    /// <remarks>
    ///     Capability flags, length, position, and flush must reflect the wrapped stream. Reading bytes does not spend
    ///     a write budget; accounting must match the direction of the operation.
    /// </remarks>
    [TestMethod]
    public void ForwardingSurface_PreservesCallerStreamBehavior()
    {
        using var inner = new TrackingMemoryStream([1, 2, 3, 4,]);
        using var stream = new WriteBudgetStream(
            inner,
            new WriteOptions { MaxTotalBytesWritten = 1, });

        Assert.IsTrue(stream.CanRead);
        Assert.IsTrue(stream.CanSeek);
        Assert.IsTrue(stream.CanWrite);
        Assert.AreEqual(4L, stream.Length);
        Assert.AreEqual(0L, stream.Position);

        stream.Flush();
        Assert.AreEqual(1, inner.FlushCount);
        Assert.AreEqual(1, stream.ReadByte());

        Span<byte> span = stackalloc byte[2];
        Assert.AreEqual(2, stream.Read(span));
        CollectionAssert.AreEqual(new byte[] { 2, 3, }, span.ToArray());

        Assert.AreEqual(1L, stream.Seek(1, SeekOrigin.Begin));
        var buffer = new byte[2];
        Assert.AreEqual(2, stream.Read(buffer, 0, buffer.Length));
        CollectionAssert.AreEqual(new byte[] { 2, 3, }, buffer);

        stream.Position = 4;
        Assert.AreEqual(4L, inner.Position);
    }

    /// <summary>
    ///     The stream begins with two bytes, and a budget of two allows extending it to four.
    /// </summary>
    /// <remarks>
    ///     Extending to five must fail without changing its length. Shrinking to one remains allowed because it creates
    ///     no new output extent.
    /// </remarks>
    [TestMethod]
    public void SetLength_ChargesOnlyExtentBeyondInitialLength()
    {
        using var inner = new MemoryStream();
        inner.Write(new byte[] { 1, 2, });
        using var stream = new WriteBudgetStream(
            inner,
            new WriteOptions { MaxTotalBytesWritten = 2, });

        stream.SetLength(4);
        Assert.AreEqual(4L, stream.Length);

        Assert.Throws<CStructWriteException>(() => stream.SetLength(5));
        Assert.AreEqual(4L, stream.Length);

        stream.SetLength(1);
        Assert.AreEqual(1L, stream.Length);
    }

    /// <summary>
    ///     Array, span, and single-byte writes all spend the same four-byte budget.
    /// </summary>
    /// <remarks>
    ///     Rewriting an earlier position counts again even though it does not increase the stream length. The next
    ///     write must fail, leaving exactly the bytes produced by the four accepted writes.
    /// </remarks>
    [TestMethod]
    public void WriteOverloads_ShareOneExactPhysicalBudget()
    {
        using var inner = new MemoryStream();
        inner.Write(new byte[] { 0xA5, 0xA5, });
        using var stream = new WriteBudgetStream(
            inner,
            new WriteOptions { MaxTotalBytesWritten = 4, });

        stream.Write(new byte[] { 1, }, 0, 1);
        stream.Write(new ReadOnlySpan<byte>(new byte[] { 2, }));
        stream.WriteByte(3);
        stream.Position = 0;
        stream.WriteByte(4);

        Assert.Throws<CStructWriteException>(() => stream.WriteByte(5));
        CollectionAssert.AreEqual(new byte[] { 4, 0xA5, 1, 2, 3, }, inner.ToArray());
        Assert.AreEqual(1L, stream.Position);
    }

    /// <summary>
    ///     With a two-byte string limit, lengths zero and two are valid, while -1 and three must be rejected.
    /// </summary>
    /// <remarks>
    ///     This tests the internal size check before encoding or output. The upper limit is inclusive, and a negative
    ///     byte length is never meaningful.
    /// </remarks>
    [TestMethod]
    public void StringBytes_RejectNegativeAndFirstByteOverLimit()
    {
        using var inner = new MemoryStream();
        using var stream = new WriteBudgetStream(
            inner,
            new WriteOptions { MaxStringBytes = 2, });

        stream.EnsureStringBytes(0);
        stream.EnsureStringBytes(2);
        Assert.Throws<CStructWriteException>(() => stream.EnsureStringBytes(-1));
        Assert.Throws<CStructWriteException>(() => stream.EnsureStringBytes(3));
    }

    /// <summary>
    ///     A request for 10000 zero bytes must fail before writing anything under a 9999-byte budget.
    /// </summary>
    /// <remarks>
    ///     With enough budget it must produce exactly 10000 zeros, even though the implementation writes in chunks. A
    ///     zero-length request does nothing, while a negative length is invalid.
    /// </remarks>
    [TestMethod]
    public void WriteZeroes_RejectsBeforeFirstChunk_AndWritesExactLargeRegion()
    {
        using var inner = new MemoryStream();
        using (var limited = new WriteBudgetStream(
                   inner,
                   new WriteOptions { MaxTotalBytesWritten = 9_999, }))
        {
            limited.Position = 10_000;
            limited.WriteZeroes(0);
            limited.Position = 0;
            Assert.Throws<ArgumentOutOfRangeException>(() => limited.WriteZeroes(-1));
            Assert.Throws<CStructWriteException>(() => limited.WriteZeroes(10_000));
            Assert.AreEqual(0L, inner.Length);
        }

        using (var exact = new WriteBudgetStream(
                   inner,
                   new WriteOptions { MaxTotalBytesWritten = 10_000, }))
        {
            exact.WriteZeroes(10_000);
        }

        byte[] output = inner.ToArray();
        Assert.AreEqual(10_000, output.Length);
        Assert.IsTrue(output.All(value => value == 0));
    }

    /// <summary>
    ///     The fake destination reports a position near the largest supported offset.
    /// </summary>
    /// <remarks>
    ///     Adding the next byte would overflow address arithmetic, so the wrapper must raise a write error before
    ///     calling the underlying writer. The original overflow remains available as the cause.
    /// </remarks>
    [TestMethod]
    public void Write_RejectsStreamPositionOverflowBeforeInnerWrite()
    {
        using var inner = new ExtremePositionStream();
        using var stream = new WriteBudgetStream(inner, new WriteOptions());

        CStructWriteException exception = Assert.Throws<CStructWriteException>(() => stream.WriteByte(1));
        Assert.IsInstanceOfType<OverflowException>(exception.InnerException);
        Assert.AreEqual(0, inner.WriteCount);
    }

    /// <summary>
    ///     A null inner stream must be rejected at construction.
    /// </summary>
    /// <remarks>
    ///     Disposing a valid budget wrapper must leave the caller's stream usable, demonstrated by a later successful
    ///     byte write. The wrapper controls accounting, not the lifetime of storage owned by its caller.
    /// </remarks>
    [TestMethod]
    public void Lifetime_DoesNotTakeOwnershipOfCallerStream()
    {
        Assert.Throws<ArgumentNullException>(
            () => new WriteBudgetStream(null!, new WriteOptions()));

        using var inner = new MemoryStream();
        var stream = new WriteBudgetStream(inner, new WriteOptions());
        stream.Dispose();

        inner.WriteByte(1);
        Assert.AreEqual(1L, inner.Length);
    }

    /// <summary>Models a writable stream positioned at the largest supported address without accepting writes.</summary>
    private sealed class ExtremePositionStream : Stream
    {
        public int WriteCount { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position { get; set; } = long.MaxValue;

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            this.Position = offset;
            return this.Position;
        }

        public override void SetLength(long value)
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.WriteCount++;
        }

        public override void WriteByte(byte value)
        {
            this.WriteCount++;
        }
    }

    /// <summary>Exposes otherwise invisible flush forwarding while retaining normal memory-stream behavior.</summary>
    private sealed class TrackingMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public int FlushCount { get; private set; }

        public override void Flush()
        {
            this.FlushCount++;
            base.Flush();
        }
    }
}
