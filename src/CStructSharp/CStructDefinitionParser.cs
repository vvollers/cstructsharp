namespace CStructSharp;

using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>
///     Entry points for parsing the supported C-like layout language into the small model classes used by
///     <see cref="CStruct"/>. <see cref="ParseLayout(string, IReadOnlySet{string})"/> parses a complete definition; the other members parse one
///     production each for tests and tooling. All of them delegate to <see cref="LayoutParser"/>.
/// </summary>
internal static class CStructDefinitionParser
{
    /// <summary>Parses a complete layout definition into its top-level declarations.</summary>
    public static IReadOnlyList<CStructElement> ParseLayout(string source, IReadOnlySet<string>? definedNames = null)
    {
        return LayoutParser.ParseLayout(source, definedNames);
    }

    public static IReadOnlyList<CStructElement> ParseLayout(string source, IReadOnlySet<string>? definedNames, string? defaultEnumStorage, out bool usesQualifiedIdentifiers)
    {
        return LayoutParser.ParseLayout(source, definedNames, defaultEnumStorage, out usesQualifiedIdentifiers);
    }

    /// <summary>Parses exactly one top-level declaration.</summary>
    public static CStructElement ParseElement(string source)
    {
        return LayoutParser.ParseElement(source);
    }

    /// <summary>Parses one complete expression.</summary>
    public static Expr ParseExpression(string source)
    {
        return LayoutParser.ParseExpression(source);
    }

    /// <summary>Parses the integer literal at the start of the text; see <see cref="LayoutParser.ParseLiteral"/>.</summary>
    public static Expr ParseLiteral(string source, int radix = 0)
    {
        return LayoutParser.ParseLiteral(source, radix);
    }

    /// <summary>Reads the digit run at the start of the text in the requested radix, without separators.</summary>
    public static string ParseDigits(string source, int radix)
    {
        return LayoutParser.ParseDigits(source, radix);
    }

    /// <summary>Returns whether a character is a digit of the requested radix or the <c>_</c> separator.</summary>
    public static bool IsDigitOrSeparator(char character, int radix)
    {
        return LayoutParser.IsDigitOrSeparator(character, radix);
    }

    /// <summary>Parses one enum member.</summary>
    public static EnumValue ParseEnumValue(string source)
    {
        return LayoutParser.ParseEnumValue(source);
    }

    /// <summary>Parses a comma-separated enum member list without braces.</summary>
    public static IReadOnlyList<EnumValue> ParseEnumValues(string source)
    {
        return LayoutParser.ParseEnumValues(source);
    }

    /// <summary>Parses a braced enum member list.</summary>
    public static IReadOnlyList<EnumValue> ParseEnumValuesInBrackets(string source)
    {
        return LayoutParser.ParseEnumValuesInBrackets(source);
    }

    /// <summary>Parses one field declaration with one or more declarators.</summary>
    public static IReadOnlyList<Field> ParseFieldGroup(string source)
    {
        return LayoutParser.ParseFieldGroup(source);
    }
}
