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
    ///     Requires the top-level grammar to consume all non-comment input so an apparently accepted header can never
    ///     be only a valid prefix followed by ignored declarations or garbage.
    /// </summary>
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
    ///     Feeds several unsupported or incomplete grammar forms through the public constructor. Every one must identify
    ///     the failure as invalid layout text and retain Pidgin's line/column context, regardless of which token failed.
    /// </summary>
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
    ///     Normalizes malformed fields and base-prefixed literals to the documented layout exception instead of leaking
    ///     parser implementation exceptions to callers.
    /// </summary>
    [TestMethod]
    public void LayoutParser_NormalizesSemanticActionFailures()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { ; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { byte value[0x]; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { byte value[0b_]; };"));
    }

    /// <summary>
    ///     Treats braces inside comments as text rather than declaration nesting and rejects variable-size union members
    ///     or unsized non-character arrays during layout compilation.
    /// </summary>
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
