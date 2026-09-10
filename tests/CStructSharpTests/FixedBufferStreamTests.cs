namespace CStructSharp.Tests;

/// <summary>Checks the fixed caller-memory stream used by synchronous span and memory operations.</summary>
[TestClass]
public class FixedBufferStreamTests
{
    /// <summary>
    ///     The adapter exposes four existing caller-owned bytes as a readable, seekable, non-writable stream.
    /// </summary>
    /// <remarks>
    ///     Array, span, and single-byte reads must return 1,2,3,4 in order, then report end-of-stream. Seeking must use
    ///     the region's own coordinates, matching the behavior expected by memory-based parser APIs.
    /// </remarks>
    [TestMethod]
    public unsafe void ReadOnlyRegion_ExposesInitializedBytesAndStreamCapabilities()
    {
        byte[] storage = [1, 2, 3, 4,];
        fixed (byte* buffer = storage)
        {
            using var stream = new FixedBufferStream(buffer, storage.Length, writable: false);

            Assert.IsTrue(stream.CanRead);
            Assert.IsTrue(stream.CanSeek);
            Assert.IsFalse(stream.CanWrite);
            Assert.AreEqual(4L, stream.Length);
            Assert.AreEqual(0L, stream.Position);
            stream.Flush();

            byte[] arrayDestination = [0xA5, 0xA5, 0xA5, 0xA5,];
            Assert.AreEqual(2, stream.Read(arrayDestination, 1, 2));
            CollectionAssert.AreEqual(new byte[] { 0xA5, 1, 2, 0xA5, }, arrayDestination);

            Span<byte> spanDestination = stackalloc byte[3];
            Assert.AreEqual(2, stream.Read(spanDestination));
            CollectionAssert.AreEqual(new byte[] { 3, 4, 0, }, spanDestination.ToArray());
            Assert.AreEqual(-1, stream.ReadByte());
            Assert.AreEqual(0, stream.Read(Span<byte>.Empty));

            Assert.AreEqual(1L, stream.Seek(1, SeekOrigin.Begin));
            Assert.AreEqual(2, stream.ReadByte());
            Assert.AreEqual(3L, stream.Seek(1, SeekOrigin.Current));
            Assert.AreEqual(3L, stream.Seek(-1, SeekOrigin.End));
            Assert.AreEqual(4, stream.ReadByte());
        }
    }

    /// <summary>
    ///     A fixed read-only region cannot be written, resized, or positioned outside its bounds.
    /// </summary>
    /// <remarks>
    ///     Invalid arrays, ranges, seek origins, and overflowing offsets must raise the appropriate errors. These
    ///     checks protect the supplied memory region before layout readers use its unsafe internal pointer.
    /// </remarks>
    [TestMethod]
    public unsafe void ReadOnlyRegion_RejectsInvalidOperations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(ConstructWithNegativeCapacity);

        byte[] storage = [1, 2, 3, 4,];
        fixed (byte* buffer = storage)
        {
            using var stream = new FixedBufferStream(buffer, storage.Length, writable: false);
            byte[] array = [1, 2, 3,];

            Assert.Throws<ArgumentNullException>(() => stream.Read(null!, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(array, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(array, 0, -1));
            Assert.Throws<ArgumentException>(() => stream.Read(array, 2, 2));

            Assert.Throws<ArgumentNullException>(() => stream.Write(null!, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Write(array, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Write(array, 0, -1));
            Assert.Throws<ArgumentException>(() => stream.Write(array, 2, 2));
            Assert.Throws<NotSupportedException>(() => stream.Write(array, 0, 1));
            Assert.Throws<NotSupportedException>(() => stream.SetLength(1));

            Assert.Throws<CStructReadException>(() => stream.Position = -1);
            Assert.Throws<CStructReadException>(() => stream.Position = storage.Length + 1L);
            Assert.Throws<CStructReadException>(() => stream.Seek(-1, SeekOrigin.Begin));
            Assert.Throws<CStructReadException>(() => stream.Seek(1, SeekOrigin.End));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)99));

            stream.Position = 1;
            CStructReadException overflow = Assert.Throws<CStructReadException>(
                () => stream.Seek(long.MaxValue, SeekOrigin.Current));
            Assert.IsInstanceOfType<OverflowException>(overflow.InnerException);
        }
    }

