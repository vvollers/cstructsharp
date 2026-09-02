namespace CStructSharp.Tests;

using System.Dynamic;
using System.Text;
using CStructSharp.Structure;

/// <summary>Groups tests for parsing so changes to this behavior are caught.</summary>
[TestClass]
public class Parsing
{
    /// <summary>
    ///     In C, a fixed array field like byte a[2] is stored inline inside the struct as contiguous elements. This test
    ///     verifies element indexing and byte-for-byte ordering during decode.
    /// </summary>
    [TestMethod]
    public void ArrayParsingTest()
    {
        const string structDef = "struct mystruct { byte a[2]; };";

        byte[] testData = [10, 20,];
        var mem = new MemoryStream(testData);

        var strct = new CStruct(structDef);
        dynamic result = strct.ParseStream(mem);

        Assert.AreEqual(10, result.a[0]);
        Assert.AreEqual(20, result.a[1]);
    }

    /// <summary>
    ///     This test models a common C layout pattern: an array of nested structs whose count comes from a preprocessor
    ///     expression. It validates that #define arithmetic is resolved before layout so the decoder reads the correct number
    ///     of records.
    /// </summary>
    [TestMethod]
    public void ComplexParsingTest()
    {
        const string structDef = """
                                 struct substruct { byte a; byte b; };
                                 #define MYCONST 0xF - 0xA
                                 struct mystruct { substruct sub[MYCONST]; };
                                 """;

        byte[] testData = [10, 20, 11, 21, 12, 22, 13, 23, 14, 24,];
        var mem = new MemoryStream(testData);

        var strct = new CStruct(structDef);
        dynamic result = strct.ParseStream(mem, "mystruct");

        Assert.AreEqual(10, result.sub[0].a);
        Assert.AreEqual(20, result.sub[0].b);

        Assert.AreEqual(12, result.sub[2].a);
        Assert.AreEqual(22, result.sub[2].b);

        Assert.AreEqual(14, result.sub[4].a);
        Assert.AreEqual(24, result.sub[4].b);
    }

    /// <summary>
    ///     Uses the same nested struct-array definition as ComplexParsingTest, but additionally validates debug-map
    ///     generation. The debug map is important for C-style binary parsing because it ties each field value to exact byte
    ///     offsets.
    /// </summary>
    [TestMethod]
    public void ComplexParsingTestWithDebug()
    {
        const string structDef = """
                                 struct substruct { byte a; byte b; };
                                 #define MYCONST 0xF - 0xA
                                 struct mystruct { substruct sub[MYCONST]; };
                                 """;

        byte[] testData = [10, 20, 11, 21, 12, 22, 13, 23, 14, 24,];
        var mem = new MemoryStream(testData);

        var strct = new CStruct(structDef);
        (List<DebugData>? debugData, dynamic resultObj)
            = strct.ParseStreamWithDebug(mem, "mystruct", new ReadOptions());
        dynamic result = resultObj;

        Assert.AreEqual(10, result.mystruct.sub[0].a);
        Assert.AreEqual(20, result.mystruct.sub[0].b);

        Assert.AreEqual(12, result.mystruct.sub[2].a);
        Assert.AreEqual(22, result.mystruct.sub[2].b);

        Assert.AreEqual(14, result.mystruct.sub[4].a);
        Assert.AreEqual(24, result.mystruct.sub[4].b);

        Assert.IsNotEmpty(debugData);
    }

    /// <summary>
    ///     C structs often repeat same-width integer fields back-to-back, relying on declaration order for layout. This test
    ///     confirms two long members are decoded sequentially with no unexpected reordering.
    /// </summary>
    [TestMethod]
    public void LongParsingTest()
    {
        const string structDef = "struct mystruct { long a; long b; };";

        long[] testData = [10, 20,];
        byte[] byteArray = new byte[testData.Length * sizeof(long)];

        // Convert long array to byte array
        Buffer.BlockCopy(testData, 0, byteArray, 0, byteArray.Length);

        var mem = new MemoryStream(byteArray);

        var strct = new CStruct(structDef);
        dynamic result = strct.ParseStream(mem);

        Assert.AreEqual(10, result.a);
        Assert.AreEqual(20, result.b);
    }

    /// <summary>
    ///     C bitfields pack sub-byte values into integer storage units, then continue with normal fields when boundaries are
    ///     crossed. This test verifies bit extraction order, grouping behavior, and transition back to byte-aligned fields.
    /// </summary>
    [TestMethod]
    public void ParsingBitFieldTest()
    {
        const string structDef = """
                                 struct mystruct { 
                                     byte a:1; 
                                     byte b:1; 
                                     byte c:1; 
                                     
                                     byte d;
                                     
                                     byte e:2;
                                     byte f:2;
                                     byte g:2;
                                     byte h:2;
                                     
                                     byte i:2;
                                     byte j:2;
                                     
                                     byte k;
                                 };
                                 """;

        byte[] testData = [0b00000_1_0_1, 0xF, 0b11_10_00_01, 0b00_00_01_10, 0xA,];
        var mem = new MemoryStream(testData);

        var strct = new CStruct(structDef);
        dynamic result = strct.ParseStream(mem, "mystruct");

        Assert.AreEqual(1, result.a);
        Assert.AreEqual(0, result.b);
        Assert.AreEqual(1, result.c);

        Assert.AreEqual(0xF, result.d);

        Assert.AreEqual(0b01, result.e);
        Assert.AreEqual(0b00, result.f);
        Assert.AreEqual(0b10, result.g);
        Assert.AreEqual(0b11, result.h);

        Assert.AreEqual(0b10, result.i);
        Assert.AreEqual(0b01, result.j);

        Assert.AreEqual(0xA, result.k);
    }

