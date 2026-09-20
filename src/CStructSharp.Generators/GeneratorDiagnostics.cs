namespace CStructSharp.Generators;

using Microsoft.CodeAnalysis;

/// <summary>The diagnostics the generator and analyzer report; ids, severities, and titles are listed in AnalyzerReleases.Unshipped.md.</summary>
internal static class GeneratorDiagnostics
{
    private const string Category = "CStructSharp";

    public static readonly DiagnosticDescriptor LayoutInvalid = new(
        "CSG001",
        "Layout does not compile",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The layout text given to [CStructLayout] fails to parse or compile; the message is the runtime's own diagnostic.");

    public static readonly DiagnosticDescriptor FileNotFound = new(
        "CSG002",
        "Layout file not found",
        "The layout file '{0}' is not among the project's AdditionalFiles; add <AdditionalFiles Include=\"{0}\" /> (or the CStructSharp build props, which include every .cstruct file)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NameCollision = new(
        "CSG003",
        "Generated name collision",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Two layout identifiers become the same C# name after PascalCase conversion, or a name clashes with its containing type; use [CStructLayout(KeepNames = true)] or rename the identifier in the layout.");

    public static readonly DiagnosticDescriptor UnknownRoot = new(
        "CSG004",
        "Unknown root declaration",
        "The layout has no declaration named '{0}'; the plain Parse and Serialize methods use '{1}' instead",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NotPartial = new(
        "CSG005",
        "Attributed class must be partial",
        "'{0}' must be declared 'static partial' (and every containing type 'partial') for the generator to add members to it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor CodecDeclarationInvalid = new(
        "CSG006",
        "Custom codec declaration is invalid",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An entry of [CStructLayout(Codecs = ...)] must be \"name\", \"name:size\", \"name:size:alignment\", or \"name:*:alignment\" with an identifier name, a non-negative size, and a power-of-two alignment, and must not repeat a built-in type.");

    public static readonly DiagnosticDescriptor LanguageVersion = new(
        "CSG010",
        "C# 12 or later is required",
        "Generated code needs C# 12 (the project uses {0}); set <LangVersion>12</LangVersion> or later",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
