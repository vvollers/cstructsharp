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
    ///     Every failure must be a layout error. Nonempty syntax errors must retain line and column information and
    ///     their underlying cause so a user can locate the faulty declaration.
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

            StringAssert.Contains(exception.Message, "line");
            StringAssert.Contains(exception.Message, "col");
            Assert.IsNotNull(exception.InnerException);
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
