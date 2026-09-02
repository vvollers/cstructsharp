namespace CStructSharp.Tests;

using CStructSharp.Structure;
using Pidgin;
using Enum = CStructSharp.Structure.Enum;

/// <summary>Groups tests for enums so changes to this behavior are caught.</summary>
[TestClass]
public class Enums
{
    /// <summary>
    ///     C enum declarations assign integer values to names, with omitted values auto-incrementing from the previous member.
    ///     This test verifies explicit assignment and implicit progression in one definition.
    /// </summary>
    [TestMethod]
    public void TestEnums()
    {
        var enm = (Enum)CStructDefinitionParser.Enum.ParseOrThrow("enum zing { Red = 5 , Green, Blue  };");

        Assert.AreEqual("zing", enm.Name.Name);
        Assert.HasCount(3, enm.Values);
        Assert.AreSame(Identifier.BYTE, enm.Type);

        Assert.AreEqual("Red", enm.Values[0].Name.Name);
        Assert.AreEqual(5, enm.Values[0].Value.Calc());
        Assert.AreEqual("Green", enm.Values[1].Name.Name);
        Assert.AreEqual(6, enm.Values[1].Value.Calc());
        Assert.AreEqual("Blue", enm.Values[2].Name.Name);
        Assert.AreEqual(7, enm.Values[2].Value.Calc());
    }

    /// <summary>
    ///     Extends enum parsing with an explicit underlying integer type and expression-based member values. This mirrors
    ///     C-style schemas that pin storage width for binary compatibility.
    /// </summary>
    [TestMethod]
    public void TestEnumsWithType()
    {
        var enm = (Enum)CStructDefinitionParser.Enum.ParseOrThrow(
                                                                  "enum zang : uint8 {Dark,Grey=0xFFF,Light=0b1001_0110+5};");

        Assert.AreEqual("zang", enm.Name.Name);
        Assert.HasCount(3, enm.Values);
        Assert.AreEqual("uint8", enm.Type.Name);
        Assert.AreEqual("Dark", enm.Values[0].Name.Name);
        Assert.AreEqual(0, enm.Values[0].Value.Calc());
        Assert.AreEqual("Grey", enm.Values[1].Name.Name);
        Assert.AreEqual(0xFFF, enm.Values[1].Value.Calc());
        Assert.AreEqual("Light", enm.Values[2].Name.Name);
        Assert.AreEqual(0b1001_0110 + 5, enm.Values[2].Value.Calc());
    }

    /// <summary>
    ///     Parses a single enum member token with optional assignment expression. It validates whitespace tolerance and
    ///     supports both fixed-value and auto-numbered member forms.
    /// </summary>
    [TestMethod]
    public void TestEnumValue()
    {
        EnumValue? enumValue1 = CStructDefinitionParser.EnumValue.ParseOrThrow("Red=2");
        Assert.AreEqual(2, enumValue1.Value.Calc());
        Assert.AreEqual("Red", enumValue1.Name.Name);

        EnumValue? enumValue2 = CStructDefinitionParser.EnumValue.ParseOrThrow("  Blue =4 ");
        Assert.AreEqual(4, enumValue2.Value.Calc());
        Assert.AreEqual("Blue", enumValue2.Name.Name);

        EnumValue? enumValue3 = CStructDefinitionParser.EnumValue.ParseOrThrow("     Green=   0xFF     ");
        Assert.AreEqual(0xFF, enumValue3.Value.Calc());
        Assert.AreEqual("Green", enumValue3.Name.Name);

        Assert.AreEqual("Yellow", CStructDefinitionParser.EnumValue.ParseOrThrow("Yellow").Name.Name);
        Assert.AreEqual("Purple", CStructDefinitionParser.EnumValue.ParseOrThrow("     Purple    ").Name.Name);
    }

    /// <summary>
    ///     Parses comma-separated enum member lists before wrapping them in a full enum declaration. It keeps placeholder
    ///     entries for omitted values so sequential numbering can be resolved later.
    /// </summary>
    [TestMethod]
    public void TestEnumValues()
    {
        List<EnumValue> enums = CStructDefinitionParser.EnumValues.ParseOrThrow(" Red = 5, Green, Blue=9 ").ToList();
        Assert.HasCount(3, enums);
        Assert.AreEqual(5, enums[0].Value.Calc());
        Assert.AreSame(NoneExpr.Instance, enums[1].Value);
        Assert.AreEqual(9, enums[2].Value.Calc());
        Assert.AreEqual("Red", enums[0].Name.Name);
        Assert.AreEqual("Green", enums[1].Name.Name);
        Assert.AreEqual("Blue", enums[2].Name.Name);
    }

    /// <summary>
    ///     Validates enum member lists enclosed in braces, matching full declaration syntax exactly. It confirms the same
    ///     value rules as TestEnumValues under real enum grammar boundaries.
    /// </summary>
    [TestMethod]
    public void TestEnumValuesInBrackets()
    {
        List<EnumValue> enums = CStructDefinitionParser.EnumValuesInBrackets.
                                                        ParseOrThrow("{ Silver = 5, Gold, Diamond=0  }").
                                                        ToList();

        Assert.HasCount(3, enums);
        Assert.AreEqual(5, enums[0].Value.Calc());
        Assert.AreSame(NoneExpr.Instance, enums[1].Value);
        Assert.AreEqual(0, enums[2].Value.Calc());
        Assert.AreEqual("Silver", enums[0].Name.Name);
        Assert.AreEqual("Gold", enums[1].Name.Name);
        Assert.AreEqual("Diamond", enums[2].Name.Name);

        List<EnumValue> enums2 = CStructDefinitionParser.EnumValuesInBrackets.
                                                         ParseOrThrow("{SilverA=5,GoldB,DiamondC=9}").
                                                         ToList();

        Assert.HasCount(3, enums2);
        Assert.AreEqual(5, enums2[0].Value.Calc());
        Assert.AreSame(NoneExpr.Instance, enums2[1].Value);
        Assert.AreEqual(9, enums2[2].Value.Calc());
        Assert.AreEqual("SilverA", enums2[0].Name.Name);
        Assert.AreEqual("GoldB", enums2[1].Name.Name);
        Assert.AreEqual("DiamondC", enums2[2].Name.Name);

        List<EnumValue> enums3 = CStructDefinitionParser.EnumValuesInBrackets.
                                                         ParseOrThrow("{SilverD = 5, GoldE=    0xFF, DiamondF}").
                                                         ToList();

        Assert.HasCount(3, enums3);
        Assert.AreEqual(5, enums3[0].Value.Calc());
        Assert.AreEqual(0xFF, enums3[1].Value.Calc());
        Assert.AreSame(NoneExpr.Instance, enums3[2].Value);
        Assert.AreEqual("SilverD", enums3[0].Name.Name);
        Assert.AreEqual("GoldE", enums3[1].Name.Name);
        Assert.AreEqual("DiamondF", enums3[2].Name.Name);
    }
}
