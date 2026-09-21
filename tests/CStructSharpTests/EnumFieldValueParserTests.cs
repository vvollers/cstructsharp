namespace CStructSharp.Tests;

using System.Dynamic;
using System.Numerics;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Exercises <see cref="EnumFieldValueParser"/> directly against a real compiled enum descriptor. Only reachable
///     indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c> partial
///     class.
/// </summary>
[TestClass]
public class EnumFieldValueParserTests
{
    private enum ClrMode : byte
    {
        First = 1,
        Second = 2,
    }

    /// <summary>An enum payload must identify its declaration so it cannot be silently reused for an unnamed domain.</summary>
    [TestMethod]
    public void EnumValueResult_RequiresANonWhitespaceDeclarationName()
    {
        foreach (string? name in new string?[] { null, string.Empty, " \t", })
        {
            // Missing metadata is rejected at construction rather than deferred until serialization.
            ArgumentException failure = Assert.Throws<ArgumentException>(
                () => new EnumValueResult(name!, null, BigInteger.Zero, 0, "uint8", 8, false));
            Assert.AreEqual("enumName", failure.ParamName);
            StringAssert.Contains(failure.Message, "An enum name is required");
        }
    }

    /// <summary>Compiles the two-member byte enum shared by these input-shape checks.</summary>
    /// <returns>The executable enum descriptor and its source declaration.</returns>
    private static (CompiledEnumType Compiled, CStructSharp.Syntax.Enum Declaration) CompileMode()
    {
        var cstruct = new CStruct("enum mode : uint8 { One=1, Two=2 };", pointerSize: 1);
        CompiledLayoutModel model = cstruct.CompiledModel;
        var compiled = (CompiledEnumType)model.Symbols["mode"].Symbol.Definition!;
        var declaration = (CStructSharp.Syntax.Enum)model.Declarations["mode"];
        return (compiled, declaration);
    }

    /// <summary>A member name string resolves to that member's exact integer value.</summary>
    [TestMethod]
    public void GetEnumValue_MemberNameString_ResolvesToMemberValue()
    {
        (CompiledEnumType compiled, _) = CompileMode();

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, "Two");

