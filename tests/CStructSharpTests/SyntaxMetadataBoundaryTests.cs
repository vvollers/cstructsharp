namespace CStructSharp.Tests;

using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;
using SyntaxEnum = CStructSharp.Syntax.Enum;

/// <summary>Checks the syntax nodes consumed by layout compilation without depending on later normalized metadata.</summary>
[TestClass]
public class SyntaxMetadataBoundaryTests
{
    /// <summary>Anonymous padding bitfields occupy bytes but never become named shape members or field lookup results.</summary>
    [TestMethod]
    public void CompiledShape_OmitsUnnamedPaddingAndDescribesMissingFields()
    {
        var layout = new CStruct("struct root { uint8 :3; uint8 a:5; uint8 :2; uint8 b:6; };");
        var compiled = (CompiledCompositeType)layout.CompiledModel.Composites[layout.GetStruct("root")].Definition!;
        CollectionAssert.AreEqual(new[] { "a", "b", }, compiled.Shape.Names.ToArray());
        Assert.IsFalse(compiled.TryFindField(string.Empty, out _));

        // A missing member must identify its containing declaration as well as the requested name.
        CStructPathException failure = Assert.Throws<CStructPathException>(() => compiled.FindField("missing"));
        StringAssert.StartsWith(failure.Message, "Unknown field 'missing' in 'root'.");
    }

    /// <summary>Both field constructors combine pointer spellings unless an explicit depth overrides them, including zero.</summary>
    /// <param name="expressionWidth">Whether the constructor receives a parsed expression rather than a resolved width.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PointerDepth_DerivesFromBothNamesAndHonorsZeroOverride(bool expressionWidth)
    {
        var type = new Identifier("uint8*");
        var name = new Identifier("**value");
        Field inferred = expressionWidth ? new Field(type, name, Field.NoArray, NoneExpr.Instance) : new Field(type, name, Field.NoArray, 0);
        Field overridden = expressionWidth ? new Field(type, name, Field.NoArray, NoneExpr.Instance, pointerDepth: 0) : new Field(type, name, Field.NoArray, 0, pointerDepth: 0);
        Assert.AreEqual(3, inferred.PointerDepth);
        Assert.IsTrue(inferred.IsPointer);
        Assert.AreEqual(0, overridden.PointerDepth);
        Assert.IsFalse(overridden.IsPointer);
        var alignments = new Dictionary<string, int> { ["uint8"] = 1, };
        Assert.AreEqual(8, inferred.GetAlignment(alignments, 8));
        Assert.AreEqual(1, overridden.GetAlignment(alignments, 8));
        Assert.IsTrue(inferred.IsKnown(new Dictionary<string, int>()));
        Assert.IsTrue(overridden.IsKnown(alignments));
        Assert.IsFalse(overridden.IsKnown(new Dictionary<string, int>()));
    }

    /// <summary>Parsed widths distinguish ordinary fields, explicit positive widths and invalid negative expressions.</summary>
    [TestMethod]
    public void ParsedWidth_EvaluatesItsExpressionAndExplainsNegativeValues()
    {
        var type = new Identifier("uint8");
        var name = new Identifier("value");
        var ordinary = new Field(type, name, Field.NoArray, NoneExpr.Instance);
        var bitfield = new Field(type, name, Field.NoArray, new Literal(3));
        Assert.AreEqual(0, ordinary.BitSize);
        Assert.IsFalse(ordinary.HasBitfieldDeclarator);
        Assert.AreEqual(3, bitfield.BitSize);
        Assert.IsTrue(bitfield.HasBitfieldDeclarator);
        var negative = new Field(type, name, Field.NoArray, new Literal(-1));

        // The parsed node is inspected before compiler normalization, so invalid widths must still be rejected here.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => _ = negative.BitSize);
        Assert.AreEqual("Bitfield width cannot be negative.", failure.Message);
    }

    /// <summary>Debug descriptions preserve declaration kinds, names, backing types, members and every array dimension.</summary>
    [TestMethod]
    public void Descriptions_IdentifyEnumsFlagsAndArrayDimensions()
    {
        var values = System.Collections.Immutable.ImmutableArray.Create(new EnumValue(new Identifier("A"), new Literal(1)), new EnumValue(new Identifier("B"), new Literal(2)));
        var enumeration = new SyntaxEnum(new Identifier("choice"), values, new Identifier("uint8"));
        SyntaxEnum flags = SyntaxEnum.CreateUnevaluated(new Identifier("choice"), values, new Identifier("uint8"), isFlag: true);
        Assert.AreEqual("Enum [choice] [[uint8]] (EnumValue([A],Literal: 1), EnumValue([B],Literal: 2))", enumeration.ToString());
        Assert.AreEqual("Flag [choice] [[uint8]] (EnumValue([A],Literal: 1), EnumValue([B],Literal: 2))", flags.ToString());
        var field = new Field(new Identifier("uint8"), new Identifier("matrix"), new Expr[] { new Literal(2), new Literal(3), }, 0);
        Assert.AreEqual("[matrix] ([uint8]) [Literal: 2][Literal: 3]", field.ToString());
    }

    /// <summary>Implicit members after the backing type's maximum must fail just like explicit out-of-range values.</summary>
    /// <param name="kind">The enum-like declaration kind.</param>
    [TestMethod]
    [DataRow("enum")]
    [DataRow("flag")]
    public void ImplicitEnumMember_CannotExceedBackingDomain(string kind)
    {
        // The explicit 255 fits; only calculating the omitted next value crosses the domain boundary.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(kind + " choice : uint8 { Last = 255, Overflow };"));
        StringAssert.Contains(failure.Message, "Enum member 'Overflow' value 256 is outside its declared 8-bit domain.");
    }
}
