namespace CStructSharp.Tests;

using System.Dynamic;
using System.Numerics;

/// <summary>
///     Exercises <see cref="EnumFieldValueParser"/> directly against a real compiled enum descriptor. Only reachable
///     indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c> partial
///     class.
/// </summary>
[TestClass]
public class EnumFieldValueParserTests
{
    private static (CompiledEnumType Compiled, CStructSharp.Structure.Enum Declaration) CompileMode()
    {
        var cstruct = new CStruct("enum mode : uint8 { One=1, Two=2 };", pointerSize: 1);
        CompiledLayoutModel model = cstruct.CompiledModel;
        var compiled = (CompiledEnumType)model.Symbols["mode"].Symbol.Definition!;
        var declaration = (CStructSharp.Structure.Enum)model.Declarations["mode"];
        return (compiled, declaration);
    }

    /// <summary>A member name string resolves to that member's exact integer value.</summary>
    [TestMethod]
    public void GetEnumValue_MemberNameString_ResolvesToMemberValue()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, enm, "Two", PocoBindingMode.PublicReadable);

        Assert.AreEqual(new BigInteger(2), result);
    }

    /// <summary>A decimal string that is not a member name is parsed as an invariant integer.</summary>
    [TestMethod]
    public void GetEnumValue_InvariantDecimalString_ParsesAsInteger()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, enm, "5", PocoBindingMode.PublicReadable);

        Assert.AreEqual(new BigInteger(5), result);
    }

    /// <summary>A string that is neither a member name nor a valid integer cannot be converted.</summary>
    [TestMethod]
    public void GetEnumValue_UnparsableString_Throws()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();

        Assert.Throws<CStructWriteException>(
            () => EnumFieldValueParser.GetEnumValue(compiled, enm, "NotAMember", PocoBindingMode.PublicReadable));
    }

    /// <summary>A direct integral CLR value converts exactly.</summary>
    [TestMethod]
    public void GetEnumValue_IntegralValue_ConvertsDirectly()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, enm, 1, PocoBindingMode.PublicReadable);

        Assert.AreEqual(BigInteger.One, result);
    }

    /// <summary>A value outside the compiled enum's storage domain is rejected.</summary>
    [TestMethod]
    public void GetEnumValue_OutOfRangeValue_Throws()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();

        Assert.Throws<CStructWriteException>(
            () => EnumFieldValueParser.GetEnumValue(compiled, enm, 1000, PocoBindingMode.PublicReadable));
    }

    private static EnumValueResult CreateParsedValue(CompiledEnumType compiled, string enumName, string? memberName, int value)
    {
        var exact = new BigInteger(value);
        return new EnumValueResult(
            enumName,
            memberName,
            exact,
            compiled.Integer.ToRawBits(exact),
            compiled.Integer.StorageType,
            compiled.Integer.BitWidth,
            compiled.Integer.IsSigned);
    }

    /// <summary>A self-describing parsed enum value that already matches this domain round-trips its exact value.</summary>
    [TestMethod]
    public void GetEnumValue_MatchingEnumValueResult_ReturnsItsValue()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();
        EnumValueResult parsed = CreateParsedValue(compiled, enm.Name.Name, "Two", 2);

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, enm, parsed, PocoBindingMode.PublicReadable);

        Assert.AreEqual(new BigInteger(2), result);
    }

    /// <summary>A self-describing parsed value naming a different enum cannot be written into this domain.</summary>
    [TestMethod]
    public void GetEnumValue_EnumValueResultForDifferentEnum_Throws()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();
        EnumValueResult parsed = CreateParsedValue(compiled, "other_enum", "Two", 2);

        Assert.Throws<CStructWriteException>(
            () => EnumFieldValueParser.GetEnumValue(compiled, enm, parsed, PocoBindingMode.PublicReadable));
    }

    /// <summary>A POCO-shaped object supplying only Name resolves to that member's value.</summary>
    [TestMethod]
    public void GetEnumValue_ObjectShapeWithName_ResolvesToMemberValue()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();
        dynamic shape = new ExpandoObject();
        shape.Name = "One";

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, enm, shape, PocoBindingMode.PublicReadable);

        Assert.AreEqual(BigInteger.One, result);
    }

    /// <summary>A POCO-shaped object supplying only Value resolves directly to that numeric value.</summary>
    [TestMethod]
    public void GetEnumValue_ObjectShapeWithValue_ResolvesToNumericValue()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();
        dynamic shape = new ExpandoObject();
        shape.Value = 2;

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, enm, shape, PocoBindingMode.PublicReadable);

        Assert.AreEqual(new BigInteger(2), result);
    }

    /// <summary>A POCO-shaped object with contradictory Name and Value metadata cannot be resolved.</summary>
    [TestMethod]
    public void GetEnumValue_ObjectShapeWithContradictoryMetadata_Throws()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();
        dynamic shape = new ExpandoObject();
        shape.Name = "One";
        shape.Value = 2;

        Assert.Throws<CStructWriteException>(
            () => EnumFieldValueParser.GetEnumValue(compiled, enm, shape, PocoBindingMode.PublicReadable));
    }

    /// <summary>A POCO-shaped object supplying neither Name nor Value has no identifying data.</summary>
    [TestMethod]
    public void GetEnumValue_ObjectShapeWithNeitherNameNorValue_Throws()
    {
        (CompiledEnumType compiled, CStructSharp.Structure.Enum enm) = CompileMode();
        dynamic shape = new ExpandoObject();

        Assert.Throws<CStructWriteException>(
            () => EnumFieldValueParser.GetEnumValue(compiled, enm, shape, PocoBindingMode.PublicReadable));
    }
}
