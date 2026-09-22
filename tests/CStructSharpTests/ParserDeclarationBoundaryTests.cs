namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Diagnostics;
using CStructSharp.Parsing;
using CStructSharp.Syntax;
using CStructSharp.Values;
using SyntaxEnum = CStructSharp.Syntax.Enum;

/// <summary>Checks declaration counts and enum/flag identity across supported typedef spellings.</summary>
[TestClass]
public class ParserDeclarationBoundaryTests
{
    /// <summary>The single-declaration entry point rejects one statement that declares multiple aliases.</summary>
    [TestMethod]
    public void ParseElement_RejectsMultipleTypedefDeclarations()
    {
        const string source = "typedef uint8 first, second;";
        Assert.HasCount(2, CStructDefinitionParser.ParseLayout(source));

        // Parsing one statement is not the same as obtaining exactly one declared type.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => CStructDefinitionParser.ParseElement(source));
        StringAssert.Contains(failure.Message, "expected a struct, union, typedef, enum, or #define declaration");
    }

    /// <summary>Tagged, anonymous and same-name typedef bodies preserve their enum or flag identity and storage width.</summary>
    /// <param name="declaration">One typedef body producing exactly one enum declaration.</param>
    /// <param name="name">The type name made available to a following struct.</param>
    /// <param name="flag">Whether parsed values expose flag decomposition.</param>
    [TestMethod]
    [DataRow("typedef enum tag : uint8 { A=1 };", "tag", false)]
    [DataRow("typedef flag tag : uint8 { A=1 };", "tag", true)]
    [DataRow("typedef enum tag : uint8 { A=1 } tag;", "tag", false)]
    [DataRow("typedef flag tag : uint8 { A=1 } tag;", "tag", true)]
    [DataRow("typedef enum : uint8 { A=1 } alias;", "alias", false)]
    [DataRow("typedef flag : uint8 { A=1 } alias;", "alias", true)]
    public void EnumTypedef_PreservesDeclarationIdentity(string declaration, string name, bool flag)
    {
        IReadOnlyList<CStructElement> elements = CStructDefinitionParser.ParseLayout(declaration);
        Assert.HasCount(1, elements);
        SyntaxEnum enumeration = Assert.IsInstanceOfType<SyntaxEnum>(elements[0]);
        Assert.AreEqual(name, enumeration.Name.Name);
        Assert.AreEqual("uint8", enumeration.Type.Name);
        Assert.AreEqual(flag, enumeration.IsFlag);

        var layout = new CStruct(declaration + $" struct root {{ {name} value; }};");
        Assert.AreEqual(1, layout.GetStructSizeInBytes("root"));
        EnumValueResult value = layout.ReadValue<EnumValueResult>(new byte[] { 1, }.AsSpan(), "root.value");
        Assert.AreEqual(flag, value is FlagValueResult);
        Assert.AreEqual(name, value.Enum);
        Assert.AreEqual("A", value.Name);
        Assert.AreEqual(BigInteger.One, value.Value);
        Assert.AreEqual(8, value.BitWidth);
    }

    /// <summary>A distinct enum alias retains its tag-kind constraint and a trailing pointer alias retains its depth.</summary>
    [TestMethod]
    public void EnumTypedef_PreservesDistinctAndPointerAliases()
    {
        const string source = "typedef enum tag : uint8 { A=1 } alias, *pointer;";
        IReadOnlyList<CStructElement> elements = CStructDefinitionParser.ParseLayout(source);
        Assert.HasCount(3, elements);
        Typedef alias = Assert.IsInstanceOfType<Typedef>(elements[1]);
        Assert.AreEqual("alias", alias.Name.Name);
        Assert.AreEqual("tag", alias.Type.Name);
        Assert.AreEqual("enum", alias.TypeKeywordHint);

        var layout = new CStruct(source + " struct root { alias value; pointer target; };", pointerSize: 1);
        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));
        Pointer pointer = layout.ReadValue<Pointer>(new byte[] { 1, 2, 1, }.AsSpan(), "root.target");
        Assert.AreEqual(2L, pointer.Address);
        Assert.IsTrue(pointer.IsDereferenced);
        EnumValueResult value = Assert.IsInstanceOfType<EnumValueResult>(pointer.Value);
        Assert.AreEqual("tag", value.Enum);
        Assert.AreEqual("A", value.Name);
    }
}
