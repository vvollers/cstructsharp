namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Introspection;
using CStructSharp.Parsing;

/// <summary>
///     Checks quoted constants - their decoded characters, escapes and byte strings - and line continuations: only a
///     complete backslash-LF or backslash-CRLF pair joins physical lines.
/// </summary>
[TestClass]
public class QuotedTextAndContinuationTests
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

    /// <summary>Quoted definitions retain each decoded character in both text and byte forms.</summary>
    /// <param name="body">The literal contents as written in the layout source.</param>
    /// <param name="expected">The decoded characters exposed by the constant.</param>
    [TestMethod]
    [DataRow("a\\nb", "a\nb")]
    [DataRow("a\\rb", "a\rb")]
    [DataRow("a\\tb", "a\tb")]
    [DataRow("a\\0b", "a\0b")]
    [DataRow("a\\\nb", "ab")]
    [DataRow("a\\\r\nb", "ab")]
    [DataRow("a\nb", "a\nb")]
    [DataRow("a\\\\b", "a\\b")]
    public void QuotedDefinitions_PreserveDecodedCharacters(string body, string expected)
    {
        var layout = new CStruct("#define TEXT \"" + body + "\"\n#define BYTES b'" + body + "'\nstruct root { uint8 value; };");

        Assert.AreEqual(LayoutConstantKind.Text, layout.Constants["TEXT"].Kind);
        Assert.AreEqual(expected, layout.Constants["TEXT"].Value);
        Assert.AreEqual(LayoutConstantKind.Bytes, layout.Constants["BYTES"].Kind);
        CollectionAssert.AreEqual(System.Text.Encoding.Latin1.GetBytes(expected), (byte[])layout.Constants["BYTES"].Value!);
        Assert.AreEqual((byte)7, layout.ReadValue<byte>(new byte[] { 7, }.AsSpan(), "root.value"));
    }

    /// <summary>A carriage return before a continued LF cannot consume the next declaration's first letter.</summary>
    [TestMethod]
    public void CarriageReturnBeforeLfContinuation_PreservesNextToken()
    {
        var layout = new CStruct("\r\\\nstruct root { uint8 value; };");
        dynamic parsed = layout.Parse(new byte[] { 42, }.AsSpan(), "root");
        Assert.AreEqual((byte)42, (byte)parsed.value);
    }
}
