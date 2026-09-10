namespace CStructSharp.Tests;

/// <summary>
///     Exercises <see cref="BinaryPrimitiveIO"/> directly, independent of a compiled <see cref="CStruct"/> layout.
///     Only reachable indirectly through the public API before this type was extracted from the God-Object
///     <c>CStruct</c> partial class.
/// </summary>
[TestClass]
public class BinaryPrimitiveIOTests
{
    /// <summary>Every supported width decodes to the expected unsigned value in the requested byte order.</summary>
    [TestMethod]
    public void ReadUnsignedBySize_SupportedWidths_DecodesInRequestedByteOrder()
    {
        Assert.AreEqual(0x12UL, ReadUnsignedBySize([0x12,], true));
        Assert.AreEqual(0x1234UL, ReadUnsignedBySize([0x34, 0x12,], true));
        Assert.AreEqual(0x1234UL, ReadUnsignedBySize([0x12, 0x34,], false));
        Assert.AreEqual(0x12345678UL, ReadUnsignedBySize([0x78, 0x56, 0x34, 0x12,], true));
        Assert.AreEqual(0x0102030405060708UL, ReadUnsignedBySize([0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,], true));
    }

    /// <summary>A stream that ends before the requested width is filled reports a layout-specific read error.</summary>
    [TestMethod]
    public void ReadUnsignedBySize_StreamEndsEarly_Throws()
    {
        using var stream = new MemoryStream([0x01, 0x02,]);

        Assert.Throws<CStructReadException>(() => BinaryPrimitiveIO.ReadUnsignedBySize(stream, 4, true));
    }

