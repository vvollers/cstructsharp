namespace CStructSharpTests;

using System.Text;
using CStructSharp;

/// <summary>Checks byte-bounded UTF-8 across parsing, sizing, selection, serialization and updates.</summary>
[TestClass]
public class BoundedUtf8Tests
{
    /// <summary>Byte counts, embedded NULs and supplementary characters survive a complete round trip.</summary>
    [TestMethod]
    public void RuntimeBuffer_RoundTripsAndPreservesOffsets()
    {
        const string text = "é\0🌍";
        var parser = new CStruct("struct root { uint8 length; utf8 name[length]; uint16 tail; };", aligned: false);
        byte[] bytes = [7, .. Encoding.UTF8.GetBytes(text), 0x34, 0x12];
        using var stream = new MemoryStream(bytes);
        (List<DebugData> debug, dynamic parsed) = parser.ParseStreamWithDebug(stream, "root");
        Assert.AreEqual(text, (string)parsed.root.name);
        Assert.AreEqual((ushort)0x1234, (ushort)parsed.root.tail);
        DebugData entry = debug.Single(item => item.DebugStackString == "root.name");
        Assert.AreEqual(1L, entry.CurPos);
        Assert.AreEqual(8L, entry.EndPos);
        CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed.root));
        stream.Position = 0;
        Assert.AreEqual(text, parser.ReadValue<string>(stream, "root.name"));
        stream.Position = 0;
        Assert.AreEqual(7, parser.GetDynamicArrayLength(stream, "root.name"));
        stream.Position = 0;
        Assert.AreEqual(8L, parser.ResolveAddress(stream, "root.tail"));
        stream.Position = 0;
        Assert.AreEqual((byte)0xa9, parser.ReadValue<byte>(stream, "root.name[1]"));
    }

    /// <summary>Fixed buffers size by bytes, zero-pad short writes and stage invalid updates atomically.</summary>
    [TestMethod]
    public void FixedBuffer_WritesWithinItsByteCapacity()
    {
        var parser = new CStruct("typedef utf8 text; struct root { text name[4]; uint8 tail; };", aligned: false);
        var value = new Dictionary<string, object> { ["name"] = "é", ["tail"] = 99 };
        byte[] bytes = parser.Serialize("root", value);
        CollectionAssert.AreEqual(new byte[] { 0xc3, 0xa9, 0, 0, 99 }, bytes);
        Assert.AreEqual(5, parser.GetStructSizeInBytes("root"));
        using var stream = new MemoryStream(bytes);
        parser.UpdateStream(stream, "root.name", "🌍");
        Assert.AreEqual("🌍", parser.ReadValue<string>(stream, "root.name"));
        byte[] before = stream.ToArray();
        Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.name", "ééé"));
        CollectionAssert.AreEqual(before, stream.ToArray());
        Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.name", "\ud800"));
        CollectionAssert.AreEqual(before, stream.ToArray());
    }

    /// <summary>A split sequence cannot borrow bytes from the next field and malformed bytes fail strictly.</summary>
    [TestMethod]
    public void InvalidOrTruncatedInput_DoesNotReadBeyondTheBuffer()
    {
        var parser = new CStruct("struct root { utf8 name[1]; uint8 tail; };", aligned: false);
        using var split = new MemoryStream(new byte[] { 0xc3, 0xa9 });
        Assert.Throws<CStructReadException>(() => parser.ParseStream(split, "root"));
        Assert.AreEqual(1L, split.Position);
        using var invalid = new MemoryStream(new byte[] { 0xff, 42 });
        Assert.Throws<CStructReadException>(() => parser.ParseStream(invalid, "root"));
        using var empty = new MemoryStream();
        Assert.Throws<CStructReadException>(() => parser.ParseStream(empty, "root"));
    }

    /// <summary>Empty buffers consume no bytes and string limits apply to encoded capacity.</summary>
    [TestMethod]
    public void EmptyBuffersAndBudgets_UseEncodedBytes()
    {
        var empty = new CStruct("struct root { utf8 name[0]; uint8 tail; };", aligned: false);
        dynamic value = empty.ParseStream(new MemoryStream(new byte[] { 42 }), "root");
        Assert.AreEqual(string.Empty, (string)value.name);
        Assert.AreEqual((byte)42, (byte)value.tail);
        var parser = new CStruct("struct root { utf8 name[2]; };", aligned: false);
        Assert.Throws<CStructReadLimitException>(() => parser.ParseStream(
            new MemoryStream(new byte[] { 0xc3, 0xa9 }), "root", options: new ReadOptions { MaxStringBytes = 1 }));
        Assert.Throws<CStructWriteLimitException>(() => parser.Serialize(
            "root", new Dictionary<string, object> { ["name"] = "é" }, options: new WriteOptions { MaxStringBytes = 1 }));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { utf8 text[]; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { utf8 text[2][4]; };"));
    }
}
