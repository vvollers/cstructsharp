namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     A layout error about a declaration names the declaration and reports where it is: one-based line and
///     column on the exception and at the end of its message. Parser errors keep their own position text.
/// </summary>
[TestClass]
public class LayoutDiagnosticPositionTests
{
    /// <summary>A missing semicolon is reported at the field's type with the likely cause.</summary>
    [TestMethod]
    public void MissingSemicolon_ReportsPositionAndHint()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct header {\n  uint16 kind\n  uint32 length;\n};"));

        Assert.AreEqual(2, exception.Line);
        Assert.AreEqual(3, exception.Column);
        StringAssert.Contains(exception.Message, "Unknown type 'uint16 kind uint32' for field 'length' in struct 'header'");
        StringAssert.Contains(exception.Message, "a ';' may be missing after 'kind'");
        StringAssert.EndsWith(exception.Message, "(line 2, column 3)");
    }

    /// <summary>The hint also fires when the first word is a struct the layout declares, not only a built-in type.</summary>
    [TestMethod]
    public void MissingSemicolon_AfterDeclaredStructType_ReportsHint()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct header { uint8 kind; };\nstruct file {\n  header first\n  uint32 length;\n};"));

        StringAssert.Contains(exception.Message, "Unknown type 'header first uint32' for field 'length' in struct 'file'");
        StringAssert.Contains(exception.Message, "a ';' may be missing after 'first'");
    }

    /// <summary>A two-word spelling whose first word is not a type gets no semicolon hint.</summary>
    [TestMethod]
    public void UnknownTwoWordType_HasNoSemicolonHint()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct file { foo bar baz; };"));

        StringAssert.Contains(exception.Message, "Unknown type 'foo bar' for field 'baz' in struct 'file'");
        Assert.IsFalse(exception.Message.Contains("may be missing", StringComparison.Ordinal), exception.Message);
    }

    /// <summary>An anonymous composite is named as such, with its kind.</summary>
    [TestMethod]
    public void UnknownType_InAnonymousUnion_NamesTheKind()
    {
        CStructLayoutException inUnion = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct file { union { foo z; uint8 b; }; };"));
        StringAssert.Contains(inUnion.Message, "Unknown type 'foo' for field 'z' in an anonymous union");

        CStructLayoutException inStruct = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct file { struct { foo z; }; };"));
        StringAssert.Contains(inStruct.Message, "Unknown type 'foo' for field 'z' in an anonymous struct");

        CStructLayoutException inNamedUnion = Assert.Throws<CStructLayoutException>(
            () => new CStruct("union choice { foo z; uint8 b; };"));
        StringAssert.Contains(inNamedUnion.Message, "Unknown type 'foo' for field 'z' in union 'choice'");
    }

    /// <summary>An unknown type names the field and the struct and points at the type spelling.</summary>
    [TestMethod]
    public void UnknownType_ReportsFieldStructAndPosition()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct a { uint8 x; };\nstruct b { uint8 y; };\nstruct c { foo z; };"));

        Assert.AreEqual(3, exception.Line);
        Assert.AreEqual(12, exception.Column);
        StringAssert.Contains(exception.Message, "Unknown type 'foo' for field 'z' in struct 'c'");
        Assert.AreEqual(CStructErrorCode.InvalidLayout, exception.Code);
    }

    /// <summary>Duplicate names point at the second spelling.</summary>
    [TestMethod]
    public void DuplicateNames_PointAtTheRepeatedDeclaration()
    {
        CStructLayoutException field = Assert.Throws<CStructLayoutException>(() => new CStruct("struct a { uint8 x;\n uint8 x; };"));
        Assert.AreEqual((2, 8), (field.Line, field.Column));

        CStructLayoutException declaration = Assert.Throws<CStructLayoutException>(() => new CStruct("struct a { uint8 x; };\nstruct a { uint8 y; };"));
        Assert.AreEqual((2, 8), (declaration.Line, declaration.Column));

        CStructLayoutException member = Assert.Throws<CStructLayoutException>(() => new CStruct("enum e : uint8 { A = 1,\n A = 2 };"));
        Assert.AreEqual((2, 2), (member.Line, member.Column));
    }

    /// <summary>A by-value recursion points at the recursive field.</summary>
    [TestMethod]
    public void ByValueRecursion_PointsAtTheField()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct node { uint32 v;\n struct node next; };"));

        Assert.AreEqual((2, 14), (exception.Line, exception.Column));
    }

    /// <summary>A parser error keeps its own position text and leaves the properties unset.</summary>
    [TestMethod]
    public void SyntaxError_KeepsParserPositionText()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct header { uint16 kind; $ };"));

        StringAssert.Contains(exception.Message, "at line 1, column 30");
        Assert.IsNull(exception.Line);
        Assert.IsNull(exception.Column);
    }

    /// <summary>Positions count over the text the layout was given, prelude included.</summary>
    [TestMethod]
    public void Prelude_ShiftsPositions()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct a { bar x; };", compilationOptions: new CStructCompilationOptions { Prelude = "#define ONE 1", }));

        Assert.AreEqual(2, exception.Line);
        Assert.AreEqual(12, exception.Column);
    }
}