    /// <summary>
    ///     The writable region begins with capacity but no initialized output.
    /// </summary>
    /// <remarks>
    ///     Writing through position 7 must produce 1,2,3,0,0,4,5 while leaving unused capacity as 0xA5. Growth must
    ///     zero new bytes, and shrinking must reduce the visible length and clamp the cursor without exposing old
    ///     capacity as valid output.
    /// </remarks>
    [TestMethod]
    public unsafe void WritableRegion_WritesReadsAndClearsNewlyInitializedBytes()
    {
        byte[] storage = Enumerable.Repeat((byte)0xA5, 10).ToArray();
        fixed (byte* buffer = storage)
        {
            using var stream = new FixedBufferStream(buffer, storage.Length, writable: true);

            Assert.IsTrue(stream.CanRead);
            Assert.IsTrue(stream.CanSeek);
            Assert.IsTrue(stream.CanWrite);
            Assert.AreEqual(0L, stream.Length);

            byte[] source = [0xEE, 1, 2, 0xDD,];
            stream.Write(source, 1, 2);
            stream.WriteByte(3);
            stream.Position = 5;
            stream.Write(new byte[] { 4, 5, }.AsSpan());

            Assert.AreEqual(7L, stream.Length);
            Assert.AreEqual(7L, stream.Position);
            CollectionAssert.AreEqual(
                new byte[] { 1, 2, 3, 0, 0, 4, 5, 0xA5, 0xA5, 0xA5, },
                storage);

            stream.Position = 0;
            Span<byte> actual = stackalloc byte[7];
            Assert.AreEqual(7, stream.Read(actual));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 0, 0, 4, 5, }, actual.ToArray());

            stream.SetLength(9);
            Assert.AreEqual(9L, stream.Length);
            CollectionAssert.AreEqual(
                new byte[] { 1, 2, 3, 0, 0, 4, 5, 0, 0, 0xA5, },
                storage);

            stream.Position = 9;
            stream.SetLength(4);
            Assert.AreEqual(4L, stream.Length);
            Assert.AreEqual(4L, stream.Position);
            Assert.AreEqual(-1, stream.ReadByte());
        }
    }

    /// <summary>
    ///     After writing three bytes into a four-byte region, a two-byte write cannot fit and must leave the prefix
    ///     1,2,3 unchanged.
    /// </summary>
    /// <remarks>
    ///     Negative or oversized lengths and writes at full capacity must also fail. The adapter must never write
    ///     beyond caller-owned memory or extend its fixed capacity.
    /// </remarks>
    [TestMethod]
    public unsafe void WritableRegion_RejectsBoundsAndCapacityFailures()
    {
        byte[] storage = [0xA5, 0xA5, 0xA5, 0xA5,];
        fixed (byte* buffer = storage)
        {
            using var stream = new FixedBufferStream(buffer, storage.Length, writable: true);

            Assert.Throws<CStructWriteException>(() => stream.SetLength(-1));
            Assert.Throws<CStructWriteException>(() => stream.SetLength(storage.Length + 1L));

            stream.Write(new byte[] { 1, 2, 3, }, 0, 3);
            stream.Position = 3;
            Assert.Throws<CStructWriteException>(() => stream.Write(new byte[] { 4, 5, }, 0, 2));
            Assert.AreEqual(3L, stream.Length);
            Assert.AreEqual(3L, stream.Position);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 0xA5, }, storage);

            stream.Position = storage.Length;
            Assert.Throws<CStructWriteException>(() => stream.WriteByte(4));
            stream.SetLength(storage.Length);
            Assert.AreEqual(storage.Length, stream.Position);
        }
    }

    private static unsafe void ConstructWithNegativeCapacity()
    {
        _ = new FixedBufferStream((byte*)0, -1, writable: false);
    }
}
