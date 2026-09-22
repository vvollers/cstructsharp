namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks consistent unnamed-array limits and padding-free results across optimized and general reads.</summary>
[TestClass]
public class ReaderPaddingLimitTests
{
    /// <summary>Debug traversal consumes padding without exposing an empty-name result member.</summary>
    /// <param name="declaration">The scalar, array or bitfield padding declaration.</param>
    /// <param name="paddingBytes">The storage extent before the named byte.</param>
    [TestMethod]
    [DataRow("uint8 _;", 1)]
    [DataRow("uint8 _[2];", 2)]
    [DataRow("uint8 : 3;", 1)]
    public void DebugPadding_IsNotAResultMember(string declaration, int paddingBytes)
    {
        var layout = new CStruct("struct root { " + declaration + " uint8 value; };");
        byte[] bytes = new byte[paddingBytes + 1];
        bytes[^1] = 7;
        ParseResult parsed = layout.ParseWithDebug(bytes.AsSpan(), "root");
        Assert.AreEqual((byte)7, parsed.Value.Get<byte>("value"));
        Assert.IsFalse(parsed.Value.ContainsKey(string.Empty));
        Assert.AreEqual(1, parsed.Value.Count);
        Assert.IsTrue(parsed.Debug.Count > 1, "Padding remains visible in the byte-range diagnostics.");
    }

    /// <summary>A union retains padding bytes without adding an unnamed decoded view.</summary>
    /// <param name="debug">Whether the enclosing struct also requests debug ranges.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnionPadding_IsNotAMemberView(bool debug)
    {
        var layout = new CStruct("union child { uint8 _[2]; uint8 value; }; struct root { child item; };");
        byte[] bytes = [7, 0xcc,];
        StructValue root = debug ? layout.ParseWithDebug(bytes.AsSpan(), "root").Value : layout.Parse(bytes.AsSpan(), "root");
        UnionValue value = root.Get<UnionValue>("item");
        Assert.AreEqual((byte)7, value.Get<byte>("value"));
        Assert.IsFalse(value.ContainsKey(string.Empty));
        Assert.AreEqual(1, value.Count);
        CollectionAssert.AreEqual(bytes, value.RawStorage!.Value.ToArray());
    }

    /// <summary>Unnamed array storage respects its element limit in direct, nested and promoted layouts.</summary>
    /// <param name="runtime">Whether a preceding runtime array prevents a whole-root static read plan.</param>
    /// <param name="placement">Where the unnamed array appears relative to the root.</param>
    [TestMethod]
    [DataRow(false, "direct")]
    [DataRow(true, "direct")]
    [DataRow(false, "nested")]
    [DataRow(true, "nested")]
    [DataRow(false, "promoted")]
    [DataRow(true, "promoted")]
    public void PaddingArray_RespectsElementLimit(bool runtime, string placement)
    {
        string preceding = runtime ? "uint8 count; uint8 data[count]; " : string.Empty;
        const string members = "uint8 _[3]; uint8 tail;";
        string definition = placement switch
        {
            "nested" => "struct child { " + members + " }; struct root { " + preceding + "child item; };",
            "promoted" => "struct root { " + preceding + "struct { " + members + " }; };",
            _ => "struct root { " + preceding + members + " };",
        };
        var layout = new CStruct(definition);
        byte[] bytes = new byte[runtime ? 5 : 4];
        bytes[^1] = 7;
        var limit = new ReadOptions { MaxArrayElements = 2 };

        // Padding is absent from the result, but its declaration still contains three elements.
        Assert.Throws<CStructReadLimitException>(() => layout.Parse(bytes.AsSpan(), "root", options: limit));
        using var source = new MemoryStream(bytes);

        // Stream-backed reads must enforce the same limit as the borrowed-span path.
        Assert.Throws<CStructReadLimitException>(() => layout.Parse(source, "root", options: limit));
        StructValue root = layout.Parse(bytes.AsSpan(), "root", options: new ReadOptions { MaxArrayElements = 3 });
        StructValue value = placement == "nested" ? root.Get<StructValue>("item") : root;
        Assert.AreEqual((byte)7, value.Get<byte>("tail"));
        Assert.IsFalse(value.ContainsKey(string.Empty));
    }
}
