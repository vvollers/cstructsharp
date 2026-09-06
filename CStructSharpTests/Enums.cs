namespace CStructSharp.Tests;

using CStructSharp.Structure;
using Pidgin;
using Enum = CStructSharp.Structure.Enum;

/// <summary>Groups tests for enums so changes to this behavior are caught.</summary>
[TestClass]
public class Enums
{
    /// <summary>
    ///     Red starts at 5, so Green and Blue must become 6 and 7.
    /// </summary>
    /// <remarks>
    ///     The declaration parser also records byte as this library's default enum storage type. That default is a
    ///     library rule, not a claim about the size of enums in every C compiler.
    /// </remarks>
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
    ///     The parser records uint8 as the storage type, starts Dark at 0, and evaluates the explicit expressions for
    ///     Grey and Light.
    /// </summary>
    /// <remarks>
    ///     Grey is 4095, which cannot fit in uint8. This test only checks declaration parsing; accepting the syntax
    ///     does not mean the completed layout will accept that out-of-range enum value.
    /// </remarks>
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
    ///     Red=2, Blue=4, and Green=0xFF must retain their names and evaluate to 2, 4, and 255 despite surrounding
    ///     spaces.
    /// </summary>
    /// <remarks>
    ///     Yellow and Purple have no explicit assignment. Their final numbers depend on their position in a complete
    ///     enum, which is outside this individual-member test.
    /// </remarks>
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
    ///     The three entries must stay in declaration order.
    /// </summary>
    /// <remarks>
    ///     Red keeps 5 and Blue keeps 9, while Green retains a marker meaning 'no number supplied yet'. Numbering Green
    ///     belongs to the full enum parser; the list parser must not guess it early.
    /// </remarks>
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
    ///     Braces surround the enum members, while commas separate them.
    /// </summary>
    /// <remarks>
    ///     Variations in spaces must leave the same three names and explicit values intact. Missing assignments remain
    ///     marked as missing, so a later step can apply enum numbering rules.
    /// </remarks>
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
