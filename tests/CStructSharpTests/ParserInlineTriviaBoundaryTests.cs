namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Introspection;

/// <summary>Checks that comment lookahead does not consume opaque text or read beyond a directive's end.</summary>
[TestClass]
public class ParserInlineTriviaBoundaryTests
{
    /// <summary>A slash or star is ordinary opaque text unless the complete comment opener is present.</summary>
    /// <param name="body">A non-expression definition body containing comment-like punctuation.</param>
    [TestMethod]
    [DataRow("/")]
    [DataRow("x*")]
    [DataRow("x/")]
    public void OpaqueDefinition_PreservesCommentLikeCharacters(string body)
    {
        var layout = new CStruct("#define VALUE " + body);
        Assert.AreEqual(LayoutConstantKind.Text, layout.Constants["VALUE"].Kind);
        Assert.AreEqual(body, layout.Constants["VALUE"].Value);
    }

    /// <summary>The star opening a block comment cannot simultaneously close that same comment.</summary>
    [TestMethod]
    public void OverlappingCommentDelimiters_AreNotAClosedComment()
    {
        // A closing delimiter must begin after the complete opening delimiter.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct("#define FLAG /*/"));
        StringAssert.Contains(failure.Message, "the end of the block comment");
    }
}
