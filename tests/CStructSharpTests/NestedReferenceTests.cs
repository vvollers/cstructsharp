namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>
///     Dotted references to a nested struct's field in an expression (<c>uint8 v[hdr.n]</c>): the nested field is
///     captured and republished under its qualified name while the struct field is read, written, or measured.
/// </summary>
[TestClass]
public class NestedReferenceTests
{
    private const string Layout = "struct h { uint8 n; uint8 pad; }; struct root { h hdr; uint8 v[hdr.n]; uint8 tail; };";

    /// <summary>The count comes from the nested field through every operation.</summary>
    [TestMethod]
    public void DottedReference_RoundTripsThroughEveryOperation()
    {
        var layout = new CStruct(Layout);
        byte[] bytes = [2, 0, 7, 8, 9,];
        using var stream = new MemoryStream((byte[])bytes.Clone());

        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        CollectionAssert.AreEqual(new byte[] { 7, 8, }, ((IEnumerable<object?>)parsed.v).Select(item => (byte)item!).ToArray());
        Assert.AreEqual((byte)9, (byte)parsed.tail);

        List<DebugData> debug = layout.ParseStreamWithDebug(stream, "root").DebugData;
        Assert.IsTrue(debug.Any(item => item.Path == "root.tail" && item.Start == 4));
        stream.Position = 0;
        Assert.AreEqual(4, layout.ResolveAddress(stream, "root.tail"));
        Assert.AreEqual(2, layout.GetDynamicArrayLength(stream, "root.v"));
        Assert.AreEqual((byte)9, layout.ReadValue<byte>(bytes.AsSpan(), "root.tail"));

        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
        byte[] written = layout.Serialize(
            "root",
            new Dictionary<string, object?>
            {
                ["hdr"] = new Dictionary<string, object?> { ["n"] = (byte)1, ["pad"] = (byte)0, },
                ["v"] = new byte[] { 5, },
                ["tail"] = (byte)9,
            });
        CollectionAssert.AreEqual(new byte[] { 1, 0, 5, 9, }, written);
        using var target = new MemoryStream();
        layout.WriteStream(target, "root", parsed);
        CollectionAssert.AreEqual(bytes, target.ToArray());

        layout.UpdateStream(stream, "root.tail", (byte)6);
        CollectionAssert.AreEqual(new byte[] { 2, 0, 7, 8, 6, }, stream.ToArray());
    }

    /// <summary>Two fields of the same nested type are told apart by their qualified names; a bare name means the last one read.</summary>
    [TestMethod]
    public void DottedReference_SelectsTheNamedField()
    {
        var layout = new CStruct("struct h { uint8 n; }; struct root { h a; h b; uint8 x[a.n]; uint8 y[b.n]; uint8 z[n]; };");
        dynamic parsed = layout.Parse(new byte[] { 1, 2, 10, 20, 21, 30, 31, }.AsSpan(), "root");
        Assert.AreEqual(1, ((IEnumerable<object?>)parsed.x).Count());
        Assert.AreEqual(2, ((IEnumerable<object?>)parsed.y).Count());
        Assert.AreEqual(2, ((IEnumerable<object?>)parsed.z).Count());
    }

    /// <summary>A reference may reach through several levels (<c>a.b.n</c>) and a nested field may be an enum.</summary>
    [TestMethod]
    public void DottedReference_ReachesThroughLevels()
    {
        var layout = new CStruct("enum k : uint8 { ONE = 1, TWO = 2 }; struct inner { k n; }; struct outer { inner b; uint8 pad; }; struct root { outer a; uint8 v[a.b.n]; uint8 tail; };");
        byte[] bytes = [2, 0, 7, 8, 9,];
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual(2, ((IEnumerable<object?>)parsed.v).Count());
        using var stream = new MemoryStream(bytes);
        Assert.AreEqual(4, layout.ResolveAddress(stream, "root.tail"));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
    }

    /// <summary>A path that names no nested struct field is still an undefined identifier at use.</summary>
    [TestMethod]
    public void DottedReference_ToNothing_FailsAtUse()
    {
        var layout = new CStruct("struct h { uint8 n; }; struct root { h hdr; uint8 v[other.n]; };");
        Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 1, 2, }.AsSpan(), "root"));
    }
}
