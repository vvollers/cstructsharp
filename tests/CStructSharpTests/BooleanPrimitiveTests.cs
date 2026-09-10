namespace CStructSharp.Tests;

using System;
using System.IO;

/// <summary>
///     Verifies the boolean primitive codec (LANG-04 bool split-out): any nonzero byte reads as true, zero reads as
///     false, and every write/serialize/update always produces the canonical 0x00/0x01 byte regardless of what was
///     previously stored.
/// </summary>
[TestClass]
public class BooleanPrimitiveTests
{
    /// <summary>The two canonical byte values parse as their matching boolean value.</summary>
    [TestMethod]
    public void ZeroAndOne_ParseAsFalseAndTrue()
    {
        var cstruct = new CStruct("struct root { bool value; };");

        dynamic falseParsed = cstruct.ParseStream(new MemoryStream([0x00,]), "root");
        dynamic trueParsed = cstruct.ParseStream(new MemoryStream([0x01,]), "root");

        Assert.AreEqual(false, (bool)falseParsed.value);
        Assert.AreEqual(true, (bool)trueParsed.value);
        Assert.AreEqual(1, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>A non-canonical nonzero byte still reads as true, matching C's own "if (x)" truthiness rule.</summary>
    [TestMethod]
    public void NonCanonicalNonzeroByte_ParsesAsTrue()
    {
        var cstruct = new CStruct("struct root { bool value; };");

        dynamic parsed = cstruct.ParseStream(new MemoryStream([0x05,]), "root");

        Assert.AreEqual(true, (bool)parsed.value);
    }

    /// <summary>Serialize always produces the canonical 0x00/0x01 byte for false/true.</summary>
    [TestMethod]
    public void Serialize_AlwaysProducesTheCanonicalByte()
    {
        var cstruct = new CStruct("struct root { bool value; };");

        byte[] trueBytes = cstruct.Serialize("root", new { value = true, });
        byte[] falseBytes = cstruct.Serialize("root", new { value = false, });

        CollectionAssert.AreEqual(new byte[] { 1, }, trueBytes);
        CollectionAssert.AreEqual(new byte[] { 0, }, falseBytes);
    }

    /// <summary>An update always canonicalizes, even when the byte it replaces was itself non-canonical.</summary>
    [TestMethod]
    public void UpdateStream_CanonicalizesEvenOverANonCanonicalByte()
    {
        var cstruct = new CStruct("struct root { bool value; };");
        using var stream = new MemoryStream([0x05,]);

        cstruct.UpdateStream(stream, "root.value", true);

        CollectionAssert.AreEqual(new byte[] { 1, }, stream.ToArray());
    }

    /// <summary>The full write/serialize/read-value/update operation set is covered end to end.</summary>
    [TestMethod]
    public void FullRoundTrip_WriteSerializeReadValueUpdate()
    {
        var cstruct = new CStruct("struct root { bool value; };");

        using var writeStream = new MemoryStream();
        cstruct.WriteStream(writeStream, "root", new { value = true, });
        CollectionAssert.AreEqual(new byte[] { 1, }, writeStream.ToArray());

        byte[] bytes = cstruct.Serialize("root", new { value = true, });
        CollectionAssert.AreEqual(new byte[] { 1, }, bytes);

        using var readStream = new MemoryStream(bytes);
        Assert.AreEqual(true, Convert.ToBoolean(cstruct.ReadValue(readStream, "root.value")));

        using var updateStream = new MemoryStream(bytes);
        cstruct.UpdateStream(updateStream, "root.value", false);
        CollectionAssert.AreEqual(new byte[] { 0, }, updateStream.ToArray());
    }

    /// <summary>A bool array behaves like an ordinary fixed array of independent elements, not a character buffer.</summary>
    [TestMethod]
    public void BoolArray_ElementsAreIndependent()
    {
        var cstruct = new CStruct("struct root { bool flags[3]; };");

        dynamic parsed = cstruct.ParseStream(new MemoryStream([1, 0, 1,]), "root");

        Assert.AreEqual(true, (bool)parsed.flags[0]);
        Assert.AreEqual(false, (bool)parsed.flags[1]);
        Assert.AreEqual(true, (bool)parsed.flags[2]);
    }

    /// <summary>The C99 <c>_Bool</c> keyword spelling is an alias that compiles identically to <c>bool</c>.</summary>
    [TestMethod]
    public void UnderscoreBoolAlias_CompilesIdenticallyToBool()
    {
        var cstruct = new CStruct("struct root { _Bool value; };");

        dynamic parsed = cstruct.ParseStream(new MemoryStream([1,]), "root");

        Assert.AreEqual(true, (bool)parsed.value);
        Assert.AreEqual(1, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>Bool is not eligible as bitfield storage - a width-1 integral bitfield already covers that case.</summary>
    [TestMethod]
    public void BoolAsBitfieldStorage_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { bool value : 1; };"));
    }

    /// <summary>
    ///     Byte order does not apply to a 1-byte primitive - both endiannesses must produce identical bytes, proving
    ///     no <c>bool&lt;</c>/<c>bool&gt;</c> suffix behavior leaked in accidentally.
    /// </summary>
    [TestMethod]
    public void Endianness_HasNoEffectOnBool()
    {
        var little = new CStruct("struct root { bool value; };", isLittleEndian: true);
        var big = new CStruct("struct root { bool value; };", isLittleEndian: false);

        byte[] littleBytes = little.Serialize("root", new { value = true, });
        byte[] bigBytes = big.Serialize("root", new { value = true, });

        CollectionAssert.AreEqual(littleBytes, bigBytes);
    }

    /// <summary>Both packed and aligned placement produce the same 1-byte-wide, 1-byte-aligned field.</summary>
    [TestMethod]
    public void AlignedAndPacked_BothPlaceBoolAsOneByte()
    {
        var packed = new CStruct("struct root { bool flag; uint32 tail; };", aligned: false);
        var aligned = new CStruct("struct root { bool flag; uint32 tail; };", aligned: true);

        Assert.AreEqual(5, packed.GetStructSizeInBytes("root"));
        Assert.AreEqual(8, aligned.GetStructSizeInBytes("root"));
    }
}
