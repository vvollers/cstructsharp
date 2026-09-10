namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Structure;
using Pidgin;

/// <summary>
///     Verifies typedef syntax and resolution across primitive, structure, pointer, array, chained, and invalid aliases.
/// </summary>
[TestClass]
public class TypedefResolutionTests
{
    /// <summary>
    ///     typedef uint16 word gives the two-byte integer another name.
    /// </summary>
    /// <remarks>
    ///     The parser must record that direction correctly, and a word field must read 34 12 as 0x1234. Reported size
    ///     and serialization must remain identical to using uint16 directly.
    /// </remarks>
    [TestMethod]
    public void PrimitiveTypedef_UsesAliasNameAndUnderlyingType()
    {
        var alias = (Typedef)CStructDefinitionParser.Typedef.ParseOrThrow("typedef uint16 word;");

        Assert.AreEqual("word", alias.Name.Name);
        Assert.AreEqual("uint16", alias.Type.Name);

        var cstruct = new CStruct("typedef uint16 word; struct root { word value; };");
        using var stream = new MemoryStream([0x34, 0x12,]);
        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual((ushort)0x1234, (ushort)parsed.value);
        Assert.AreEqual(2, cstruct.GetStructSizeInBytes("root"));
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, cstruct.Serialize("root", parsed));
    }

    /// <summary>
    ///     Aliases are used for a nested record, array elements, and a pointer.
    /// </summary>
    /// <remarks>
    ///     They must retain the expected values, pointer address, and seven-byte packed root size. Forward alias chains
    ///     must resolve, while missing types and cycles must fail during construction instead of reaching stream
    ///     operations.
    /// </remarks>
    [TestMethod]
    public void Typedefs_WorkForStructPointersAndArraysAndRejectInvalidChains()
    {
        const string layout = """
                              typedef struct payload { byte value; } payload_t;
                              typedef uint16 word;
                              typedef uint16* word_pointer;
                              struct root {
                                  payload_t item;
                                  word words[2];
                                  word_pointer pointer;
                              };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 2);
        using var stream = new MemoryStream([0x2A, 0x34, 0x12, 0x78, 0x56, 0x08, 0x00, 0xA5, 0x00,]);

        dynamic parsed = cstruct.ParseStream(
            stream,
            "root",
            (IReadOnlyDictionary<string, int>?)null,
            options: new ReadOptions { DereferencePointers = false, });

        Assert.AreEqual((byte)0x2A, (byte)parsed.item.value);
        Assert.AreEqual((ushort)0x1234, (ushort)parsed.words[0]);
        Assert.AreEqual((ushort)0x5678, (ushort)parsed.words[1]);
        Assert.AreEqual(8L, ((Pointer)parsed.pointer).Address);
        Assert.AreEqual(7, cstruct.GetStructSizeInBytes("root"));

        Assert.Throws<CStructLayoutException>(
            () => new CStruct("typedef missing alias; struct root { alias value; };"));
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("typedef second first; typedef first second; struct root { first value; };"));

        var chain = new CStruct(
            "struct root { later first; final_t values[2]; }; " +
            "typedef uint16 later; typedef later middle; typedef middle final_t;");
        using var chainStream = new MemoryStream([0x34, 0x12, 0x78, 0x56, 0xBC, 0x9A,]);
        dynamic chainParsed = chain.ParseStream(chainStream, "root");
        Assert.AreEqual((ushort)0x1234, (ushort)chainParsed.first);
        Assert.AreEqual((ushort)0x5678, (ushort)chainParsed.values[0]);
        Assert.AreEqual((ushort)0x9ABC, (ushort)chainParsed.values[1]);
        CollectionAssert.AreEqual(
            new byte[] { 0x34, 0x12, 0x78, 0x56, 0xBC, 0x9A, },
            chain.Serialize("root", chainParsed));
    }
}