        Assert.AreEqual(new BigInteger(2), result);
    }

    /// <summary>A CLR enum value (a mapped class's property) is matched by its underlying integer, whatever its C# name.</summary>
    [TestMethod]
    public void GetEnumValue_ClrEnum_ResolvesByUnderlyingValue()
    {
        (CompiledEnumType compiled, _) = CompileMode();

        Assert.AreEqual(new BigInteger(2), EnumFieldValueParser.GetEnumValue(compiled, ClrMode.Second));
        Assert.AreEqual(new BigInteger(5), EnumFieldValueParser.GetEnumValue(compiled, (ClrMode)5));
    }

    /// <summary>A decimal string that is not a member name is parsed as an invariant integer.</summary>
    [TestMethod]
    public void GetEnumValue_InvariantDecimalString_ParsesAsInteger()
    {
        (CompiledEnumType compiled, _) = CompileMode();

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, "5");

        Assert.AreEqual(new BigInteger(5), result);
    }

    /// <summary>A string that is neither a member name nor a valid integer cannot be converted.</summary>
    [TestMethod]
    public void GetEnumValue_UnparsableString_Throws()
    {
        (CompiledEnumType compiled, _) = CompileMode();

        Assert.Throws<CStructWriteException>(
            () => EnumFieldValueParser.GetEnumValue(compiled, "NotAMember"));
    }

    /// <summary>A direct integral CLR value converts exactly.</summary>
    [TestMethod]
    public void GetEnumValue_IntegralValue_ConvertsDirectly()
    {
        (CompiledEnumType compiled, _) = CompileMode();

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, 1);

        Assert.AreEqual(BigInteger.One, result);
    }

    /// <summary>A value outside the compiled enum's storage domain is rejected.</summary>
    [TestMethod]
    public void GetEnumValue_OutOfRangeValue_Throws()
    {
        (CompiledEnumType compiled, _) = CompileMode();

        Assert.Throws<CStructWriteException>(
            () => EnumFieldValueParser.GetEnumValue(compiled, 1000));
    }

    /// <summary>Builds self-describing metadata in the compiled enum's integer domain.</summary>
    /// <param name="compiled">The target integer-domain descriptor.</param>
    /// <param name="enumName">The supplied enum type name.</param>
    /// <param name="memberName">The optional supplied member name.</param>
    /// <param name="value">The exact integer to encode in the metadata.</param>
    /// <returns>The caller-supplied enum value to validate.</returns>
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
        (CompiledEnumType compiled, _) = CompileMode();
        EnumValueResult parsed = CreateParsedValue(compiled, compiled.Name, "Two", 2);

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, parsed);

        Assert.AreEqual(new BigInteger(2), result);
    }

    /// <summary>A self-describing parsed value naming a different enum cannot be written into this domain.</summary>
    [TestMethod]
    public void GetEnumValue_EnumValueResultForDifferentEnum_Throws()
    {
        (CompiledEnumType compiled, _) = CompileMode();
        EnumValueResult parsed = CreateParsedValue(compiled, "other_enum", "Two", 2);

        Assert.Throws<CStructWriteException>(
            () => EnumFieldValueParser.GetEnumValue(compiled, parsed));
    }

    /// <summary>A POCO-shaped object supplying only Name resolves to that member's value.</summary>
    [TestMethod]
    public void GetEnumValue_ObjectShapeWithName_ResolvesToMemberValue()
    {
        (CompiledEnumType compiled, _) = CompileMode();
        dynamic shape = new ExpandoObject();
        shape.Name = "One";

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, shape);

        Assert.AreEqual(BigInteger.One, result);
    }

    /// <summary>A POCO-shaped object supplying only Value resolves directly to that numeric value.</summary>
    [TestMethod]
    public void GetEnumValue_ObjectShapeWithValue_ResolvesToNumericValue()
    {
        (CompiledEnumType compiled, _) = CompileMode();
        dynamic shape = new ExpandoObject();
        shape.Value = 2;

        BigInteger result = EnumFieldValueParser.GetEnumValue(compiled, shape);

        Assert.AreEqual(new BigInteger(2), result);
    }

    /// <summary>A POCO-shaped object with contradictory Name and Value metadata cannot be resolved.</summary>
    [TestMethod]
    public void GetEnumValue_ObjectShapeWithContradictoryMetadata_Throws()
    {
        (CompiledEnumType compiled, _) = CompileMode();
        dynamic shape = new ExpandoObject();
        shape.Name = "One";
        shape.Value = 2;

        Assert.Throws<CStructWriteException>(
            () => EnumFieldValueParser.GetEnumValue(compiled, shape));
    }

    /// <summary>A POCO-shaped object supplying neither Name nor Value has no identifying data.</summary>
    [TestMethod]
    public void GetEnumValue_ObjectShapeWithNeitherNameNorValue_Throws()
    {
        (CompiledEnumType compiled, _) = CompileMode();
        dynamic shape = new ExpandoObject();

        Assert.Throws<CStructWriteException>(
            () => EnumFieldValueParser.GetEnumValue(compiled, shape));
    }

    /// <summary>Null optional metadata is absent, while the remaining name or integer still identifies the value.</summary>
    [TestMethod]
    public void GetEnumValue_NullOptionalMetadataPreservesTheSuppliedValue()
    {
        (CompiledEnumType compiled, _) = CompileMode();
        var numeric = new Dictionary<string, object?> { ["Enum"] = null, ["Name"] = null, ["Value"] = 2, };
        var named = new Dictionary<string, object?> { ["Name"] = "Two", ["Value"] = null, };
        Assert.AreEqual(new BigInteger(2), EnumFieldValueParser.GetEnumValue(compiled, numeric));
        Assert.AreEqual(new BigInteger(2), EnumFieldValueParser.GetEnumValue(compiled, named));
    }

    /// <summary>Flag union is idempotent, skips empty entries, and does not turn an ordinary enum into a flag set.</summary>
    [TestMethod]
    public void GetEnumValue_FlagNamesCombineWithoutTogglingRepeatedBits()
    {
        var layout = new CStruct("flag access : uint8 { A=1, B=2 };");
        var flags = (CompiledEnumType)layout.CompiledModel.Symbols["access"].Symbol.Definition!;
        Assert.AreEqual(new BigInteger(3), EnumFieldValueParser.GetEnumValue(flags, "A|A||B|"));
        Assert.AreEqual(new BigInteger(3), EnumFieldValueParser.GetEnumValue(flags, new[] { "A", string.Empty, "A", "B", }));
        Assert.AreEqual(new BigInteger(3), EnumFieldValueParser.GetEnumValue(flags, "3"));
        CStructWriteException missing = Assert.Throws<CStructWriteException>(() => EnumFieldValueParser.GetEnumValue(flags, "A|Unknown"));
        StringAssert.Contains(missing.Message, "Flag 'access' has no member named 'Unknown'");
        (CompiledEnumType ordinary, _) = CompileMode();
        CStructWriteException combined = Assert.Throws<CStructWriteException>(() => EnumFieldValueParser.GetEnumValue(ordinary, "One|Two"));
        StringAssert.Contains(combined.Message, "neither a member of enum 'mode' nor an invariant decimal integer");
    }

    /// <summary>Invalid enum object metadata explains the distinct missing, contradictory, unknown and non-integral cases.</summary>
    [TestMethod]
    public void GetEnumValue_InvalidMetadataExplainsTheRejectedInput()
    {
        (CompiledEnumType compiled, _) = CompileMode();
        (Dictionary<string, object?> Value, string Reason)[] cases =
        [
            (new() { ["Name"] = "Missing", }, "has no member named 'Missing'"),
            (new(), "input must supply Name or Value"),
            (new() { ["Name"] = "One", ["Value"] = 2, }, "Name and Value identify different members"),
            (new() { ["Value"] = 1.5, }, "Enum Value must be an integral CLR value"),
            (new() { ["Enum"] = "other", ["Value"] = 1, }, "Enum value 'other' cannot be written as 'mode'"),
        ];
        foreach ((Dictionary<string, object?> value, string reason) in cases)
        {
            CStructWriteException failure = Assert.Throws<CStructWriteException>(() => EnumFieldValueParser.GetEnumValue(compiled, value));
            StringAssert.Contains(failure.Message, reason);
            Assert.IsNotNull(failure.InnerException);
        }
    }
}
