namespace CStructSharpTests;

using System.Dynamic;
using CStructSharp;

/// <summary>
///     Verifies that aligned composite fields use one consistent parent boundary across sizing, reading, writing,
///     address resolution, and debug metadata.
/// </summary>
[TestClass]
public class CompositeAlignmentTests
{
    /// <summary>
    ///     Places a narrow-first child after a one-byte prefix and verifies that every public operation uses the
    ///     child's eight-byte parent boundary, not merely the first member's one-byte alignment.
    /// </summary>
    [TestMethod]
    public void AlignedNestedStruct_UsesParentFieldBoundaryEverywhere()
    {
        const string layout = """
                              struct inner { byte a; uint64 b; };
                              struct outer { byte prefix; inner item; byte tail; };
                              """;
        byte[] bytes = new byte[32];
        bytes[0] = 0x11;
        bytes[8] = 0x22;
        bytes[16] = 0x33;
        bytes[24] = 0x44;
        var cstruct = new CStruct(layout, aligned: true);

        using var stream = new MemoryStream(bytes);
        dynamic parsed = cstruct.ParseStream(stream, "outer");

        Assert.AreEqual((byte)0x11, (byte)parsed.prefix);
        Assert.AreEqual((byte)0x22, (byte)parsed.item.a);
        Assert.AreEqual(0x33UL, (ulong)parsed.item.b);
        Assert.AreEqual((byte)0x44, (byte)parsed.tail);
        Assert.AreEqual(32, cstruct.GetStructSizeInBytes("outer"));

        stream.Position = 0;
        Assert.AreEqual(8L, cstruct.ResolveAddress(stream, "outer.item"));
        stream.Position = 0;
        Assert.AreEqual(8L, cstruct.ResolveAddress(stream, "outer.item.a"));
        stream.Position = 0;
        Assert.AreEqual(16L, cstruct.ResolveAddress(stream, "outer.item.b"));
        stream.Position = 0;
        Assert.AreEqual(24L, cstruct.ResolveAddress(stream, "outer.tail"));

        byte[] serialized = cstruct.Serialize("outer", parsed);
        CollectionAssert.AreEqual(bytes, serialized);

        stream.Position = 0;
        (List<DebugData> debug, _) = cstruct.ParseStreamWithDebug(stream, "outer");
        DebugData nestedFirstField = debug.Single(item => item.DebugStackString == "outer.item.a");
        Assert.AreEqual(8L, nestedFirstField.CurPos);
        Assert.AreEqual(9L, nestedFirstField.EndPos);
    }

    /// <summary>
    ///     Extends the parent-boundary invariant across an inline child, a child array, and a named union. These are
    ///     separate recursive branches internally, so each must place its first byte at the compiled field boundary and
    ///     leave the following sentinel at the same address used by size, read, write, and debug operations.
    /// </summary>
    [TestMethod]
    public void AlignedCompositeFields_ShareOneParentBoundaryRule()
    {
        const string inlineLayout = """
                                    struct root {
                                        byte prefix;
                                        struct { byte narrow; uint64 wide; } item;
                                        byte tail;
                                    };
                                    """;
        byte[] inlineBytes = new byte[32];
        inlineBytes[0] = 0x11;
        inlineBytes[8] = 0x22;
        inlineBytes[16] = 0x33;
        inlineBytes[24] = 0x44;
        var inline = new CStruct(inlineLayout, aligned: true);
        using var inlineStream = new MemoryStream(inlineBytes);
        dynamic inlineParsed = inline.ParseStream(inlineStream, "root");
        Assert.AreEqual((byte)0x22, (byte)inlineParsed.item.narrow);
        Assert.AreEqual(32, inline.GetStructSizeInBytes("root"));
        inlineStream.Position = 0;
        Assert.AreEqual(24L, inline.ResolveAddress(inlineStream, "root.tail"));
        CollectionAssert.AreEqual(inlineBytes, inline.Serialize("root", inlineParsed));

        const string arrayLayout = """
                                   struct item { byte narrow; uint64 wide; };
                                   struct root { byte prefix; item items[2]; byte tail; };
                                   """;
        byte[] arrayBytes = new byte[48];
        arrayBytes[0] = 0x11;
        arrayBytes[8] = 0x21;
        arrayBytes[16] = 0x22;
        arrayBytes[24] = 0x31;
        arrayBytes[32] = 0x32;
        arrayBytes[40] = 0x44;
        var array = new CStruct(arrayLayout, aligned: true);
        using var arrayStream = new MemoryStream(arrayBytes);
        dynamic arrayParsed = array.ParseStream(arrayStream, "root");
        Assert.AreEqual((byte)0x21, (byte)arrayParsed.items[0].narrow);
        Assert.AreEqual((byte)0x31, (byte)arrayParsed.items[1].narrow);
        Assert.AreEqual(48, array.GetStructSizeInBytes("root"));
        arrayStream.Position = 0;
        Assert.AreEqual(24L, array.ResolveAddress(arrayStream, "root.items[1]"));
        arrayStream.Position = 0;
        Assert.AreEqual(40L, array.ResolveAddress(arrayStream, "root.tail"));
        CollectionAssert.AreEqual(arrayBytes, array.Serialize("root", arrayParsed));

        const string unionLayout = """
                                   union choice { byte narrow; uint64 wide; };
                                   struct root { byte prefix; choice item; byte tail; };
                                   """;
        byte[] unionBytes = new byte[24];
        unionBytes[0] = 0x11;
        unionBytes[8] = 0x22;
        unionBytes[16] = 0x44;
        var union = new CStruct(unionLayout, aligned: true);
        using var unionStream = new MemoryStream(unionBytes);
        dynamic unionParsed = union.ParseStream(unionStream, "root");
        Assert.AreEqual((byte)0x22, (byte)unionParsed.item.narrow);
        Assert.AreEqual(24, union.GetStructSizeInBytes("root"));
        unionStream.Position = 0;
        Assert.AreEqual(8L, union.ResolveAddress(unionStream, "root.item"));
        unionStream.Position = 0;
        Assert.AreEqual(16L, union.ResolveAddress(unionStream, "root.tail"));
        CollectionAssert.AreEqual(unionBytes, union.Serialize("root", unionParsed));
    }

    /// <summary>
    ///     Uses a new bitfield storage unit when the declared primitive type changes, matching the compiled size rule
    ///     and keeping the following sentinel at the same offset for read, write, debug, and address operations.
    /// </summary>
    [TestMethod]
    public void MixedBaseTypeBitfields_UseSeparateStorageUnits()
    {
        const string layout = "struct root { uint16 a:4; int16 b:4; byte tail; };";
        byte[] bytes = [0x0A, 0x00, 0x0B, 0x00, 0xA5,];
        var cstruct = new CStruct(layout);
        using var stream = new MemoryStream(bytes);

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual(0xA, (int)parsed.a);
        Assert.AreEqual(0xB, (int)parsed.b);
        Assert.AreEqual((byte)0xA5, (byte)parsed.tail);
        Assert.AreEqual(5, cstruct.GetStructSizeInBytes("root"));
        stream.Position = 0;
        Assert.AreEqual(2L, cstruct.ResolveAddress(stream, "root.b"));
        stream.Position = 0;
        Assert.AreEqual(4L, cstruct.ResolveAddress(stream, "root.tail"));
        CollectionAssert.AreEqual(bytes, cstruct.Serialize("root", parsed));
    }
}
