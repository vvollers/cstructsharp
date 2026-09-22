namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Parsing;

/// <summary>Checks that only complete backslash-LF or backslash-CRLF pairs join physical lines.</summary>
[TestClass]
public class QuotedContinuationBoundaryTests
{
    /// <summary>A malformed continuation preserves the appropriate literal error without reading beyond the source.</summary>
    /// <param name="source">A quoted definition with an invalid escape or incomplete line ending.</param>
    /// <param name="expected">The literal diagnostic expected from the unchanged input.</param>
    [TestMethod]
    [DataRow("#define TEXT \"a\\x\nb\"", "a hexadecimal digit")]
    [DataRow("#define TEXT \"a\\\r", "the closing \"")]
    public void InvalidContinuation_PreservesTheLiteralDiagnostic(string source, string expected)
    {
        // A lone CR cannot make lookahead read one character beyond the definition.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(source));
        StringAssert.Contains(failure.Message, expected);
    }

    /// <summary>A line break inside trailing expression trivia must not move the eventual error into the comment.</summary>
    /// <param name="newline">The line ending inside the block comment.</param>
    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void CommentLineBreak_PreservesTheFollowingErrorLocation(string newline)
    {
        // The invalid top-level token is junk, not the already consumed closing comment delimiter.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLayout("#define VALUE 1/*" + newline + "*/junk"));
        StringAssert.Contains(failure.Message, "unexpected 'j' at line 2, column 3");
    }
}
