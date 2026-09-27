namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Introspection;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>
///     Checks comments and whitespace inside declarations: comment lookahead neither consumes opaque text nor reads
///     past a directive's end, and trivia leaves a function-pointer declaration's shape intact.
/// </summary>
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

    /// <summary>Trivia around the pointer marker and after its parameter list does not change the stored pointer.</summary>
    /// <param name="declaration">A supported function-pointer field with boundary trivia.</param>
    [TestMethod]
    [DataRow("void ( /* marker */ *callback)(void);")]
    [DataRow("void (*callback)(void) /* trailing */ ;")]
    public void FunctionPointer_PreservesItsShapeAcrossTrivia(string declaration)
    {
        IReadOnlyList<Field> fields = LayoutParser.ParseFieldGroup(declaration);
        Assert.HasCount(1, fields);
        Assert.AreEqual("callback", fields[0].Name.Name);
        Assert.AreEqual("void", fields[0].Type.Name);
        Assert.AreEqual(1, fields[0].PointerDepth);
        var layout = new CStruct("struct root { " + declaration + " };", pointerSize: 8);
        Assert.AreEqual(8, layout.GetStructSizeInBytes("root"));
    }
}
