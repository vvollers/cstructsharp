namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks that typedef roots retain one public alias in their debug paths.</summary>
[TestClass]
public class ReaderAliasDebugTests
{
    /// <summary>All struct-alias forms identify a leaf without duplicating the alias or exposing the tag.</summary>
    /// <param name="declaration">An inline, separate or chained alias declaration.</param>
    /// <param name="streamBacked">Whether parsing borrows a stream instead of a byte span.</param>
    [TestMethod]
    [DataRow("typedef struct hidden_tag { uint8 value; } public_alias;", false)]
    [DataRow("typedef struct hidden_tag { uint8 value; } public_alias;", true)]
    [DataRow("struct hidden_tag { uint8 value; }; typedef hidden_tag public_alias;", false)]
    [DataRow("struct hidden_tag { uint8 value; }; typedef hidden_tag public_alias;", true)]
    [DataRow("struct hidden_tag { uint8 value; }; typedef hidden_tag intermediate; typedef intermediate public_alias;", false)]
    [DataRow("struct hidden_tag { uint8 value; }; typedef hidden_tag intermediate; typedef intermediate public_alias;", true)]
    public void StructAlias_HasOnePublicRootSegment(string declaration, bool streamBacked)
    {
        var layout = new CStruct(declaration);
        byte[] bytes = [7,];
        using var source = new MemoryStream(bytes);
        ParseResult result = streamBacked
            ? layout.ParseWithDebug(source, "public_alias")
            : layout.ParseWithDebug(bytes.AsSpan(), "public_alias");
        Assert.AreEqual((byte)7, result.Value["value"]);
        DebugData leaf = result.Debug.Single();
        Assert.AreEqual("public_alias.value", leaf.Path);
        Assert.AreEqual(0L, leaf.Start);
        Assert.AreEqual(1L, leaf.End);

        // The generic value reader and struct parser expose the same declaration coordinates.
        source.Position = 0;
        var selected = layout.ReadValueWithDebug(source, "public_alias");
        DebugData selectedLeaf = selected.Debug.Single();
        Assert.AreEqual(leaf.Path, selectedLeaf.Path);
        Assert.AreEqual(leaf.Start, selectedLeaf.Start);
        Assert.AreEqual(leaf.End, selectedLeaf.End);
    }
}
