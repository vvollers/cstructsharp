namespace CStructSharp.Tests;

using System.Buffers;

/// <summary>Checks the seekable active-window contract used by direct IBufferWriter serialization.</summary>
[TestClass]
public class BufferWriterStreamTests
{
    /// <summary>Supports local rewrites, reads, gaps, length changes, and completion without output copies.</summary>
    [TestMethod]
    public void ActiveWindow_ProvidesTheStreamOperationsRequiredByTheSerializer()
    {
        var writer = new ArrayBufferWriter<byte>();
        using var stream = new BufferWriterStream(writer);

        Assert.IsTrue(stream.CanRead);
        Assert.IsTrue(stream.CanSeek);
        Assert.IsTrue(stream.CanWrite);
        Assert.AreEqual(0L, stream.Length);
        Assert.AreEqual(0L, stream.Position);

        stream.Write(new byte[] { 1, 2, 3, }, 0, 3);
        Assert.AreEqual(3L, stream.Length);
        Assert.AreEqual(3L, stream.Seek(0, SeekOrigin.End));
        stream.Position = 1;
        Span<byte> read = stackalloc byte[2];
        Assert.AreEqual(2, stream.Read(read));
        CollectionAssert.AreEqual(new byte[] { 2, 3, }, read.ToArray());

        stream.Position = 1;
        stream.WriteByte(9);
        stream.Position = 5;
        stream.WriteByte(7);
        stream.SetLength(7);
        Assert.AreEqual(7L, stream.Length);
        Assert.AreEqual(7L, stream.Complete());
        CollectionAssert.AreEqual(
            new byte[] { 1, 9, 3, 0, 0, 7, 0, },
            writer.WrittenSpan.ToArray());
        Assert.Throws<ObjectDisposedException>(() => stream.WriteByte(8));
    }

    /// <summary>Refuses to seek back into output that the IBufferWriter already owns.</summary>
    [TestMethod]
    public void CommittedWindow_CannotBeRevisitedOrTruncated()
    {
        var writer = new ExactWindowWriter();
        using var stream = new BufferWriterStream(writer);
        stream.Write(new byte[4096]);
        stream.WriteByte(1);

        Assert.Throws<IOException>(() => stream.Position = 0);
        Assert.Throws<IOException>(() => stream.Seek(-4097, SeekOrigin.Current));
        Assert.Throws<IOException>(() => stream.SetLength(1));
        Assert.AreEqual(4097L, stream.Complete());
        Assert.AreEqual(4097, writer.Written.Count);
        Assert.AreEqual((byte)1, writer.Written[^1]);
    }

    /// <summary>Validates construction and the array-based read/write argument contract.</summary>
    [TestMethod]
    public void ArrayOperations_RejectNullAndInvalidRanges()
    {
        Assert.Throws<ArgumentNullException>(() => new BufferWriterStream(null!));

        var writer = new ArrayBufferWriter<byte>();
        using var stream = new BufferWriterStream(writer);
        byte[] array = [1, 2, 3,];

        Assert.Throws<ArgumentNullException>(() => stream.Read(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(array, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(array, 0, -1));
        Assert.Throws<ArgumentException>(() => stream.Read(array, 2, 2));
        Assert.AreEqual(0, stream.Read(array, 0, array.Length));

        Assert.Throws<ArgumentNullException>(() => stream.Write(null!, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Write(array, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Write(array, 0, -1));
        Assert.Throws<ArgumentException>(() => stream.Write(array, 2, 2));
    }

    /// <summary>Allows idempotent completion and flush while rejecting stateful operations afterward.</summary>
    [TestMethod]
    public void Completion_IsIdempotentAndClosesStatefulOperations()
    {
        var writer = new ArrayBufferWriter<byte>();
        using var stream = new BufferWriterStream(writer);
        stream.Flush();
        stream.Write(new byte[] { 1, 2, 3, });

        Assert.AreEqual(3L, stream.Complete());
        Assert.AreEqual(3L, stream.Complete());
        stream.Flush();
        Assert.AreEqual(3L, stream.Length);
        Assert.AreEqual(3L, stream.Position);

        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
        Assert.Throws<ObjectDisposedException>(() => stream.WriteByte(4));
        Assert.Throws<ObjectDisposedException>(() => stream.Position = 0);
        Assert.Throws<ObjectDisposedException>(() => stream.Seek(0, SeekOrigin.Begin));
        Assert.Throws<ObjectDisposedException>(() => stream.SetLength(0));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, writer.WrittenSpan.ToArray());

        using var empty = new BufferWriterStream(new ArrayBufferWriter<byte>());
        Assert.AreEqual(0L, empty.Complete());
    }

    /// <summary>Rejects invalid and overflowing seek/length requests before allocating an impractical window.</summary>
    [TestMethod]
    public void PositionAndLength_RejectInvalidOrOverflowingRequests()
    {
        var writer = new ArrayBufferWriter<byte>();
        using var stream = new BufferWriterStream(writer);

        Assert.Throws<IOException>(() => stream.Position = -1);
        Assert.Throws<IOException>(() => stream.Position = (long)int.MaxValue + 1);
        Assert.Throws<IOException>(() => stream.SetLength(-1));
        Assert.Throws<IOException>(() => stream.SetLength((long)int.MaxValue + 1));
        Assert.Throws<IOException>(() => stream.Seek(long.MinValue, SeekOrigin.Begin));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)99));

        stream.Position = 1;
        IOException overflow = Assert.Throws<IOException>(
            () => stream.Seek(long.MaxValue, SeekOrigin.Current));
        Assert.IsInstanceOfType<OverflowException>(overflow.InnerException);
    }

