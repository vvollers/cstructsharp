namespace CStructSharp.Tests;

using CStructSharp.Structure;
using Pidgin;

/// <summary>Groups tests for structs so changes to this behavior are caught.</summary>
[TestClass]
public class Structs
{
    /// <summary>
    ///     Parses a minimal C struct with two int members to validate fundamental grammar: type-name pairs, field order, and
    ///     field count. This is the base structural model every richer declaration relies on.
    /// </summary>
    [TestMethod]
    public void TestSimpleStruct()
    {
        var mystruct = (Struct)CStructDefinitionParser.Struct.ParseOrThrow("struct mystruct { int a; int b; };");
        Assert.AreEqual("mystruct", mystruct.Name.Name);
        Assert.HasCount(2, mystruct.Fields);
        Assert.AreEqual("a", mystruct.Fields[0].Name.Name);
        Assert.AreEqual("int", mystruct.Fields[0].Type.Name);
        Assert.AreEqual("b", mystruct.Fields[1].Name.Name);
        Assert.AreEqual("int", mystruct.Fields[1].Type.Name);
    }

    /// <summary>
    ///     Exercises real-world C struct grammar with fixed arrays, computed array sizes, nested type references, and
    ///     bitfield-array syntax. It validates that the parser preserves both declaration intent and derived counts.
    /// </summary>
    [TestMethod]
    public void TestStructWithArrays()
    {
        const string mystructTxt = """
                                   struct mbr_s {
                                       uint8       jmp[3];
                                       sig     signature;
                                       uint8   crlf[3];            /* 10 */
                                       uint8   default_char;   /* 13 */
                                       uint8   chars[4];           /* 14 */
                                       uint16  delay;              /* 18 */
                                       uint16  offsets[4]: 6;         /* 1a..20 */
                                       char    rest_of_code[0x1b6-0x22];
                                       uint16  pad1;
                                       uint32  vol_no;
                                       uint16  pad2;
                                       part    part[4];
                                       uint16  bootsig;
                                   };
                                   """;
        var mystruct = (Struct)CStructDefinitionParser.Struct.ParseOrThrow(mystructTxt);
        Assert.AreEqual("mbr_s", mystruct.Name.Name);
        Assert.HasCount(13, mystruct.Fields);
        Assert.AreEqual("jmp", mystruct.Fields[0].Name.Name);
        Assert.AreEqual("uint8", mystruct.Fields[0].Type.Name);
        Assert.AreEqual(3, mystruct.Fields[0].ArrayCount.Calc());
        Assert.AreEqual("signature", mystruct.Fields[1].Name.Name);
        Assert.AreEqual("sig", mystruct.Fields[1].Type.Name);
        Assert.AreEqual("crlf", mystruct.Fields[2].Name.Name);
        Assert.AreEqual("uint8", mystruct.Fields[2].Type.Name);
        Assert.AreEqual(3, mystruct.Fields[2].ArrayCount.Calc());
        Assert.AreEqual("default_char", mystruct.Fields[3].Name.Name);
        Assert.AreEqual("uint8", mystruct.Fields[3].Type.Name);
        Assert.AreEqual("chars", mystruct.Fields[4].Name.Name);
        Assert.AreEqual("uint8", mystruct.Fields[4].Type.Name);
        Assert.AreEqual(4, mystruct.Fields[4].ArrayCount.Calc());
        Assert.AreEqual("delay", mystruct.Fields[5].Name.Name);
        Assert.AreEqual("uint16", mystruct.Fields[5].Type.Name);
        Assert.AreEqual("offsets", mystruct.Fields[6].Name.Name);
        Assert.AreEqual(6, mystruct.Fields[6].BitSize);
        Assert.AreEqual("uint16", mystruct.Fields[6].Type.Name);
        Assert.AreEqual(4, mystruct.Fields[6].ArrayCount.Calc());
        Assert.AreEqual("rest_of_code", mystruct.Fields[7].Name.Name);
        Assert.AreEqual("char", mystruct.Fields[7].Type.Name);
        Assert.AreEqual(0x1b6 - 0x22, mystruct.Fields[7].ArrayCount.Calc());
        Assert.AreEqual("pad1", mystruct.Fields[8].Name.Name);
        Assert.AreEqual("uint16", mystruct.Fields[8].Type.Name);
        Assert.AreEqual("vol_no", mystruct.Fields[9].Name.Name);
        Assert.AreEqual("uint32", mystruct.Fields[9].Type.Name);
        Assert.AreEqual("pad2", mystruct.Fields[10].Name.Name);
        Assert.AreEqual("uint16", mystruct.Fields[10].Type.Name);
        Assert.AreEqual("part", mystruct.Fields[11].Name.Name);
        Assert.AreEqual("part", mystruct.Fields[11].Type.Name);
        Assert.AreEqual(4, mystruct.Fields[11].ArrayCount.Calc());
        Assert.AreEqual("bootsig", mystruct.Fields[12].Name.Name);
        Assert.AreEqual("uint16", mystruct.Fields[12].Type.Name);
    }

    /// <summary>
    ///     Verifies C field declarations that include explicit bit widths using colon syntax on integral types. It also
    ///     confirms that non-bitfield members in the same struct remain regular scalar fields.
    /// </summary>
    [TestMethod]
    public void TestStructWithBitfields()
    {
        var mystruct
            = (Struct)CStructDefinitionParser.Struct.ParseOrThrow("struct mystruct { int a : 4; int b : 2; byte c; };");
        Assert.AreEqual("mystruct", mystruct.Name.Name);
        Assert.HasCount(3, mystruct.Fields);
        Assert.AreEqual("a", mystruct.Fields[0].Name.Name);
        Assert.AreEqual("int", mystruct.Fields[0].Type.Name);
        Assert.AreEqual(4, mystruct.Fields[0].BitSize);
        Assert.AreEqual("b", mystruct.Fields[1].Name.Name);
        Assert.AreEqual("int", mystruct.Fields[1].Type.Name);
        Assert.AreEqual(2, mystruct.Fields[1].BitSize);
        Assert.AreEqual("c", mystruct.Fields[2].Name.Name);
        Assert.AreEqual("byte", mystruct.Fields[2].Type.Name);
        Assert.AreEqual(0, mystruct.Fields[2].BitSize);
    }

    /// <summary>
    ///     Unsized array members like char a[] represent flexible or unknown-length storage in C declarations. This test
    ///     confirms the parser marks the field as unknown-sized rather than inventing a fixed length.
    /// </summary>
    [TestMethod]
    public void TestUknownArraySize()
    {
        var mystruct = (Struct)CStructDefinitionParser.Struct.ParseOrThrow("struct mystruct { char a[]; };");
        Assert.AreEqual("mystruct", mystruct.Name.Name);
        Assert.HasCount(1, mystruct.Fields);
        Assert.AreEqual("a", mystruct.Fields[0].Name.Name);
        Assert.AreEqual("char", mystruct.Fields[0].Type.Name);
        Assert.AreEqual(Field.UnknownArraysize, mystruct.Fields[0].ArrayCount);
    }
}
