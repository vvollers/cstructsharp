namespace CStructSharpTests;

using CStructSharp;

/// <summary>Verifies the complete lexical grammar shared by every public path-based operation.</summary>
[TestClass]
public class PathGrammarTests
{
    /// <summary>Returns the selected root value rather than leaking the parser's internal declaration-name wrapper.</summary>
    [TestMethod]
    public void ParseStream_RootPathPreservesThePublicObjectShape()
    {
        var cstruct = new CStruct("struct root { byte value; };");

        dynamic parsed = cstruct.ParseStream(new MemoryStream([0x2A,]), "root");

        Assert.AreEqual((byte)0x2A, (byte)parsed.value);
        Assert.IsFalse(((IDictionary<string, object?>)parsed).ContainsKey("root"));
    }

    /// <summary>Accepts case-sensitive identifiers, underscores, Unicode letters, and the complete non-negative index domain.</summary>
    [TestMethod]
    public void Parse_AcceptsNamesAndNonNegativeIndexes()
    {
        IReadOnlyList<PathSegment> segments =
            CStructPathResolver.Parse(" _root.items_2[2147483647].Élément[00].digit[9] ");

        Assert.AreEqual(4, segments.Count);
        Assert.AreEqual("_root", segments[0].Name);
        Assert.IsNull(segments[0].Index);
        Assert.AreEqual("items_2", segments[1].Name);
        Assert.AreEqual(int.MaxValue, segments[1].Index);
        Assert.AreEqual("Élément", segments[2].Name);
        Assert.AreEqual(0, segments[2].Index);
        Assert.AreEqual("digit", segments[3].Name);
        Assert.AreEqual(9, segments[3].Index);
    }

    /// <summary>Rejects empty segments, malformed names/brackets, signed or non-decimal indexes, and Int32 overflow.</summary>
    /// <param name="path">The invalid public path text.</param>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" \t")]
    [DataRow(".root")]
    [DataRow("root.")]
    [DataRow("root..field")]
    [DataRow("1root")]
    [DataRow("root.1field")]
    [DataRow("root.bad-name")]
    [DataRow("root.field name")]
    [DataRow("[0]")]
    [DataRow("root.items[")]
    [DataRow("root.items]")]
    [DataRow("root.items[]")]
    [DataRow("root.items[-1]")]
    [DataRow("root.items[+1]")]
    [DataRow("root.items[ 1]")]
    [DataRow("root.items[1.0]")]
    [DataRow("root.items[one]")]
    [DataRow("root.items[2147483648]")]
    [DataRow("root.items[0]tail")]
    [DataRow("root.items[0][1]")]
    [DataRow("root.items[[0]")]
    [DataRow("root.items[0]]")]
    public void Parse_RejectsTextOutsideThePathGrammar(string? path)
    {
        Assert.Throws<CStructPathException>(() => CStructPathResolver.Parse(path!));
    }
}
