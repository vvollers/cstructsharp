namespace CStructSharp.Tests;

using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;
using SyntaxEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     Checks the syntax nodes layout compilation consumes: their facts before normalization, and their value equality
///     and hashing.
/// </summary>
[TestClass]
public class SyntaxMetadataBoundaryTests
{
    /// <summary>Array dimensions contribute to field hashing rather than collapsing distinct shapes into one bucket.</summary>
    /// <param name="dimension">The array dimension varied while the other dimension remains fixed.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public void FieldHash_UsesEveryArrayDimension(int dimension)
    {
        var hashes = new HashSet<int>();
        for (int index = 1; index <= 32; index++)
        {
            Expr[] dimensions = { new Literal(dimension == 0 ? index : 2), new Literal(dimension == 1 ? index : 3), };
            var field = new Field(new Identifier("uint8"), new Identifier("matrix"), dimensions, Field.Width(0));
            hashes.Add(field.GetHashCode());
        }

        Assert.IsGreaterThan(1, hashes.Count, "Distinct array shapes must not all collapse into one hash bucket.");
    }

    /// <summary>Already known and absent bit widths do not allocate an expression evaluation on every metadata read.</summary>
    /// <param name="resolved">Whether the width is already resolved rather than absent from the declaration.</param>
    [TestMethod]
    [DoNotParallelize]
    [DataRow(false)]
    [DataRow(true)]
    public void KnownBitWidth_ReadsWithoutRepeatedEvaluationAllocation(bool resolved)
    {
        var type = new Identifier("uint8");
        var name = new Identifier("value");
        Field field = resolved ? new Field(type, name, Field.NoArray, Field.Width(3)) : new Field(type, name, Field.NoArray, NoneExpr.Instance);
        int expected = resolved ? 3 : 0;
        for (int index = 0; index < 32; index++)
        {
            Assert.AreEqual(expected, field.BitSize);
        }

        // Warm up before measuring only repeated property reads, not construction or test assertions.
        long before = GC.GetAllocatedBytesForCurrentThread();
        int total = 0;
        for (int index = 0; index < 256; index++)
        {
            total += field.BitSize;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(expected * 256, total);
        Assert.IsLessThanOrEqualTo(1024L, allocated, "Known widths should reuse metadata without allocating an evaluator per read.");
    }

    /// <summary>Enum hash codes distribute declarations that differ in each identity component across hash buckets.</summary>
    /// <param name="component">The identity component varied while the others remain fixed.</param>
    [TestMethod]
    [DataRow("name")]
    [DataRow("type")]
    [DataRow("value")]
    public void EnumHash_UsesEachIdentityComponent(string component)
    {
        var hashes = new HashSet<int>();
        for (int index = 0; index < 32; index++)
        {
            var name = new Identifier(component == "name" ? "choice" + index : "choice");
            var type = new Identifier(component == "type" ? "storage" + index : "uint8");
            var values = System.Collections.Immutable.ImmutableArray.Create(
                new EnumValue(new Identifier("A"), new Literal(component == "value" ? index : 0)));
            hashes.Add(new SyntaxEnum(name, values, type).GetHashCode());
        }

        Assert.IsGreaterThan(1, hashes.Count, "Varying an identity component must not collapse every declaration into one hash bucket.");
    }

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

    /// <summary>A field combines the pointer spellings of its type and name unless an explicit depth overrides them, including zero.</summary>
    [TestMethod]
    public void PointerDepth_DerivesFromBothNamesAndHonorsZeroOverride()
    {
        var type = new Identifier("uint8*");
        var name = new Identifier("**value");
        var inferred = new Field(type, name, Field.NoArray, NoneExpr.Instance);
        var overridden = new Field(type, name, Field.NoArray, NoneExpr.Instance, pointerDepth: 0);
        Assert.AreEqual(3, inferred.PointerDepth);
        Assert.IsTrue(inferred.IsPointer);
        Assert.AreEqual(0, overridden.PointerDepth);
        Assert.IsFalse(overridden.IsPointer);
    }

    /// <summary>Parsed widths distinguish ordinary fields, explicit positive widths, invalid negative ones and unevaluated expressions.</summary>
    [TestMethod]
    public void ParsedWidth_IsStoredAndExplainsNegativeOrUnevaluatedValues()
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

        // A width written as an expression has no value until normalization evaluates it with the layout's constants.
        var unevaluated = new Field(type, name, Field.NoArray, new Identifier("WIDTH"));
        StringAssert.StartsWith(Assert.Throws<InvalidOperationException>(() => _ = unevaluated.BitSize).Message, "Bitfield width is not evaluated yet");
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
        var field = new Field(new Identifier("uint8"), new Identifier("matrix"), new Expr[] { new Literal(2), new Literal(3), }, Field.Width(0));
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

    /// <summary>
    ///     Equivalent declaration objects must compare equally and have matching hashes even when created separately.
    /// </summary>
    /// <remarks>
    ///     Changing bit width, pointer depth, typedef shape, enum sequence, or struct-versus-union identity must change
    ///     equality. This prevents metadata collections from treating layouts with different binary meanings as
    ///     interchangeable.
    /// </remarks>
    [TestMethod]
    public void LayoutElements_EqualityAndHashingFollowSemanticValue()
    {
        var name = new Identifier("field");
        var type = new Identifier("uint16");
        var fourBits = new Field(type, name, Field.NoArray, Field.Width(4));
        var eightBits = new Field(type, name, Field.NoArray, Field.Width(8));
        Assert.AreNotEqual(fourBits, eightBits);

        var fieldsA = System.Collections.Immutable.ImmutableList.Create(fourBits);
        var fieldsB = System.Collections.Immutable.ImmutableList.Create(new Field(type, name, Field.NoArray, Field.Width(4)));
        var structA = new Struct(new Identifier("shape"), fieldsA, false);
        var structB = new Struct(new Identifier("shape"), fieldsB, false);
        var union = new Struct(new Identifier("shape"), fieldsB, true);
        Assert.AreEqual(structA, structB);
        Assert.AreEqual(structA.GetHashCode(), structB.GetHashCode());
        Assert.AreNotEqual(structA, union);

        Assert.AreNotEqual(new Identifier("node"), new Identifier("node*"));

        var primitiveAlias = new Typedef(new Identifier("alias"), new Identifier("struct"));
        var structAlias = new Typedef(new Identifier("alias"), structA);
        Assert.IsFalse(primitiveAlias.Equals(structAlias));
        Assert.IsFalse(structAlias.Equals(primitiveAlias));

        var enumA = new CStructSharp.Syntax.Enum(
            new Identifier("kind"),
            System.Collections.Immutable.ImmutableArray.Create(new EnumValue(new Identifier("one"), new Literal(1))));
        var enumB = new CStructSharp.Syntax.Enum(
            new Identifier("kind"),
            System.Collections.Immutable.ImmutableArray.Create(new EnumValue(new Identifier("one"), new Literal(1))));
        Assert.AreEqual(enumA, enumB);
        Assert.AreEqual(enumA.GetHashCode(), enumB.GetHashCode());

        var structC = new Struct(
            new Identifier("shape"),
            System.Collections.Immutable.ImmutableList.Create(new Field(type, name, Field.NoArray, Field.Width(4))),
            false);
        Assert.IsTrue(structA.Equals((object)structA));
        Assert.AreEqual(structA, structB);
        Assert.AreEqual(structB, structA);
        Assert.AreEqual(structB, structC);
        Assert.AreEqual(structA, structC);
        Assert.IsTrue(new HashSet<CStructElement> { structA, }.Contains(structB));
        Assert.AreNotEqual(structA, new Struct(new Identifier("other"), fieldsB, false));
    }
}
