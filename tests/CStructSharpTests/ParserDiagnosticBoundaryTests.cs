namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks actionable syntax diagnostics and declaration metadata at parser boundaries.</summary>
[TestClass]
public class ParserDiagnosticBoundaryTests
{
    /// <summary>Malformed directives, literals and declarators retain the expected missing-token explanation.</summary>
    /// <param name="source">The incomplete or malformed layout source.</param>
    /// <param name="expected">The corrective detail expected in the syntax error.</param>
    [TestMethod]
    [DataRow("#ifdef MISSING\n", "#endif")]
    [DataRow("#define PRESENT 1\n#ifdef PRESENT\n", "#endif")]
    [DataRow("#else\n", "a matching #ifdef or #ifndef before #else")]
    [DataRow("#endif\n", "a matching #ifdef or #ifndef before #endif")]
    [DataRow("#define PRESENT 1\n#ifdef PRESENT\n#else\n#else\n#endif\n", "a matching #ifdef or #ifndef before #else")]
    [DataRow("#include plain.h\n", "a quoted or angle-bracketed include path")]
    [DataRow("#include <missing", "'>'")]
    [DataRow("#include \"missing", "'\"'")]
    [DataRow("#unknown\n", "a preprocessor directive (#define, #undef, #include, #pragma, #ifdef, #ifndef, #else, #endif)")]
    [DataRow("#define TEXT \"unfinished", "the closing \"")]
    [DataRow("#define TEXT \"trailing\\", "an escape sequence")]
    [DataRow("#define TEXT \"\\xG\"", "a hexadecimal digit")]
    [DataRow("/* unfinished", "the end of the block comment")]
    [DataRow("typedef uint8 bytes[];", "A typedef array needs a count in every dimension.")]
    [DataRow("enum kind { = 1 };", "expected '}'")]
    [DataRow("struct root { void (*callback)(uint8 value;", "')'")]
    [DataRow("struct root { uint8 bytes[sizeof(1)]; };", "a type or field name")]
    public void InvalidSyntax_ExplainsTheMissingOrInvalidToken(string source, string expected)
    {
        // Use the parser directly so name resolution cannot substitute a later compilation error.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLayout(source));
        StringAssert.StartsWith(failure.Message, LayoutParser.SyntaxErrorPrefix);
        StringAssert.Contains(failure.Message, expected);
    }

    /// <summary>A tag alias retains its declared kind so the compiler can detect a mismatched declaration.</summary>
    /// <param name="keyword">The CStruct declaration keyword used before the tag.</param>
    /// <param name="kind">The compiled type-kind hint expected on the alias.</param>
    [TestMethod]
    [DataRow("struct", "struct")]
    [DataRow("union", "union")]
    [DataRow("enum", "enum")]
    [DataRow("flag", "enum")]
    public void TagAliases_RetainTheTypeKeywordHint(string keyword, string kind)
    {
        CStructElement element = LayoutParser.ParseLayout("typedef " + keyword + " Tag Alias;").Single();
        Assert.IsInstanceOfType<Typedef>(element);
        var alias = (Typedef)element;
        Assert.AreEqual("Alias", alias.Name.Name);
        Assert.AreEqual(kind, alias.TypeKeywordHint);
    }

    /// <summary>Anonymous composites retain empty names so their members can be promoted into the parent.</summary>
    [TestMethod]
    public void AnonymousComposites_KeepTheirPromotionMetadata()
    {
        var root = (Struct)LayoutParser.ParseLayout("struct root { struct { uint8 a; }; union { uint8 b; uint16 c; }; };").Single();
        Assert.AreEqual(2, root.Fields.Count);
        foreach (Field field in root.Fields)
        {
            Assert.IsInstanceOfType<Struct>(field);
            Assert.AreEqual(string.Empty, field.Name.Name);
        }

        Assert.IsFalse(((Struct)root.Fields[0]).IsUnion);
        Assert.IsTrue(((Struct)root.Fields[1]).IsUnion);
    }
}