    /// <summary>Every typed reader decodes the expected value in the requested byte order, matching a hand-composed expectation.</summary>
    [TestMethod]
    public void TypedReaders_DecodeInRequestedByteOrder()
    {
        Assert.AreEqual((char)0x0201, ReadTyped(BinaryPrimitiveIO.ReadChar, [0x01, 0x02,], true));
        Assert.AreEqual((char)0x0102, ReadTyped(BinaryPrimitiveIO.ReadChar, [0x01, 0x02,], false));
        Assert.AreEqual((short)0x0201, ReadTyped(BinaryPrimitiveIO.ReadInt16, [0x01, 0x02,], true));
        Assert.AreEqual((short)0x0102, ReadTyped(BinaryPrimitiveIO.ReadInt16, [0x01, 0x02,], false));
        Assert.AreEqual((ushort)0x0201, ReadTyped(BinaryPrimitiveIO.ReadUInt16, [0x01, 0x02,], true));
        Assert.AreEqual(0x04030201, ReadTyped(BinaryPrimitiveIO.ReadInt32, [0x01, 0x02, 0x03, 0x04,], true));
        Assert.AreEqual(0x01020304, ReadTyped(BinaryPrimitiveIO.ReadInt32, [0x01, 0x02, 0x03, 0x04,], false));
        Assert.AreEqual(0x04030201U, ReadTyped(BinaryPrimitiveIO.ReadUInt32, [0x01, 0x02, 0x03, 0x04,], true));
        Assert.AreEqual(
            0x0807060504030201L,
            ReadTyped(BinaryPrimitiveIO.ReadInt64, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,], true));
        Assert.AreEqual(
            0x0807060504030201UL,
            ReadTyped(BinaryPrimitiveIO.ReadUInt64, [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,], true));
    }

    /// <summary>A typed reader also reports the same layout-specific error as the byte-array primitives on a short stream.</summary>
    [TestMethod]
    public void TypedReaders_StreamEndsEarly_Throws()
    {
        using var stream = new MemoryStream([0x01, 0x02,]);

        Assert.Throws<CStructReadException>(() => BinaryPrimitiveIO.ReadInt32(stream, true));
    }

    /// <summary>Every typed reader/writer pair round-trips a value exactly, in both byte orders.</summary>
    [TestMethod]
    public void TypedWriters_RoundTripThroughTypedReaders()
    {
        AssertRoundTrips(BinaryPrimitiveIO.WriteChar, BinaryPrimitiveIO.ReadChar, (char)0x1234);
        AssertRoundTrips(BinaryPrimitiveIO.WriteInt16, BinaryPrimitiveIO.ReadInt16, (short)-1234);
        AssertRoundTrips(BinaryPrimitiveIO.WriteUInt16, BinaryPrimitiveIO.ReadUInt16, (ushort)0xBEEF);
        AssertRoundTrips(BinaryPrimitiveIO.WriteInt32, BinaryPrimitiveIO.ReadInt32, -123456789);
        AssertRoundTrips(BinaryPrimitiveIO.WriteUInt32, BinaryPrimitiveIO.ReadUInt32, 0xDEADBEEFU);
        AssertRoundTrips(BinaryPrimitiveIO.WriteInt64, BinaryPrimitiveIO.ReadInt64, -1234567890123456789L);
        AssertRoundTrips(BinaryPrimitiveIO.WriteUInt64, BinaryPrimitiveIO.ReadUInt64, 0xDEADBEEFCAFEBABEUL);
        AssertRoundTrips(BinaryPrimitiveIO.WriteSingle, BinaryPrimitiveIO.ReadSingle, 3.14159f);
        AssertRoundTrips(BinaryPrimitiveIO.WriteDouble, BinaryPrimitiveIO.ReadDouble, 2.718281828459045);
    }

    private static ulong ReadUnsignedBySize(byte[] bytes, bool isLittleEndian)
    {
        using var stream = new MemoryStream(bytes);
        return BinaryPrimitiveIO.ReadUnsignedBySize(stream, bytes.Length, isLittleEndian);
    }

    private static T ReadTyped<T>(Func<Stream, bool, T> reader, byte[] bytes, bool isLittleEndian)
    {
        using var stream = new MemoryStream(bytes);
        return reader(stream, isLittleEndian);
    }

    private static void AssertRoundTrips<T>(Action<Stream, T, bool> writer, Func<Stream, bool, T> reader, T value)
    {
        foreach (bool isLittleEndian in new[] { true, false, })
        {
            using var stream = new MemoryStream();
            writer(stream, value, isLittleEndian);
            stream.Position = 0;
            Assert.AreEqual(value, reader(stream, isLittleEndian));
        }
    }

    /// <summary>A single available byte is returned exactly.</summary>
    [TestMethod]
    public void ReadByteExactly_ReturnsTheByte()
    {
        using var stream = new MemoryStream([0x7F,]);

        Assert.AreEqual((byte)0x7F, BinaryPrimitiveIO.ReadByteExactly(stream));
    }

    /// <summary>An empty stream cannot supply the required byte.</summary>
    [TestMethod]
    public void ReadByteExactly_EndOfStream_Throws()
    {
        using var stream = new MemoryStream();

        Assert.Throws<CStructReadException>(() => BinaryPrimitiveIO.ReadByteExactly(stream));
    }

    /// <summary>Every supported width decodes to the expected unsigned value in the requested byte order.</summary>
    [TestMethod]
    public void ReadUnsigned_SupportedWidths_DecodesInRequestedByteOrder()
    {
        Assert.AreEqual(0x12UL, BinaryPrimitiveIO.ReadUnsigned([0x12,], true));
        Assert.AreEqual(0x1234UL, BinaryPrimitiveIO.ReadUnsigned([0x34, 0x12,], true));
        Assert.AreEqual(0x1234UL, BinaryPrimitiveIO.ReadUnsigned([0x12, 0x34,], false));
        Assert.AreEqual(0x12345678UL, BinaryPrimitiveIO.ReadUnsigned([0x78, 0x56, 0x34, 0x12,], true));
        Assert.AreEqual(0x0102030405060708UL, BinaryPrimitiveIO.ReadUnsigned([0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,], true));
    }

    /// <summary>The caller's buffer is never reordered in place; only the returned value reflects byte order.</summary>
    [TestMethod]
    public void ReadUnsigned_DoesNotMutateTheCallersBuffer()
    {
        byte[] buffer = [0x34, 0x12,];

        _ = BinaryPrimitiveIO.ReadUnsigned(buffer, !System.BitConverter.IsLittleEndian);

        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, buffer);
    }

    /// <summary>An unsupported buffer length cannot be decoded as one of the four known integer widths.</summary>
    [TestMethod]
    public void ReadUnsigned_UnsupportedLength_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => BinaryPrimitiveIO.ReadUnsigned([0x01, 0x02, 0x03,], true));
    }

    /// <summary>Writing a typed value in little-endian order produces the expected least-significant-byte-first bytes.</summary>
    [TestMethod]
    public void TypedWriters_LittleEndian_WritesLeastSignificantByteFirst()
    {
        using var stream = new MemoryStream();

        BinaryPrimitiveIO.WriteUInt16(stream, 0x0201, true);

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, }, stream.ToArray());
    }

    /// <summary>Writing a typed value in big-endian order produces the expected most-significant-byte-first bytes.</summary>
    [TestMethod]
    public void TypedWriters_BigEndian_WritesMostSignificantByteFirst()
    {
        using var stream = new MemoryStream();

        BinaryPrimitiveIO.WriteUInt16(stream, 0x0201, false);

        CollectionAssert.AreEqual(new byte[] { 0x02, 0x01, }, stream.ToArray());
    }

    /// <summary>Every supported width encodes to the expected bytes in the requested byte order.</summary>
    [TestMethod]
    public void WriteUnsigned_SupportedWidths_EncodesInRequestedByteOrder()
    {
        CollectionAssert.AreEqual(new byte[] { 0x12, }, BinaryPrimitiveIO.WriteUnsigned(0x12, 1, true));
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, BinaryPrimitiveIO.WriteUnsigned(0x1234, 2, true));
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, }, BinaryPrimitiveIO.WriteUnsigned(0x1234, 2, false));
        CollectionAssert.AreEqual(
            new byte[] { 0x78, 0x56, 0x34, 0x12, },
            BinaryPrimitiveIO.WriteUnsigned(0x12345678, 4, true));
        CollectionAssert.AreEqual(
            new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, },
            BinaryPrimitiveIO.WriteUnsigned(0x0102030405060708, 8, true));
    }

    /// <summary>An unsupported byte size cannot be encoded as one of the four known integer widths.</summary>
    [TestMethod]
    public void WriteUnsigned_UnsupportedSize_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => BinaryPrimitiveIO.WriteUnsigned(1, 3, true));
    }
}