    /// <summary>
    ///     Builds on ParsingBitFieldTest by adding a leading ushort and enabling aligned mode. It checks that pre-bitfield
    ///     alignment and struct-level layout still produce correct bitfield values.
    /// </summary>
    [TestMethod]
    public void ParsingBitFieldWithStructTest()
    {
        const string structDef = """
                                 struct mystruct {
                                     ushort u;
                                     
                                     byte a:1;
                                     byte b:1;
                                     byte c:1;
                                     
                                     byte d;
                                     
                                     byte e:2;
                                     byte f:2;
                                     byte g:2;
                                     byte h:2;
                                     
                                     byte i:2;
                                     byte j:2;
                                     
                                     byte k;
                                 };
                                 """;

        byte[] testData = [10, 0, 0b00000_1_0_1, 0xF, 0b11_10_00_01, 0b00_00_01_10, 0xA,];
        var mem = new MemoryStream(testData);

        var strct = new CStruct(structDef, aligned: true);
        dynamic result = strct.ParseStream(mem, "mystruct");

        Assert.AreEqual(10, result.u);

        Assert.AreEqual(1, result.a);
        Assert.AreEqual(0, result.b);
        Assert.AreEqual(1, result.c);

        Assert.AreEqual(0xF, result.d);

        Assert.AreEqual(0b01, result.e);
        Assert.AreEqual(0b00, result.f);
        Assert.AreEqual(0b10, result.g);
        Assert.AreEqual(0b11, result.h);

        Assert.AreEqual(0b10, result.i);
        Assert.AreEqual(0b01, result.j);

        Assert.AreEqual(0xA, result.k);
    }

    /// <summary>
    ///     C enums are integer-backed constants that map numeric payload values to symbolic names. This test verifies explicit
    ///     enum values and implicit auto-incremented values resolve correctly during parsing.
    /// </summary>
    [TestMethod]
    public void ParsingEnumTest()
    {
        const string structDef = """
                                 enum myenum { Red = 5, Green, Blue = 9 };
                                 struct mystruct { myenum a; myenum b; myenum c; };
                                 """;

        byte[] testData = [5, 9, 6,];
        var mem = new MemoryStream(testData);

        var strct = new CStruct(structDef);
        dynamic result = strct.ParseStream(mem, "mystruct");

        Assert.AreEqual("Red", result.a.Name);
        Assert.AreEqual("Blue", result.b.Name);
        Assert.AreEqual("Green", result.c.Name);
    }

    /// <summary>
    ///     Demonstrates the baseline C struct rule that scalar members are read in declaration order from contiguous bytes. It
    ///     is the control case for more complex layout tests.
    /// </summary>
    [TestMethod]
    public void SimpleParsingTest()
    {
        const string structDef = "struct mystruct { byte a; byte b; };";

        byte[] testData = [10, 20,];
        var mem = new MemoryStream(testData);

        var strct = new CStruct(structDef);
        dynamic result = strct.ParseStream(mem);

        Assert.AreEqual(10, result.a);
        Assert.AreEqual(20, result.b);
    }

    /// <summary>
    ///     A wchar array in C represents a fixed-length wide-character buffer embedded directly in the struct. This test
    ///     verifies that buffer is decoded as a full string value with expected character width.
    /// </summary>
    [TestMethod]
    public void WCharParseArrayTest()
    {
        const string structDef = "struct mystruct { wchar a[4]; };";

        const string testDataStr = "test";
        byte[] testData = Encoding.Unicode.GetBytes(testDataStr);

        var mem = new MemoryStream(testData);

        var strct = new CStruct(structDef);
        dynamic result = strct.ParseStream(mem);

        Assert.AreEqual("test", result.a);
    }

    /// <summary>
    ///     Contrasts WCharParseArrayTest by declaring four independent wchar fields instead of one wchar array. It validates
    ///     per-field decoding and preserves each character as a distinct struct member.
    /// </summary>
    [TestMethod]
    public void WCharParsingTest()
    {
        const string structDef = "struct mystruct { wchar a; wchar b; wchar c; wchar d;};";

        const string testDataStr = "test";
        byte[] testData = Encoding.Unicode.GetBytes(testDataStr);

        var mem = new MemoryStream(testData);

        var strct = new CStruct(structDef);
        dynamic result = strct.ParseStream(mem);

        Assert.AreEqual('t', result.a);
        Assert.AreEqual('e', result.b);
        Assert.AreEqual('s', result.c);
        Assert.AreEqual('t', result.d);
    }
}
