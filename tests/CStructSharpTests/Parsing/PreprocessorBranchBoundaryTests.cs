namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Introspection;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks nested inactive directives, definition removal and include/constant token boundaries.</summary>
[TestClass]
public class PreprocessorBranchBoundaryTests
{
    /// <summary>A single-element parse skips leading directives that do not themselves declare an element.</summary>
    /// <param name="directive">A directive whose only effect is parser state.</param>
    [TestMethod]
    [DataRow("#pragma once")]
    [DataRow("#undef MISSING")]
    public void SingleElement_SkipsNonDeclaringDirectives(string directive)
    {
        CStructElement element = LayoutParser.ParseElement(directive + "\nstruct root { uint8 value; };");
        Assert.AreEqual("root", element.Name.Name);
    }

    /// <summary>Popping an empty packing stack leaves the ordinary aligned layout unchanged.</summary>
    [TestMethod]
    public void EmptyPackPop_PreservesDefaultAlignment()
    {
        var layout = new CStruct("#pragma pack(pop)\nstruct root { uint8 prefix; uint32 value; };", aligned: true);
        Assert.AreEqual(8, layout.GetStructSizeInBytes("root"));
    }

    /// <summary>Nested conditionals inside an inactive branch cannot expose their else arm or close the outer branch.</summary>
    /// <param name="innerDirective">Either supported opening directive inside the skipped outer branch.</param>
    [TestMethod]
    [DataRow("#ifdef")]
    [DataRow("#ifndef")]
    public void InactiveOuterBranch_SkipsNestedConditionalArms(string innerDirective)
    {
        string source = "#ifdef MISSING\n" + innerDirective + " INNER\ninvalid inner arm\n#else\ninvalid inner else\n#endif\ninvalid outer arm\n#else\nstruct root { uint16 value; };\n#endif";
        var layout = new CStruct(source);
        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));
    }

    /// <summary>Undef removes only its named definition and its conditional visibility, retaining unrelated declarations.</summary>
    [TestMethod]
    public void Undef_PreservesOtherDefinitionsAndDeclarations()
    {
        const string source = "#define REMOVE 1\n#define KEEP 2\nstruct preserved { uint8 marker; };\n#undef REMOVE\n#ifdef REMOVE\ninvalid removed arm\n#else\nstruct root { uint8 values[KEEP]; };\n#endif";
        var layout = new CStruct(source);
        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(1, layout.GetStructSizeInBytes("preserved"));
        Assert.IsFalse(layout.Constants.ContainsKey("REMOVE"));
        Assert.IsTrue(layout.Constants.ContainsKey("KEEP"));
    }

    /// <summary>Truncated include directives fail with their missing delimiter rather than indexing beyond source text.</summary>
    /// <param name="source">The incomplete include declaration.</param>
    /// <param name="expectation">The missing path or closing delimiter named by the parser.</param>
    [TestMethod]
    [DataRow("#include", "a quoted or angle-bracketed include path")]
    [DataRow("#include <unfinished", "'>'")]
    [DataRow("#include \"unfinished", "'\"'")]
    public void IncompleteInclude_ExplainsTheMissingDelimiter(string source, string expectation)
    {
        // Parse the directive itself so no later compilation error can hide an incorrect lexical failure.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseElement(source));
        StringAssert.Contains(failure.Message, "expected " + expectation);
    }

    /// <summary>Parsed include metadata distinguishes system paths from local paths without resolving either.</summary>
    /// <param name="source">An angle-bracketed or quoted include declaration.</param>
    /// <param name="system">Whether the source used angle brackets.</param>
    [TestMethod]
    [DataRow("#include <types.h>", true)]
    [DataRow("#include \"types.h\"", false)]
    public void Include_RetainsItsDelimiterKind(string source, bool system)
    {
        var include = (IncludeDirective)LayoutParser.ParseElement(source);
        Assert.AreEqual("types.h", include.Path);
        Assert.AreEqual(system, include.IsSystem);
    }

    /// <summary>A final b identifier is an expression, not a byte-string prefix requiring a following character.</summary>
    [TestMethod]
    public void FinalBIdentifier_DoesNotReadBeyondTheSource()
    {
        var definition = (CStructSharp.Syntax.Defines)LayoutParser.ParseElement("#define VALUE b");
        Assert.AreEqual("VALUE", definition.Name.Name);
        Assert.AreEqual("b", ((Identifier)definition.Value).Name);
    }

    /// <summary>Only the b prefix creates byte-string constants; other token bodies remain opaque text.</summary>
    [TestMethod]
    public void OtherQuotedPrefix_RemainsText()
    {
        var layout = new CStruct("#define TEXT x\"abc\"\nstruct root { uint8 value; };");
        Assert.AreEqual(LayoutConstantKind.Text, layout.Constants["TEXT"].Kind);
        Assert.AreEqual("x\"abc\"", layout.Constants["TEXT"].Value);
    }
}
