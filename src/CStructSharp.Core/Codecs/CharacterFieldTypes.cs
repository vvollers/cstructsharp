namespace CStructSharp.Codecs;

using CStructSharp.Syntax;

/// <summary>Classifies field type names that denote a character, wide character, or terminated string.</summary>
internal static class CharacterFieldTypes
{
    /// <summary>The narrow one-byte character type, <c>char</c>.</summary>
    public static readonly Identifier CharType = new("char");

    /// <summary>The one-byte UTF-8 code unit type, <c>utf8</c>.</summary>
    public static readonly Identifier Utf8Type = new("utf8");

    /// <summary>The zero-terminated ASCII string codec, <c>cstring</c>.</summary>
    public static readonly Identifier CstringType = new("cstring");

    /// <summary>The zero-terminated UTF-16 string codec in the layout byte order, <c>string</c>.</summary>
    public static readonly Identifier StringType = new("string");

    /// <summary>The big-endian 16-bit character type, <c>wchar&gt;</c>.</summary>
    public static readonly Identifier WcharBigEndianType = new("wchar>");

    /// <summary>The little-endian 16-bit character type, <c>wchar&lt;</c>.</summary>
    public static readonly Identifier WcharLittleEndianType = new("wchar<");

    /// <summary>The 16-bit character type in the layout byte order, <c>wchar</c>.</summary>
    public static readonly Identifier WcharType = new("wchar");

    /// <summary>Chooses the zero-terminated string reader that matches a character pointer type.</summary>
    /// <param name="type">The type as written, without pointer stars of its own.</param>
    /// <returns>The terminated codec's name.</returns>
    public static string GetStringPointerHandlerKey(Identifier type)
        => type.PointerDepth == 0 || PrimitiveCodecs.IsVariableLengthType(type.Name) ? GetStringPointerHandlerKey(type.Name) : CstringType.Name;

    /// <summary>Chooses the zero-terminated string reader that matches a character type name.</summary>
    /// <param name="typeName">The resolved type name.</param>
    /// <returns>The terminated codec's name.</returns>
    public static string GetStringPointerHandlerKey(string typeName)
    {
        if (PrimitiveCodecs.IsVariableLengthType(typeName))
        {
            return typeName;
        }

        return typeName == WcharBigEndianType.Name ? "string>"
               : typeName == WcharLittleEndianType.Name ? "string<"
               : typeName == WcharType.Name ? StringType.Name
               : CstringType.Name;
    }

    /// <summary>Returns whether a non-pointer field is a fixed array of narrow or wide characters.</summary>
    /// <param name="field">The parsed field declaration.</param>
    /// <returns><see langword="true"/> for a non-pointer <c>char</c> or <c>wchar</c> field.</returns>
    public static bool IsCharArrayField(Field field)
    {
        return !field.IsPointer && (field.Type.Equals(CharType) || IsWideCharacterType(field.Type));
    }

    /// <summary>Returns whether a pointer target should be read as a terminated string.</summary>
    /// <param name="type">The pointer's target type as written.</param>
    /// <returns><see langword="true"/> for a character type or a terminated string codec.</returns>
    public static bool IsStringPointerType(Identifier type)
    {
        return type.Equals(CharType) || IsWideCharacterType(type) || PrimitiveCodecs.IsVariableLengthType(type.Name);
    }

    /// <summary>Returns whether a type is a neutral or explicit-endian 16-bit character.</summary>
    /// <param name="type">The type to classify.</param>
    /// <returns><see langword="true"/> for <c>wchar</c>, <c>wchar&gt;</c>, or <c>wchar&lt;</c>.</returns>
    public static bool IsWideCharacterType(Identifier type)
    {
        return type.Equals(WcharType) || type.Equals(WcharBigEndianType) || type.Equals(WcharLittleEndianType);
    }
}
