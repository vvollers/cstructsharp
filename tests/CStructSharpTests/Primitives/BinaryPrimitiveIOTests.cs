namespace CStructSharp.Tests;

using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>
///     Exercises <see cref="BinaryPrimitiveIO"/> directly, independent of a compiled <see cref="CStruct"/> layout.
/// </summary>
[TestClass]
public class BinaryPrimitiveIOTests
{
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
    public void ReadUnsigned_OddLengths_AreBitfieldWindows_AndZeroOrNineThrow()
    {
        // A packed SysV bitfield window can be any width up to eight bytes, in either byte order.
        Assert.AreEqual(0x030201UL, BinaryPrimitiveIO.ReadUnsigned([0x01, 0x02, 0x03,], true));
        Assert.AreEqual(0x010203UL, BinaryPrimitiveIO.ReadUnsigned([0x01, 0x02, 0x03,], false));
        Assert.AreEqual(0x0706050403020100UL >> 8, BinaryPrimitiveIO.ReadUnsigned([0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,], true));
        Assert.Throws<InvalidOperationException>(() => BinaryPrimitiveIO.ReadUnsigned([], true));
        Assert.Throws<InvalidOperationException>(() => BinaryPrimitiveIO.ReadUnsigned(new byte[9], true));
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
        CollectionAssert.AreEqual(new byte[] { 0x12, }, WriteUnsigned(0x12, 1, true));
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, WriteUnsigned(0x1234, 2, true));
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, }, WriteUnsigned(0x1234, 2, false));
        CollectionAssert.AreEqual(
            new byte[] { 0x78, 0x56, 0x34, 0x12, },
            WriteUnsigned(0x12345678, 4, true));
        CollectionAssert.AreEqual(
            new byte[] { 0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, },
            WriteUnsigned(0x0102030405060708, 8, true));
    }

    /// <summary>An odd width is a bitfield window; zero and nine bytes cannot be encoded.</summary>
    [TestMethod]
    public void WriteUnsigned_OddSizes_AreBitfieldWindows_AndZeroOrNineThrow()
    {
        CollectionAssert.AreEqual(new byte[] { 0x01, 0x02, 0x03 }, WriteUnsigned(0x030201, 3, true));
        CollectionAssert.AreEqual(new byte[] { 0x03, 0x02, 0x01 }, WriteUnsigned(0x030201, 3, false));
        Assert.Throws<InvalidOperationException>(() => WriteUnsigned(1, 0, true));
        Assert.Throws<InvalidOperationException>(() => WriteUnsigned(1, 9, true));
    }

    /// <summary>Encodes one value through the stream helper and returns the bytes written.</summary>
    /// <param name="value">The value.</param>
    /// <param name="byteSize">The width in bytes.</param>
    /// <param name="littleEndian">Whether to write little-endian.</param>
    /// <returns>The bytes.</returns>
    private static byte[] WriteUnsigned(ulong value, int byteSize, bool littleEndian)
    {
        using var stream = new MemoryStream();
        BinaryPrimitiveIO.WriteUnsigned(stream, value, byteSize, littleEndian);
        return stream.ToArray();
    }
}
