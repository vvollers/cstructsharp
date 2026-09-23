namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Parsing;

/// <summary>Checks directive line boundaries, initial packing and failed function-pointer recognition.</summary>
[TestClass]
public class ParserResidualBoundaryTests
{
    /// <summary>Adjacent continued lines remain one text definition even when the body starts with a number.</summary>
    [TestMethod]
    public void AdjacentContinuations_PreserveTheTextBody()
    {
        var layout = new CStruct("#define TEXT 1\\\n\\\nXYZ\nstruct root { uint8 value; };");
        Assert.AreEqual("1 XYZ", layout.Constants["TEXT"].Value);
    }

    /// <summary>A plain pack directive establishes the initial alignment clamp without requiring a push.</summary>
    [TestMethod]
    public void InitialPack_SetsTheClamp()
    {
        var layout = new CStruct("#pragma pack(1)\nstruct root { uint8 prefix; uint32 value; };", aligned: true);
        Assert.AreEqual(1, layout.GetStructAlignmentInBytes("root"));
        Assert.AreEqual(5, layout.GetStructSizeInBytes("root"));
    }

    /// <summary>A plain pack directive replaces an existing clamp without pushing a new stack entry.</summary>
    [TestMethod]
    public void PlainPack_ReplacesTheCurrentClamp()
    {
        var layout = new CStruct("#pragma pack(push, 1)\n#pragma pack(2)\nstruct root { uint8 prefix; uint32 value; };\n#pragma pack(pop)\nstruct natural { uint8 prefix; uint32 value; };", aligned: true);
        Assert.AreEqual(2, layout.GetStructAlignmentInBytes("root"));
        Assert.AreEqual(6, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(4, layout.GetStructAlignmentInBytes("natural"));
        Assert.AreEqual(8, layout.GetStructSizeInBytes("natural"));
    }

    /// <summary>A continuation joins literal text without interpreting the next ordinary character as an escape.</summary>
    /// <param name="suffix">A character that would have a special meaning after a backslash.</param>
    [TestMethod]
    [DataRow("n")]
    [DataRow("t")]
    public void QuotedContinuation_DoesNotEscapeTheFollowingCharacter(string suffix)
    {
        var layout = new CStruct("#define TEXT \"A\\\n" + suffix + "\"\nstruct root { uint8 value; };");
        Assert.AreEqual("A" + suffix, layout.Constants["TEXT"].Value);
    }

    /// <summary>An ignored directive stops at the first non-continued line ending, preserving the next declaration.</summary>
    [TestMethod]
    public void IgnoredDirective_StopsAtTheBlankLine()
    {
        var layout = new CStruct("#pragma ignored\\\n\nstruct root { uint8 value; };");
        Assert.AreEqual(1, layout.GetStructSizeInBytes("root"));
    }

    /// <summary>A nested tag is published once, not inserted again before a later top-level declaration.</summary>
    [TestMethod]
    public void HoistedTag_IsNotRepeatedForTheNextDeclaration()
    {
        var layout = new CStruct("struct outer { struct inner { uint8 value; } child; }; struct root { outer item; };");
        Assert.AreEqual(1, layout.GetStructSizeInBytes("inner"));
        Assert.AreEqual(1, layout.GetStructSizeInBytes("root"));
    }

    /// <summary>An incomplete signature restores recognition to its opening parenthesis and reports a syntax error.</summary>
    /// <param name="source">A pointer declarator without the required function parameter list.</param>
    [TestMethod]
    [DataRow("void (*name)")]
    [DataRow("void (*name);")]
    public void IncompleteFunctionPointer_ReportsTheUnrecognizedParenthesis(string source)
    {
        // Recognition must fail normally, including when the closing declarator parenthesis is the last character.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => CStructDefinitionParser.ParseFieldGroup(source));
        StringAssert.Contains(failure.Message, "expected ';'");
    }
}
