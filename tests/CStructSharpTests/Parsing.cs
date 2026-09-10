namespace CStructSharp.Tests;

using System.Dynamic;
using System.Text;
using CStructSharp.Structure;

/// <summary>Groups tests for parsing so changes to this behavior are caught.</summary>
[TestClass]
public class Parsing
{
    /// <summary>
    ///     The brackets reserve two adjacent bytes inside mystruct; they do not store a pointer.
    /// </summary>
    /// <remarks>
    ///     Input bytes 10 and 20 must become a[0] and a[1]. Array indexes start at zero, and no byte-order conversion
    ///     is needed for a one-byte element.
    /// </remarks>
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
    ///     MYCONST evaluates to 15 - 10 = 5.
    /// </summary>
    /// <remarks>
    ///     Each substruct has two bytes, so the ten input bytes form five records. The checks select the first, middle,
    ///     and last records, expecting pairs (10,20), (12,22), and (14,24). Selecting mystruct as the root avoids
    ///     parsing only the earlier substruct declaration.
    /// </remarks>
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
    ///     Five two-byte substruct records are decoded from the ten input bytes.
    /// </summary>
    /// <remarks>
    ///     The checked pairs remain (10,20), (12,22), and (14,24), and the debug list must not be empty. This overload
    ///     wraps the full result under mystruct and supplies byte-location metadata for inspection; it does not change
    ///     the field values.
    /// </remarks>
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
    ///     The fixture copies C# long values 10 and 20 into a byte buffer, then reads the two layout fields in order.
    /// </summary>
    /// <remarks>
    ///     In this library long is eight bytes; C compilers do not all use that size for C long. The test expects a =
    ///     10 and b = 20 from the resulting 16 bytes.
    /// </remarks>
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
    ///     The first byte 0b00000101 supplies a = 1, b = 0, and c = 1 from its low bits.
    /// </summary>
    /// <remarks>
    ///     The ordinary field d starts at the next byte and reads 15. Two-bit fields then share bytes in groups, and k
    ///     must still read 10 after the final partial group.
    /// </remarks>
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
    ///     The leading ushort consumes 0A 00 and reads 10 before the one-bit fields begin.
    /// </summary>
    /// <remarks>
    ///     With alignment enabled, those fields must still read 1, 0, and 1, and the later two-bit groups must keep
    ///     their original order. The final byte field k must read 10 rather than leftover bits from the preceding
    ///     group.
    /// </remarks>
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
    ///     Red is explicitly 5 and Blue is 9; Green inherits the next number after Red, which is 6.
    /// </summary>
    /// <remarks>
    ///     Input bytes 5, 9, and 6 must therefore return names Red, Blue, and Green. An enum gives meaning to stored
    ///     integer values without storing the names themselves.
    /// </remarks>
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
    ///     mystruct has two one-byte fields and the input contains exactly two bytes.
    /// </summary>
    /// <remarks>
    ///     The first becomes a = 10 and the second b = 20. This is the simplest complete example of using layout text
    ///     to give names to positions in a binary record.
    /// </remarks>
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
    ///     The library's wchar uses a two-byte UTF-16 code unit.
    /// </summary>
    /// <remarks>
    ///     Four units containing test occupy eight bytes and must return one string. The brackets make this a fixed
    ///     buffer, so no extra zero terminator is needed; this width is a library choice rather than a universal C
    ///     wchar_t rule.
    /// </remarks>
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
    ///     The same eight UTF-16 bytes are declared as four separate fields instead of one array.
    /// </summary>
    /// <remarks>
    ///     The result must expose characters t, e, s, and t as a, b, c, and d. A scalar character field and a character
    ///     array therefore have different result shapes even when their bytes match.
    /// </remarks>
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
