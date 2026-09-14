namespace CStructSharpTests;

using CStructSharp;

/// <summary>
///     Verifies that invalid or unsupported layout text is rejected during compilation with stable, located
///     diagnostics, while comments do not interfere with structural validation.
/// </summary>
[TestClass]
public class LayoutParsingValidationTests
{
    /// <summary>
    ///     A valid struct followed by garbage or an unfinished declaration must fail as a whole.
    /// </summary>
    /// <remarks>
    ///     A trailing comment is allowed. This prevents the constructor from accepting only the valid beginning of a
    ///     definition and silently ignoring a mistake later in the text.
    /// </remarks>
    [TestMethod]
    public void LayoutParser_RejectsTrailingNonCommentInput()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { byte value; }; trailing"));
        StringAssert.Contains(exception.Message, "line");
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { byte value; }; struct broken {"));

        _ = new CStruct("struct root { byte value; }; /* valid trailing comment */");
    }

    /// <summary>
    ///     The inputs include empty text, broken fields, an incomplete number, unsupported include syntax, and trailing
    ///     garbage.
    /// </summary>
    /// <remarks>
    ///     Every failure must be a layout error. Nonempty syntax errors must carry line and column information and
    ///     the shared "invalid syntax" prefix so a user can locate the faulty declaration.
    /// </remarks>
    [TestMethod]
    public void LayoutParser_MalformedCorpusHasStableLocatedDiagnostics()
    {
        string[] malformedLayouts =
        [
            " ",
            "struct root { ; };",
            "struct root { byte value[0x]; };",
            "struct root { byte value; }; struct broken {",
            "#include <stdint.h>\nstruct root { byte value; };",
            "struct root { byte value; };\nunsupported",
        ];

        foreach (string malformed in malformedLayouts)
        {
            CStructLayoutException exception = Assert.Throws<CStructLayoutException>(() => new CStruct(malformed));
            if (string.IsNullOrWhiteSpace(malformed))
            {
                StringAssert.Contains(exception.Message, "empty");
                continue;
            }

            StringAssert.Contains(exception.Message, "Layout definition contains invalid syntax: ");
            StringAssert.Contains(exception.Message, "line");
            StringAssert.Contains(exception.Message, "column");
        }
    }

    /// <summary>
    ///     An empty field and malformed hexadecimal or binary array counts exercise failures inside parser conversions.
    /// </summary>
    /// <remarks>
    ///     Callers must receive CStructLayoutException for all of them, rather than unrelated parser implementation
    ///     errors. The test concerns invalid layout text, not missing binary bytes.
    /// </remarks>
    [TestMethod]
    public void LayoutParser_NormalizesSemanticActionFailures()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { ; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { byte value[0x]; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { byte value[0b_]; };"));
    }

    /// <summary>
    ///     A prefix-operator chain as long as the definition-length limit allows parses without exhausting the
    ///     stack; the expression depth limit then rejects it as an ordinary layout error.
    /// </summary>
    /// <remarks>
    ///     Parentheses and braces are bounded by the source validator before parsing, but nothing bounds a run of
    ///     <c>-</c>, <c>~</c>, or <c>!</c> except the source length, so the parser must collect such a chain
    ///     iteratively rather than one stack frame per operator.
    /// </remarks>
    [TestMethod]
    public void LayoutParser_LongPrefixOperatorChainsDoNotOverflowTheStack()
    {
        string layout = "#define A " + new string('-', 100_000) + "1\nstruct root { uint8 a[A]; };";
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(() => new CStruct(layout));
        StringAssert.Contains(exception.Message, "depth");

        CStructSharp.Structure.Expr parsed = CStructDefinitionParser.ParseExpression("!~-" + new string('-', 50_000) + "7");
        Assert.IsInstanceOfType<CStructSharp.Structure.UnaryOp>(parsed);
    }

    /// <summary>
    ///     Braces inside a comment must not exceed a one-level nesting limit.
    /// </summary>
    /// <remarks>
    ///     In contrast, an unsized byte array and a variable-length string inside a union must fail for real storage
    ///     reasons. Unsized character fields have special support, but unions still require fixed-size members.
    /// </remarks>
    [TestMethod]
    public void CompilationValidation_IgnoresCommentBracesAndRejectsUnboundedStorage()
    {
        _ = new CStruct(
            "/* {{{{{{{{{{ */ struct root { byte value; };",
            compilationOptions: new CStructCompilationOptions { MaxLayoutNestingDepth = 1, });

        Assert.Throws<CStructLayoutException>(() => new CStruct("union root { char text[]; byte value; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { byte values[]; };"));
    }
}
