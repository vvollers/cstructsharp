namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks exact reader limits and caller-visible names retained by debug traversal.</summary>
[TestClass]
public class ReaderBoundaryTests
{
    /// <summary>A multidimensional array may contain exactly the configured maximum number of leaf elements.</summary>
    /// <param name="debug">Whether the reader also captures field ranges.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MultidimensionalArray_AcceptsTheExactLeafLimit(bool debug)
    {
        var layout = new CStruct("struct root { uint8 values[2][2]; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 1, 2, 3, 4, 9, });
        var options = new ReadOptions { MaxArrayElements = 4, };
        StructValue result = debug ? layout.ParseWithDebug(source, "root", options: options).Value : layout.Parse(source, "root", options: options);
        var rows = (IList<object?>)result["values"]!;
        Assert.AreEqual(2, rows.Count);
        CollectionAssert.AreEqual(new object[] { (byte)1, (byte)2, }, ((IList<object?>)rows[0]!).ToArray());
        CollectionAssert.AreEqual(new object[] { (byte)3, (byte)4, }, ((IList<object?>)rows[1]!).ToArray());
        Assert.AreEqual((byte)9, result["tail"]);
        Assert.AreEqual(5L, source.Position);
    }

    /// <summary>Debug paths use the requested typedef alias, not the implementation's different struct tag.</summary>
    [TestMethod]
    public void TaggedAlias_DebugPathsUseThePublicAlias()
    {
        var layout = new CStruct("typedef struct hidden_tag { uint8 value; } public_alias;");
        ParseResult result = layout.ParseWithDebug(new byte[] { 7, }, "public_alias");
        Assert.AreEqual((byte)7, result.Value["value"]);

        // The leaf's public path and byte range are part of the debug result, not just its decoded value.
        Assert.IsTrue(result.Debug.Any(item => item.Path == "public_alias.value" && item.Start == 0 && item.End == 1));
        foreach (var item in result.Debug)
        {
            Assert.IsFalse(item.Path.Contains("hidden_tag", StringComparison.Ordinal), item.Path);
        }
    }
}
