namespace CStructSharpTests;

using CStructSharp.Structure;

/// <summary>
///     Verifies value equality and hash-code consistency for the immutable elements that make up a compiled layout.
/// </summary>
[TestClass]
public class LayoutModelEqualityTests
{
    /// <summary>
    ///     Verifies that all semantic model properties participate in symmetric equality and produce matching hashes,
    ///     including bit widths, union identity, pointer depth, typedef shape, and enum value sequences.
    /// </summary>
    [TestMethod]
    public void LayoutElements_EqualityAndHashingFollowSemanticValue()
    {
        var name = new Identifier("field");
        var type = new Identifier("uint16");
        var fourBits = new Field(type, name, Field.NoArray, 4);
        var eightBits = new Field(type, name, Field.NoArray, 8);
        Assert.AreNotEqual(fourBits, eightBits);

        var fieldsA = System.Collections.Immutable.ImmutableList.Create(fourBits);
        var fieldsB = System.Collections.Immutable.ImmutableList.Create(new Field(type, name, Field.NoArray, 4));
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

        var enumA = new CStructSharp.Structure.Enum(
            new Identifier("kind"),
            System.Collections.Immutable.ImmutableArray.Create(new EnumValue(new Identifier("one"), new Literal(1))));
        var enumB = new CStructSharp.Structure.Enum(
            new Identifier("kind"),
            System.Collections.Immutable.ImmutableArray.Create(new EnumValue(new Identifier("one"), new Literal(1))));
        Assert.AreEqual(enumA, enumB);
        Assert.AreEqual(enumA.GetHashCode(), enumB.GetHashCode());

        var structC = new Struct(
            new Identifier("shape"),
            System.Collections.Immutable.ImmutableList.Create(new Field(type, name, Field.NoArray, 4)),
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
