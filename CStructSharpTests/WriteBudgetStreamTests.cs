namespace CStructSharp.Tests;

/// <summary>Verifies the internal stream boundary that applies physical-output and extent budgets.</summary>
[TestClass]
public class WriteBudgetStreamTests
{
    /// <summary>Forwards stream capabilities and every read/seek shape without charging the write budget.</summary>
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

    /// <summary>Allows shrinking and exact bounded extension while rejecting the first byte beyond the limit.</summary>
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

    /// <summary>Uses one cumulative physical budget across array, span, and single-byte writes.</summary>
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

    /// <summary>Applies the per-field string boundary exactly, including its invalid negative domain.</summary>
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

    /// <summary>Preflights a complete zero region and writes an accepted region through reusable bounded chunks.</summary>
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

    /// <summary>Rejects arithmetic overflow before submitting bytes to an extreme-position stream.</summary>
    [TestMethod]
    public void Write_RejectsStreamPositionOverflowBeforeInnerWrite()
    {
        using var inner = new ExtremePositionStream();
        using var stream = new WriteBudgetStream(inner, new WriteOptions());

        CStructWriteException exception = Assert.Throws<CStructWriteException>(() => stream.WriteByte(1));
        Assert.IsInstanceOfType<OverflowException>(exception.InnerException);
        Assert.AreEqual(0, inner.WriteCount);
    }

    /// <summary>Rejects a missing inner stream and leaves a valid caller-owned stream open on disposal.</summary>
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
