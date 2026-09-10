namespace CStructSharp.Tests;

using CStructSharp.Structure;

/// <summary>
///     Exercises <see cref="CharacterFieldTypes"/> directly, independent of a compiled <see cref="CStruct"/> layout.
///     Only reachable indirectly through the public API before this type was extracted from the God-Object
///     <c>CStruct</c> partial class.
/// </summary>
[TestClass]
public class CharacterFieldTypesTests
{
    /// <summary>A variable-length terminated-string type name is its own handler key.</summary>
    [TestMethod]
    public void GetStringPointerHandlerKey_VariableLengthType_ReturnsTheSameName()
    {
        Assert.AreEqual("cstring", CharacterFieldTypes.GetStringPointerHandlerKey(new Identifier("cstring")));
    }

    /// <summary>A big-endian wide character pointer selects the big-endian terminated string handler.</summary>
    [TestMethod]
    public void GetStringPointerHandlerKey_WcharBigEndian_ReturnsBigEndianStringKey()
    {
        Assert.AreEqual("string>", CharacterFieldTypes.GetStringPointerHandlerKey(new Identifier("wchar>")));
    }

    /// <summary>A little-endian wide character pointer selects the little-endian terminated string handler.</summary>
    [TestMethod]
    public void GetStringPointerHandlerKey_WcharLittleEndian_ReturnsLittleEndianStringKey()
    {
        Assert.AreEqual("string<", CharacterFieldTypes.GetStringPointerHandlerKey(new Identifier("wchar<")));
    }

    /// <summary>A neutral wide character pointer selects the neutral terminated string handler.</summary>
    [TestMethod]
    public void GetStringPointerHandlerKey_Wchar_ReturnsStringKey()
    {
        Assert.AreEqual("string", CharacterFieldTypes.GetStringPointerHandlerKey(new Identifier("wchar")));
    }

    /// <summary>A narrow character pointer selects the narrow terminated string handler.</summary>
    [TestMethod]
    public void GetStringPointerHandlerKey_Char_ReturnsCstringKey()
    {
        Assert.AreEqual("cstring", CharacterFieldTypes.GetStringPointerHandlerKey(new Identifier("char")));
    }

    /// <summary>A non-pointer narrow character field is a character array.</summary>
    [TestMethod]
    public void IsCharArrayField_NonPointerNarrowChar_ReturnsTrue()
    {
        var field = new Field(new Identifier("char"), new Identifier("label"), [new Literal(4),], 0);

        Assert.IsTrue(CharacterFieldTypes.IsCharArrayField(field));
    }

    /// <summary>A pointer to a character is a string pointer, not a character array.</summary>
    [TestMethod]
    public void IsCharArrayField_PointerToChar_ReturnsFalse()
    {
        var field = new Field(new Identifier("char"), new Identifier("label"), Field.NoArray, 0, pointerDepth: 1);

        Assert.IsFalse(CharacterFieldTypes.IsCharArrayField(field));
    }

    /// <summary>A non-pointer wide character field is a character array.</summary>
    [TestMethod]
    public void IsCharArrayField_NonPointerWideChar_ReturnsTrue()
    {
        var field = new Field(new Identifier("wchar"), new Identifier("label"), [new Literal(4),], 0);

        Assert.IsTrue(CharacterFieldTypes.IsCharArrayField(field));
    }

    /// <summary>A non-character fixed-width field is never a character array.</summary>
    [TestMethod]
    public void IsCharArrayField_NonCharacterType_ReturnsFalse()
    {
        var field = new Field(new Identifier("uint32"), new Identifier("count"), Field.NoArray, 0);

        Assert.IsFalse(CharacterFieldTypes.IsCharArrayField(field));
    }

    /// <summary>A narrow character, wide character, or variable-length type reads as a terminated string pointer target.</summary>
    [TestMethod]
    public void IsStringPointerType_CharacterAndVariableLengthTypes_ReturnsTrue()
    {
        Assert.IsTrue(CharacterFieldTypes.IsStringPointerType(new Identifier("char")));
        Assert.IsTrue(CharacterFieldTypes.IsStringPointerType(new Identifier("wchar")));
        Assert.IsTrue(CharacterFieldTypes.IsStringPointerType(new Identifier("cstring")));
    }

    /// <summary>A fixed-width non-character primitive is not a string pointer target.</summary>
    [TestMethod]
    public void IsStringPointerType_FixedWidthPrimitive_ReturnsFalse()
    {
        Assert.IsFalse(CharacterFieldTypes.IsStringPointerType(new Identifier("uint32")));
    }

    /// <summary>Every neutral and explicit-endian wide character spelling is recognized.</summary>
    [TestMethod]
    public void IsWideCharacterType_AllWideSpellings_ReturnsTrue()
    {
        Assert.IsTrue(CharacterFieldTypes.IsWideCharacterType(new Identifier("wchar")));
        Assert.IsTrue(CharacterFieldTypes.IsWideCharacterType(new Identifier("wchar>")));
        Assert.IsTrue(CharacterFieldTypes.IsWideCharacterType(new Identifier("wchar<")));
    }

    /// <summary>A narrow character is not a wide character.</summary>
    [TestMethod]
    public void IsWideCharacterType_NarrowChar_ReturnsFalse()
    {
        Assert.IsFalse(CharacterFieldTypes.IsWideCharacterType(new Identifier("char")));
    }
}