    /// <summary>Grows, clears, truncates, and clamps position inside one uncommitted writer window.</summary>
    [TestMethod]
    public void SetLength_ManagesOnlyTheActiveWindow()
    {
        var writer = new ArrayBufferWriter<byte>();
        using var stream = new BufferWriterStream(writer);

        stream.SetLength(5);
        Assert.AreEqual(5L, stream.Length);
        Assert.AreEqual(0L, stream.Position);
        stream.Position = 1;
        stream.WriteByte(9);
        stream.SetLength(7);
        Assert.AreEqual(7L, stream.Length);
        stream.Position = 7;
        stream.SetLength(3);
        Assert.AreEqual(3L, stream.Length);
        Assert.AreEqual(3L, stream.Position);

        Assert.AreEqual(3L, stream.Complete());
        CollectionAssert.AreEqual(new byte[] { 0, 9, 0, }, writer.WrittenSpan.ToArray());
    }

    /// <summary>Commits a full window, carries a forward gap, and clears it before appending the next byte.</summary>
    [TestMethod]
    public void ForwardGapAcrossWindowBoundary_IsZeroInitialized()
    {
        var writer = new ExactWindowWriter();
        using var stream = new BufferWriterStream(writer);
        byte[] firstWindow = Enumerable.Repeat((byte)0xA5, 4096).ToArray();
        stream.Write(firstWindow);

        Assert.AreEqual(4098L, stream.Seek(2, SeekOrigin.End));
        stream.WriteByte(0x7E);

        Assert.AreEqual(4099L, stream.Complete());
        Assert.AreEqual(4099, writer.Written.Count);
        CollectionAssert.AreEqual(firstWindow, writer.Written[..4096]);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0x7E, }, writer.Written[4096..]);
    }

    /// <summary>Refuses rewrites, seeks, or length growth that would cross an active-window boundary.</summary>
    [TestMethod]
    public void ActiveWindowBoundary_CannotBeCrossedWhileRewriting()
    {
        var writer = new ExactWindowWriter();
        using var stream = new BufferWriterStream(writer);
        stream.Write(new byte[4096]);
        stream.Position = 4095;

        Assert.Throws<IOException>(() => stream.Write(new byte[2]));
        Assert.Throws<IOException>(() => stream.Position = 4097);
        Assert.Throws<IOException>(() => stream.SetLength(4097));
        Assert.AreEqual(4095L, stream.Position);
        Assert.AreEqual(4096L, stream.Length);
        Assert.AreEqual(4096L, stream.Complete());
    }

    /// <summary>Fails closed when an IBufferWriter violates a nonzero requested-size contract.</summary>
    [TestMethod]
    public void WriterReturningTooLittleMemory_IsRejected()
    {
        using var stream = new BufferWriterStream(new ShortWindowWriter());

        Assert.Throws<InvalidOperationException>(() => stream.Write(new byte[4096]));
    }

    /// <summary>Returns exactly the requested active window size and records each advance.</summary>
    private sealed class ExactWindowWriter : IBufferWriter<byte>
    {
        private byte[] active = [];

        public List<byte> Written { get; } = [];

        public void Advance(int count)
        {
            this.Written.AddRange(this.active.AsSpan(0, count).ToArray());
            this.active = [];
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            this.active = new byte[Math.Max(1, sizeHint)];
            return this.active;
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            return this.GetMemory(sizeHint).Span;
        }
    }

    private sealed class ShortWindowWriter : IBufferWriter<byte>
    {
        public void Advance(int count)
        {
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            return new byte[Math.Max(0, sizeHint - 1)];
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            return this.GetMemory(sizeHint).Span;
        }
    }
}
