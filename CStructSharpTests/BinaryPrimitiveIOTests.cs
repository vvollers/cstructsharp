namespace CStructSharp.Tests;

/// <summary>
///     Exercises <see cref="BinaryPrimitiveIO"/> directly, independent of a compiled <see cref="CStruct"/> layout.
///     Only reachable indirectly through the public API before this type was extracted from the God-Object
///     <c>CStruct</c> partial class.
/// </summary>
[TestClass]
public class BinaryPrimitiveIOTests
{
    /// <summary>A read matching the machine's own byte order returns the stream bytes unchanged.</summary>
    [TestMethod]
    public void ReadIntoBuffer_MatchingEndianness_ReturnsBytesAsIs()
    {
        using var stream = new MemoryStream([0x01, 0x02, 0x03, 0x04,]);

        byte[] result = BinaryPrimitiveIO.ReadIntoBuffer(stream, 4, System.BitConverter.IsLittleEndian).ToArray();

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03, 0x04, }, result);
    }

    /// <summary>A read requesting the opposite byte order reverses the bytes read from the stream.</summary>
    [TestMethod]
    public void ReadIntoBuffer_MismatchedEndianness_ReversesBytes()
    {
        using var stream = new MemoryStream([0x01, 0x02, 0x03, 0x04,]);

        byte[] result = BinaryPrimitiveIO.ReadIntoBuffer(stream, 4, !System.BitConverter.IsLittleEndian).ToArray();

        CollectionAssert.AreEqual(new byte[] { 0x04, 0x03, 0x02, 0x01, }, result);
    }

    /// <summary>A stream that ends before the requested width is filled reports a layout-specific read error.</summary>
    [TestMethod]
    public void ReadIntoBuffer_StreamEndsEarly_Throws()
    {
        using var stream = new MemoryStream([0x01, 0x02,]);

        Assert.Throws<CStructReadException>(() => BinaryPrimitiveIO.ReadIntoBuffer(stream, 4, true));
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

    /// <summary>Writing with the machine's own byte order leaves the source bytes unchanged on the stream.</summary>
    [TestMethod]
    public void WriteEndianBytes_MatchingEndianness_WritesAsIs()
    {
        using var stream = new MemoryStream();

        BinaryPrimitiveIO.WriteEndianBytes(stream, [0x01, 0x02,], System.BitConverter.IsLittleEndian);

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, }, stream.ToArray());
    }

    /// <summary>Writing with the opposite byte order reverses the bytes before they reach the stream.</summary>
    [TestMethod]
    public void WriteEndianBytes_MismatchedEndianness_ReversesBeforeWriting()
    {
        using var stream = new MemoryStream();

        BinaryPrimitiveIO.WriteEndianBytes(stream, [0x01, 0x02,], !System.BitConverter.IsLittleEndian);

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
