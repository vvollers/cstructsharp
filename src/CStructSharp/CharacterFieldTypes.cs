namespace CStructSharp;

using CStructSharp.Structure;

/// <summary>Classifies field type names that denote a character, wide character, or terminated string.</summary>
internal static class CharacterFieldTypes
{
    public static readonly Identifier CharType = new("char");
    public static readonly Identifier Utf8Type = new("utf8");
    public static readonly Identifier CstringType = new("cstring");
    public static readonly Identifier StringType = new("string");
    public static readonly Identifier WcharBigEndianType = new("wchar>");
    public static readonly Identifier WcharLittleEndianType = new("wchar<");
    public static readonly Identifier WcharType = new("wchar");

    /// <summary>Chooses the zero-terminated string reader that matches a character pointer type.</summary>
    public static string GetStringPointerHandlerKey(Identifier type)
    {
        if (PrimitiveCodecs.IsVariableLengthType(type.Name))
        {
            return type.Name;
        }

        if (type.Equals(WcharBigEndianType))
        {
            return "string>";
        }

        if (type.Equals(WcharLittleEndianType))
        {
            return "string<";
        }

        return type.Equals(WcharType) ? StringType.Name : CstringType.Name;
    }

    /// <summary>Returns whether a non-pointer field is a fixed array of narrow or wide characters.</summary>
    public static bool IsCharArrayField(Field field)
    {
        return !field.IsPointer && (field.Type.Equals(CharType) || IsWideCharacterType(field.Type));
    }

    /// <summary>Returns whether a pointer target should be read as a terminated string.</summary>
    public static bool IsStringPointerType(Identifier type)
    {
        return type.Equals(CharType) || IsWideCharacterType(type) || PrimitiveCodecs.IsVariableLengthType(type.Name);
    }

    /// <summary>Returns whether a type is a neutral or explicit-endian 16-bit character.</summary>
    public static bool IsWideCharacterType(Identifier type)
    {
        return type.Equals(WcharType) || type.Equals(WcharBigEndianType) || type.Equals(WcharLittleEndianType);
    }
}
