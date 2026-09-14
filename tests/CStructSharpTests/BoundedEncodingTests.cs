namespace CStructSharpTests;

using CStructSharp;

/// <summary>Exercises strict byte-bounded encodings across the shared operation path.</summary>
[TestClass]
public class BoundedEncodingTests
{
    /// <summary>Encoded strings preserve byte extents, NULs, byte order and following fields.</summary>
    [TestMethod]
    public void Encodings_RoundTripAndPreserveBoundaries()
    {
        foreach ((string type, string text, byte[] payload) in new (string, string, byte[])[]
                 {
                     ("latin1", "é\0ÿ", [0xe9, 0, 0xff]),
                     ("cp437", "é\0─", [0x82, 0, 0xc4]),
                     ("utf16le", "é🌍", [0xe9, 0, 0x3c, 0xd8, 0x0d, 0xdf]),
                     ("utf16be", "é🌍", [0, 0xe9, 0xd8, 0x3c, 0xdf, 0x0d]),
                 })
        {
            var parser = new CStruct($"typedef {type} text; struct root {{ uint8 length; text name[length]; uint8 tail; }};", aligned: false);
            byte[] bytes = [(byte)payload.Length, .. payload, 99];
            using var stream = new MemoryStream(bytes);
            (List<DebugData> debug, dynamic parsed) = parser.ParseStreamWithDebug(stream, "root");
            Assert.AreEqual(text, (string)parsed.root.name);
            Assert.AreEqual((byte)99, (byte)parsed.root.tail);
            CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed.root));
            DebugData entry = debug.Single(item => item.DebugStackString == "root.name");
            Assert.AreEqual(1L, entry.CurPos);
            Assert.AreEqual(1L + payload.Length, entry.EndPos);
            stream.Position = 0;
            Assert.AreEqual(text, parser.ReadValue<string>(stream, "root.name"));
            stream.Position = 0;
            Assert.AreEqual(1L + payload.Length, parser.ResolveAddress(stream, "root.tail"));
            stream.Position = 0;
            Assert.AreEqual(payload.Length, parser.GetDynamicArrayLength(stream, "root.name"));
            stream.Position = 0;
            parser.UpdateStream(stream, "root.name", string.Empty);
            CollectionAssert.AreEqual(new byte[payload.Length], stream.ToArray()[1..^1]);
        }
    }

    /// <summary>Unmappable text and malformed UTF-16 cannot silently replace or consume adjacent bytes.</summary>
    [TestMethod]
    public void InvalidTextAndCapacities_FailWithoutMutation()
    {
        foreach (string type in new[] { "latin1", "cp437", "utf16le", "utf16be" })
        {
            var parser = new CStruct($"struct root {{ {type} name[2]; uint8 tail; }};", aligned: false);
            using var stream = new MemoryStream(new byte[] { 0, 0, 99 });
            byte[] before = stream.ToArray();
            Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.name", "\ud800"));
            CollectionAssert.AreEqual(before, stream.ToArray());
            Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.name", "abcdef"));
            CollectionAssert.AreEqual(before, stream.ToArray());
            Assert.Throws<CStructLayoutException>(() => new CStruct($"struct root {{ {type} name[]; }};"));
            Assert.Throws<CStructLayoutException>(() => new CStruct($"struct root {{ {type} name[2][2]; }};"));
        }

        foreach (string type in new[] { "utf16le", "utf16be" })
        {
            var odd = new CStruct($"struct root {{ {type} name[1]; uint8 tail; }};", aligned: false);
            using var stream = new MemoryStream(new byte[] { 0, 99 });
            Assert.Throws<CStructReadException>(() => odd.ParseStream(stream, "root"));
            Assert.AreEqual(1L, stream.Position);
            Assert.Throws<CStructWriteException>(() => odd.Serialize("root", new { name = string.Empty, tail = 99 }));
        }
    }

    /// <summary>CP437 maps all byte values bijectively without depending on host encoding providers.</summary>
    [TestMethod]
    public void Cp437_AllBytesRoundTrip()
    {
        var parser = new CStruct("struct root { cp437 text[256]; };", aligned: false);
        byte[] bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();
        dynamic parsed = parser.ParseStream(new MemoryStream(bytes), "root");
        Assert.AreEqual('é', ((string)parsed.text)[0x82]);
        Assert.AreEqual('─', ((string)parsed.text)[0xc4]);
        CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed));
    }
}
