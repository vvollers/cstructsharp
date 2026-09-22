namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Parsing;

/// <summary>Checks the source locations behind declaration errors and the parser's union-body restrictions.</summary>
[TestClass]
public class ParserSourceLocationTests
{
    /// <summary>Semantic declaration failures identify the offending token rather than losing its source location.</summary>
    /// <param name="source">A layout with one invalid declaration.</param>
    /// <param name="token">The last occurrence of the token that caused the failure.</param>
    /// <param name="detail">The expected error category.</param>
    [TestMethod]
    [DataRow("enum kind { 32BIT = 1, 32BIT = 2 };", "32BIT", "Duplicate enum member")]
    [DataRow("#define uint8 \"reserved\"\n", "uint8", "conflicts with a built-in codec")]
    [DataRow("struct root { missing : 3; };", "missing", "Unknown type")]
    public void InvalidDeclarations_RetainTheirTokenOffset(string source, string token, string detail)
    {
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(source));
        StringAssert.Contains(failure.Message, detail);
        Assert.AreEqual(source.LastIndexOf(token, StringComparison.Ordinal), failure.SourceOffset);
        Assert.AreEqual(1, failure.Line);
        Assert.AreEqual(failure.SourceOffset + 1, failure.Column);
    }

    /// <summary>A union cannot have conditional members even when its condition is a literal.</summary>
    /// <param name="body">A conditional body accepted only by structs.</param>
    [TestMethod]
    [DataRow("if (1) { uint8 value; }")]
    [DataRow("switch (1) { case 1: { uint8 value; } }")]
    public void UnionBodies_RejectConditionalMembers(string body)
    {
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLayout("union root { " + body + " };"));
        Assert.HasCount(1, LayoutParser.ParseLayout("struct root { " + body + " };"));
    }
}
