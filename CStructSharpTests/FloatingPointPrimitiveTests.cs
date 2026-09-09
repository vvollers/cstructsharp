namespace CStructSharp.Tests;

using System;
using System.IO;

/// <summary>
///     Verifies the floating-point primitive codecs (LANG-04, ADR-015): <c>float32</c>/<c>float64</c> (with
///     <c>float</c>/<c>double</c> as aliases) bit-reinterpret rather than numerically or textually convert, so
///     every representable IEEE-754 bit pattern - including NaN payloads, subnormals, and negative zero - round
///     trips exactly with no special-case behavior.
/// </summary>
[TestClass]
public class FloatingPointPrimitiveTests
{
    /// <summary>A hand-chosen quiet-NaN payload for float32 round-trips through its exact bit pattern, not a canonical NaN.</summary>
    [TestMethod]
    public void Float32_QuietNaNPayloadRoundTripsExactly()
    {
        var cstruct = new CStruct("struct root { float32 value; };", pointerSize: 1);
        float nan = BitConverter.Int32BitsToSingle(0x7FC00123);

        byte[] bytes = cstruct.Serialize("root", new { value = nan, });
        dynamic parsed = cstruct.ParseStream(new MemoryStream(bytes), "root");

        Assert.AreEqual(0x7FC00123, BitConverter.SingleToInt32Bits((float)parsed.value));
    }

    /// <summary>A hand-chosen NaN payload for float64 round-trips through its exact bit pattern, not a canonical NaN.</summary>
    [TestMethod]
    public void Float64_NaNPayloadRoundTripsExactly()
    {
        var cstruct = new CStruct("struct root { float64 value; };", pointerSize: 1);
        double nan = BitConverter.Int64BitsToDouble(0x7FF8000000ABCDEF);

        byte[] bytes = cstruct.Serialize("root", new { value = nan, });
        dynamic parsed = cstruct.ParseStream(new MemoryStream(bytes), "root");

        Assert.AreEqual(0x7FF8000000ABCDEF, BitConverter.DoubleToInt64Bits((double)parsed.value));
    }

    /// <summary>Negative zero round-trips as its own distinct bit pattern for float32, unlike a CLR `==` comparison.</summary>
    [TestMethod]
    public void Float32_NegativeZeroRoundTripsAsDistinctBitPattern()
    {
        var cstruct = new CStruct("struct root { float32 value; };", pointerSize: 1);

        byte[] bytes = cstruct.Serialize("root", new { value = -0.0f, });
        dynamic parsed = cstruct.ParseStream(new MemoryStream(bytes), "root");

        float result = (float)parsed.value;
        Assert.IsTrue(result == 0.0f);
        Assert.AreEqual(unchecked((int)0x80000000), BitConverter.SingleToInt32Bits(result));
    }

    /// <summary>Negative zero round-trips as its own distinct bit pattern for float64, unlike a CLR `==` comparison.</summary>
    [TestMethod]
    public void Float64_NegativeZeroRoundTripsAsDistinctBitPattern()
    {
        var cstruct = new CStruct("struct root { float64 value; };", pointerSize: 1);

        byte[] bytes = cstruct.Serialize("root", new { value = -0.0, });
        dynamic parsed = cstruct.ParseStream(new MemoryStream(bytes), "root");

        double result = (double)parsed.value;
        Assert.IsTrue(result == 0.0);
        Assert.AreEqual(unchecked((long)0x8000000000000000), BitConverter.DoubleToInt64Bits(result));
    }

    /// <summary>The smallest positive subnormal float32 value round-trips through its exact bit pattern.</summary>
    [TestMethod]
    public void Float32_SubnormalRoundTripsExactly()
    {
        var cstruct = new CStruct("struct root { float32 value; };", pointerSize: 1);
        float subnormal = BitConverter.Int32BitsToSingle(0x00000001);

        byte[] bytes = cstruct.Serialize("root", new { value = subnormal, });
        dynamic parsed = cstruct.ParseStream(new MemoryStream(bytes), "root");

        Assert.AreEqual(0x00000001, BitConverter.SingleToInt32Bits((float)parsed.value));
    }

    /// <summary>The smallest positive subnormal float64 value round-trips through its exact bit pattern.</summary>
    [TestMethod]
    public void Float64_SubnormalRoundTripsExactly()
    {
        var cstruct = new CStruct("struct root { float64 value; };", pointerSize: 1);
        double subnormal = BitConverter.Int64BitsToDouble(0x0000000000000001);

        byte[] bytes = cstruct.Serialize("root", new { value = subnormal, });
        dynamic parsed = cstruct.ParseStream(new MemoryStream(bytes), "root");

        Assert.AreEqual(0x0000000000000001, BitConverter.DoubleToInt64Bits((double)parsed.value));
    }

