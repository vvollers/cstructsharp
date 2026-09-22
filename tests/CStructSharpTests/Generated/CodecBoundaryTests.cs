namespace CStructSharp.Tests.Generated;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Checks generated codec boundaries that callers can reach without a generated record wrapper.</summary>
[TestClass]
public class CodecBoundaryTests
{
    /// <summary>Wide text keeps byte order, validates surrogate pairs and trims only when explicitly requested.</summary>
    /// <param name="littleEndian">The input code-unit byte order.</param>
    /// <param name="trim">Whether fixed trailing NUL characters should be removed.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void WideText_ValidatesAndAppliesTheRequestedTrimming(bool littleEndian, bool trim)
    {
        Encoding encoding = littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode;
        byte[] bytes = encoding.GetBytes("AΣ\0");
        var options = new ReadOptions { TrimFixedText = trim, };
        Assert.AreEqual(trim ? "AΣ" : "AΣ\0", Codec.DecodeWideText(bytes, littleEndian, options));
        Assert.AreEqual("AΣ\0", Codec.DecodeWideText(bytes, littleEndian, null));
        Assert.AreEqual(trim, Codec.TrimsFixedText(options));
        Assert.IsFalse(Codec.TrimsFixedText(null));
        byte[] invalid = littleEndian ? new byte[] { 0, 0xD8, } : new byte[] { 0xD8, 0, };

        // A lone high surrogate is not valid UTF-16, even when trimming is enabled.
        CStructReadException failure = Assert.Throws<CStructReadException>(() => Codec.DecodeWideText(invalid, littleEndian, options));
        StringAssert.StartsWith(failure.Message, "Wide-character buffer contains an invalid UTF-16 code-unit sequence.");
        Assert.IsInstanceOfType<EncoderFallbackException>(failure.InnerException);
    }

    /// <summary>The minimum signed fixed-point value is accepted exactly and the next lower grid point is rejected.</summary>
    [TestMethod]
    public void FixedPoint_AcceptsItsMinimumAndRejectsTheNextGridPoint()
    {
        Assert.AreEqual((long)int.MinValue, Codec.EncodeFixedPoint(-32768m, 32, 16, true));
        Assert.AreEqual((long)int.MinValue, Codec.EncodeFixedPoint(-32768d, 32, 16, true));

        // Changing the signed-domain exponent would incorrectly permit this representable but out-of-domain grid value.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => Codec.EncodeFixedPoint(-32768.5m, 32, 16, true));
        StringAssert.StartsWith(failure.Message, "Fixed-point value is outside the exact storage grid or range.");
        Assert.AreEqual((byte)255, Codec.ToNarrowCharacter('\u00FF'));
    }

    /// <summary>The signed 48-bit codec accepts both inclusive endpoints in either byte order.</summary>
    /// <param name="littleEndian">The output byte order.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Signed48_AcceptsBothEndpoints(bool littleEndian)
    {
        byte[] bytes = new byte[6];
        Codec.WriteInt48(bytes, 140_737_488_355_327, littleEndian);
        CollectionAssert.AreEqual(Convert.FromHexString(littleEndian ? "FFFFFFFFFF7F" : "7FFFFFFFFFFF"), bytes);
        Assert.AreEqual(140_737_488_355_327L, Codec.ReadInt48(bytes, littleEndian));
        Codec.WriteInt48(bytes, -140_737_488_355_328, littleEndian);
        CollectionAssert.AreEqual(Convert.FromHexString(littleEndian ? "000000000080" : "800000000000"), bytes);
        Assert.AreEqual(-140_737_488_355_328L, Codec.ReadInt48(bytes, littleEndian));
    }

    /// <summary>Bulk eight-byte encoding reverses each element independently when the requested order differs from the host.</summary>
    [TestMethod]
    public void Encode64_ReversesEveryElementWhenRequired()
    {
        ulong[] values = new ulong[] { 0x0102030405060708UL, 0x1112131415161718UL, };
        byte[] bytes = new byte[16];
        Codec.EncodeIntegers<ulong>(values, bytes, littleEndian: true);
        CollectionAssert.AreEqual(Convert.FromHexString("08070605040302011817161514131211"), bytes);
        Codec.EncodeIntegers<ulong>(values, bytes, littleEndian: false);
        CollectionAssert.AreEqual(Convert.FromHexString("01020304050607081112131415161718"), bytes);
    }

    /// <summary>Unsupported storage widths report the requested byte size for both scalar and bulk paths.</summary>
    [TestMethod]
    public void UnsupportedWidths_IdentifyTheRequestedSize()
    {
        foreach (int size in new[] { 0, 9, })
        {
            // Packed scalar windows accept one through eight bytes, not empty or wider buffers.
            Assert.AreEqual("Unsupported integer size: " + size, Assert.Throws<InvalidOperationException>(() => Codec.ReadUnsigned(new byte[size], true)).Message);
            Assert.AreEqual("Unsupported integer size: " + size, Assert.Throws<InvalidOperationException>(() => Codec.WriteUnsigned(new byte[size], 1, true)).Message);
        }

        // The vectorized endian-switch path supports only its documented one/two/four/eight-byte element widths.
        Assert.AreEqual("Unsupported integer element size: 16", Assert.Throws<InvalidOperationException>(() => Codec.DecodeIntegers<Int128>(new byte[16], new Int128[1], !BitConverter.IsLittleEndian)).Message);
        Assert.AreEqual("Unsupported integer element size: 16", Assert.Throws<InvalidOperationException>(() => Codec.EncodeIntegers<Int128>(new Int128[1], new byte[16], !BitConverter.IsLittleEndian)).Message);
    }
}
