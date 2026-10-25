namespace CStructSharp.Generators;

using Microsoft.CodeAnalysis;

/// <summary>The diagnostics the generator and analyzer report; ids, severities, and titles are listed in AnalyzerReleases.Unshipped.md.</summary>
internal static class GeneratorDiagnostics
{
    private const string Category = "CStructSharp";

    /// <summary>CSG001 (error): the layout fails to parse or compile; the message is the runtime diagnostic.</summary>
    public static readonly DiagnosticDescriptor LayoutInvalid = new(
        "CSG001",
        "Layout does not compile",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "The layout text given to [CStructLayout] fails to parse or compile; the message is the runtime's own diagnostic.");

    /// <summary>CSG002 (error): the attribute's <c>File</c> matches none of the project's AdditionalFiles.</summary>
    public static readonly DiagnosticDescriptor FileNotFound = new(
        "CSG002",
        "Layout file not found",
        "The layout file '{0}' is not among the project's AdditionalFiles; add <AdditionalFiles Include=\"{0}\" /> (or the CStructSharp build props, which include every .cstruct file)",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>CSG003 (error): two generated C# names clash, or a name clashes with its containing type.</summary>
    public static readonly DiagnosticDescriptor NameCollision = new(
        "CSG003",
        "Generated name collision",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Two layout identifiers become the same C# name after PascalCase conversion, or a name clashes with its containing type; use [CStructLayout(KeepNames = true)] or rename the identifier in the layout.");

    /// <summary>CSG004 (warning): the attribute's <c>Root</c> names no declaration; the default root is used.</summary>
    public static readonly DiagnosticDescriptor UnknownRoot = new(
        "CSG004",
        "Unknown root declaration",
        "The layout has no declaration named '{0}'; the plain Parse and Serialize methods use '{1}' instead",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>CSG005 (error): the attributed class or one of its containing types is not partial.</summary>
    public static readonly DiagnosticDescriptor NotPartial = new(
        "CSG005",
        "Attributed class must be partial",
        "'{0}' must be declared 'static partial' (and every containing type 'partial') for the generator to add members to it",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>CSG006 (error): a <c>Codecs</c> entry is malformed or repeats a built-in type.</summary>
    public static readonly DiagnosticDescriptor CodecDeclarationInvalid = new(
        "CSG006",
        "Custom codec declaration is invalid",
        "{0}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "An entry of [CStructLayout(Codecs = ...)] must be \"name\", \"name:size\", \"name:size:alignment\", or \"name:*:alignment\" with an identifier name, a non-negative size, and a power-of-two alignment, and must not repeat a built-in type.");

    /// <summary>CSG011 (error): <c>Surface = Views</c> generates only views, so <c>Views = false</c> would leave nothing.</summary>
    public static readonly DiagnosticDescriptor SurfaceNeedsViews = new(
        "CSG011",
        "The Views surface needs views",
        "'{0}' uses Surface = GeneratedSurface.Views with Views = false (from the attribute or the project's CStructSharpViews), which together generate nothing; remove Views = false or choose another surface",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>CSG012 (warning): a <c>CStructSharp*</c> project property has a value the generator does not understand and is ignored.</summary>
    public static readonly DiagnosticDescriptor InvalidProjectDefault = new(
        "CSG012",
        "A CStructSharp project property is not valid",
        "The build property {0} has the value '{1}', which is not valid; it is ignored and the library's default applies",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "CStructSharpSurface takes Full, Reader or Views; CStructSharpBitfieldPacking SysV or Msvc; CStructSharpBitfieldAllocation LowBitFirst or HighBitFirst; CStructSharpPointerSize 1, 2, 4 or 8; CStructSharpCLongWidth 32 or 64; the flag properties true or false.");

    /// <summary>CSG013 (error): <c>RuntimeByteOrder</c> cannot be generated for this layout or surface.</summary>
    public static readonly DiagnosticDescriptor RuntimeByteOrderUnsupported = new(
        "CSG013",
        "RuntimeByteOrder is not supported here",
        "'{0}' sets RuntimeByteOrder, which {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "RuntimeByteOrder needs a surface that only reads (Reader, Parser or Views) and a layout whose bitfields sit in the same place in both byte orders.");

    /// <summary>CSG014 (error): <c>Shape = Struct</c> cannot be generated for this layout or surface.</summary>
    public static readonly DiagnosticDescriptor ValueShapeUnsupported = new(
        "CSG014",
        "A value-type shape is not supported here",
        "'{0}' sets Shape = Struct, which {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Shape = Struct needs the Reader or Parser surface and a layout that is fixed in size: numbers, enums, bitfields, nested structs, text and fixed one-dimensional arrays of numbers.");

    /// <summary>CSG015 (error): <c>Shape = Scalar</c> cannot be generated for this layout or surface.</summary>
    public static readonly DiagnosticDescriptor ScalarShapeUnsupported = new(
        "CSG015",
        "A scalar shape is not supported here",
        "'{0}' sets Shape = Scalar, which {1}",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true,
        description: "Shape = Scalar needs the Reader or Parser surface and a layout of one struct whose only member is a number, an enum or an identifier at offset 0.");

    /// <summary>CSG016 (info): a <c>[CStructLayout]</c> class that no code references.</summary>
    public static readonly DiagnosticDescriptor LayoutUnused = new(
        "CSG016",
        "A generated layout is never used",
        "The layout class '{0}' is never referenced, so its generated code is dead weight; delete it, or use it",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "The class is not visible to other assemblies and nothing in this one names it. Raise the severity in an .editorconfig to make it a warning or error.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>CSG017 (info): a <c>[CStructLayout]</c> class generates more than the members used need.</summary>
    public static readonly DiagnosticDescriptor LayoutOvergenerates = new(
        "CSG017",
        "A generated layout generates more than its uses need",
        "The layout class '{0}' is used only through {1}; generate less with {2}",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "Every member of the layout the code uses is also generated by the smaller surface or without views. The suggestion is made for a class that no other assembly can see and whose uses are all visible.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>CSG018 (warning): the conditions on one member cannot all be satisfied by any value.</summary>
    public static readonly DiagnosticDescriptor ConstraintUnsatisfiable = new(
        "CSG018",
        "Constraints on a member can never hold",
        "The conditions on '{0}' can never hold: {1}, so every read of this layout fails",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The analysis compares the member with constants and combines the comparisons; it reports only what it can prove, so a condition it cannot reduce (arithmetic on other members, masks) is never reported.");

    /// <summary>CSG019 (info): one constraint label is used for conditions that differ.</summary>
    public static readonly DiagnosticDescriptor ConstraintLabelReused = new(
        "CSG019",
        "A constraint label names different conditions",
        "The label '{0}' names different conditions in this layout: {1}; a report cannot tell them apart",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true,
        description: "A label identifies the constraint that failed, so give each condition its own, or keep one label only where the same condition is meant.");

    /// <summary>CSG020 (warning): a <c>@magic</c> condition sits on a member that follows a variable-size member.</summary>
    public static readonly DiagnosticDescriptor MagicAfterVariableSize = new(
        "CSG020",
        "A magic follows a variable-size member",
        "The @magic on '{0}' follows a variable-size member: {1}, so the members in front of it are read (and their conditions held back) before it is checked",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A magic identifies the format, so put it before any member whose size depends on the data. Where that is impossible the magic still works, but it is evaluated at its member and Matches does not evaluate it.");

    /// <summary>CSG100 (error): a mapped type is not partial or lacks a parameterless constructor.</summary>
    public static readonly DiagnosticDescriptor MappedNotPartial = new(
        "CSG100",
        "Mapped type must be partial with a parameterless constructor",
        "'{0}' {1} for the generator to add its ICStructMapped implementation",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>CSG101 (error): a mapped member's class is not [CStructMapped] or ICStructMapped&lt;T&gt;.</summary>
    public static readonly DiagnosticDescriptor MappedMemberType = new(
        "CSG101",
        "Mapped member type is not mapped",
        "The type of '{0}', {1}, is a class that is neither [CStructMapped] nor ICStructMapped<T>; mark it, or map the member as StructValue",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>CSG102 (warning): a mapped member matches no member of its layout.</summary>
    public static readonly DiagnosticDescriptor MappedMemberMissing = new(
        "CSG102",
        "Mapped member has no layout counterpart",
        "'{0}' matches no member of the layout '{1}' (by exact name, case-insensitively, or ignoring underscores); add [CStructMember(\"name\")] or rename it",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>CSG200 (warning): a constant path names a declaration or member the visible layout lacks.</summary>
    public static readonly DiagnosticDescriptor PathUnresolved = new(
        "CSG200",
        "Path does not resolve against the layout",
        "The path '{0}' does not resolve against the layout this CStruct was built from: {1}",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "The receiver's layout is visible (new CStruct(\"...\"), CStruct.GetOrCompile(\"...\"), or a [CStructLayout] class's Layout) and the constant path names a declaration or member it does not have; the runtime would throw CStructPathException.");

    /// <summary>CSG201 (info): Parse selects a root that is a union or a scalar rather than a struct.</summary>
    public static readonly DiagnosticDescriptor ParseNotStruct = new(
        "CSG201",
        "Parse selects a root that is not a struct",
        "'{0}' is {1}; Parse returns structs only - use ReadValue for a union or a scalar",
        Category,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>CSG300 (warning): <c>dynamic</c> binds a parsed value in a project published trimmed or AOT.</summary>
    public static readonly DiagnosticDescriptor DynamicUnderAot = new(
        "CSG300",
        "dynamic over a parsed value in a trimmed or AOT-published project",
        "This project publishes trimmed or AOT, where dynamic binding of a {0} is not available; read members through the indexer, Get<T>, or a mapped class",
        Category,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>CSG010 (error): the consuming project compiles with a C# version older than 12.</summary>
    public static readonly DiagnosticDescriptor LanguageVersion = new(
        "CSG010",
        "C# 12 or later is required",
        "Generated code needs C# 12 (the project uses {0}); set <LangVersion>12</LangVersion> or later",
        Category,
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
