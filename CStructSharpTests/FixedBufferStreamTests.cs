namespace CStructSharp.Tests;

/// <summary>Checks the fixed caller-memory stream used by synchronous span and memory operations.</summary>
[TestClass]
public class FixedBufferStreamTests
{
    /// <summary>Reads an initialized region through the array, span, byte, seek, and position contracts.</summary>
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

    /// <summary>Rejects invalid fixed-region construction, positions, seeks, ranges, and read-only mutations.</summary>
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

            Assert.Throws<IOException>(() => stream.Position = -1);
            Assert.Throws<IOException>(() => stream.Position = storage.Length + 1L);
            Assert.Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin));
            Assert.Throws<IOException>(() => stream.Seek(1, SeekOrigin.End));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)99));

            stream.Position = 1;
            IOException overflow = Assert.Throws<IOException>(
                () => stream.Seek(long.MaxValue, SeekOrigin.Current));
            Assert.IsInstanceOfType<OverflowException>(overflow.InnerException);
        }
    }

    /// <summary>Tracks the initialized prefix, clears forward gaps/growth, and preserves unused capacity.</summary>
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

    /// <summary>Rejects writable-region length, position, and capacity overflow without changing its prefix.</summary>
    [TestMethod]
    public unsafe void WritableRegion_RejectsBoundsAndCapacityFailures()
    {
        byte[] storage = [0xA5, 0xA5, 0xA5, 0xA5,];
        fixed (byte* buffer = storage)
        {
            using var stream = new FixedBufferStream(buffer, storage.Length, writable: true);

            Assert.Throws<IOException>(() => stream.SetLength(-1));
            Assert.Throws<IOException>(() => stream.SetLength(storage.Length + 1L));

            stream.Write(new byte[] { 1, 2, 3, }, 0, 3);
            stream.Position = 3;
            Assert.Throws<IOException>(() => stream.Write(new byte[] { 4, 5, }, 0, 2));
            Assert.AreEqual(3L, stream.Length);
            Assert.AreEqual(3L, stream.Position);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 0xA5, }, storage);

            stream.Position = storage.Length;
            Assert.Throws<IOException>(() => stream.WriteByte(4));
            stream.SetLength(storage.Length);
            Assert.AreEqual(storage.Length, stream.Position);
        }
    }

    private static unsafe void ConstructWithNegativeCapacity()
    {
        _ = new FixedBufferStream((byte*)0, -1, writable: false);
    }
}