    /// <summary>
    ///     Explicit endian suffixes produce the documented byte order independently of the layout's own byte
    ///     order. 1.5f's IEEE-754 binary32 bit pattern is 0x3FC00000, so its little-endian bytes are
    ///     00 00 C0 3F and its big-endian bytes are 3F C0 00 00 - hardcoded here rather than derived from
    ///     <c>BitConverter</c>, so the expected bytes do not depend on the test host's own endianness.
    /// </summary>
    [TestMethod]
    public void ExplicitEndianSuffix_ProducesTheDocumentedByteOrder()
    {
        var cstruct = new CStruct(
            "struct root { float32< littleValue; float32> bigValue; };",
            pointerSize: 1,
            isLittleEndian: false);

        byte[] bytes = cstruct.Serialize("root", new { littleValue = 1.5f, bigValue = 1.5f, });

        CollectionAssert.AreEqual(new byte[] { 0x00, 0x00, 0xC0, 0x3F, 0x3F, 0xC0, 0x00, 0x00, }, bytes);
    }

    /// <summary>The full write/serialize/read-value/update operation set is covered end to end for both widths.</summary>
    [TestMethod]
    public void FullRoundTrip_WriteSerializeReadValueUpdate()
    {
        var cstruct = new CStruct("struct root { float32 a; float64 b; };", pointerSize: 1);

        using var writeStream = new MemoryStream();
        cstruct.WriteStream(writeStream, "root", new { a = 1.5f, b = 2.5, });
        byte[] expected = cstruct.Serialize("root", new { a = 1.5f, b = 2.5, });
        CollectionAssert.AreEqual(expected, writeStream.ToArray());

        using var readStream = new MemoryStream(expected);
        Assert.AreEqual(1.5f, Convert.ToSingle(cstruct.ReadValue(readStream, "root.a")));

        using var updateStream = new MemoryStream(expected);
        cstruct.UpdateStream(updateStream, "root.a", 9.5f);
        dynamic updated = cstruct.ParseStream(new MemoryStream(updateStream.ToArray()), "root");
        Assert.AreEqual(9.5f, (float)updated.a);
        Assert.AreEqual(2.5, (double)updated.b);
    }

    /// <summary>A float32 array behaves like an ordinary fixed array of independent elements.</summary>
    [TestMethod]
    public void Float32Array_ElementsAreIndependent()
    {
        var cstruct = new CStruct("struct root { float32 values[2]; };", pointerSize: 1);

        byte[] bytes = cstruct.Serialize("root", new { values = new[] { 1.5f, -2.5f, }, });
        dynamic parsed = cstruct.ParseStream(new MemoryStream(bytes), "root");

        Assert.AreEqual(1.5f, (float)parsed.values[0]);
        Assert.AreEqual(-2.5f, (float)parsed.values[1]);
    }

    /// <summary>The <c>float</c> alias compiles identically to <c>float32</c>.</summary>
    [TestMethod]
    public void FloatAlias_CompilesIdenticallyToFloat32()
    {
        var alias = new CStruct("struct root { float value; };", pointerSize: 1);
        var canonical = new CStruct("struct root { float32 value; };", pointerSize: 1);

        Assert.AreEqual(canonical.GetStructSizeInBytes("root"), alias.GetStructSizeInBytes("root"));
        Assert.AreEqual(canonical.GetStructAlignmentInBytes("root"), alias.GetStructAlignmentInBytes("root"));

        dynamic parsed = alias.ParseStream(new MemoryStream(BitConverter.GetBytes(1.5f)), "root");
        Assert.AreEqual(1.5f, (float)parsed.value);
    }

    /// <summary>The <c>double</c> alias compiles identically to <c>float64</c>.</summary>
    [TestMethod]
    public void DoubleAlias_CompilesIdenticallyToFloat64()
    {
        var alias = new CStruct("struct root { double value; };", pointerSize: 1);
        var canonical = new CStruct("struct root { float64 value; };", pointerSize: 1);

        Assert.AreEqual(canonical.GetStructSizeInBytes("root"), alias.GetStructSizeInBytes("root"));
        Assert.AreEqual(canonical.GetStructAlignmentInBytes("root"), alias.GetStructAlignmentInBytes("root"));

        dynamic parsed = alias.ParseStream(new MemoryStream(BitConverter.GetBytes(2.5)), "root");
        Assert.AreEqual(2.5, (double)parsed.value);
    }

    /// <summary>float32 is not eligible as bitfield storage - a bit-width slice of a float has no IEEE-754 meaning.</summary>
    [TestMethod]
    public void Float32AsBitfieldStorage_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { float32 value : 4; };", pointerSize: 1));
    }

    /// <summary>float32 is not eligible as enum backing - enum members are discrete integral constants in this model.</summary>
    [TestMethod]
    public void Float32AsEnumBacking_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("enum mode : float32 { off = 0, on = 1 };"));
    }

    /// <summary>
    ///     Supplying a differently-sized CLR floating value into a field performs a real numeric narrowing
    ///     conversion (<c>Convert.ToSingle</c>), matching every other numeric codec's generous-input convention -
    ///     it is accepted, not rejected.
    /// </summary>
    [TestMethod]
    public void NarrowingConversion_DoubleValueIntoFloat32Field_IsAccepted()
    {
        var cstruct = new CStruct("struct root { float32 value; };", pointerSize: 1);

        byte[] bytes = cstruct.Serialize("root", new { value = 1.5, });
        dynamic parsed = cstruct.ParseStream(new MemoryStream(bytes), "root");

        Assert.AreEqual(1.5f, (float)parsed.value);
    }

    /// <summary>long double remains rejected - no single portable width exists to standardize on.</summary>
    [TestMethod]
    public void LongDouble_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { long double value; };"));
    }
}
