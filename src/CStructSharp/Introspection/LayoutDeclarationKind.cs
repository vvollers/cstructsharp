namespace CStructSharp.Introspection;

/// <summary>The kind of a top-level declaration as reported by <see cref="LayoutDeclarationInfo.Kind"/>.</summary>
public enum LayoutDeclarationKind
{
    /// <summary>A sequential composite.</summary>
    Struct,

    /// <summary>An overlapping composite.</summary>
    Union,

    /// <summary>An integral enum.</summary>
    Enum,

    /// <summary>A bitmask enum declared with <c>flag</c>.</summary>
    Flag,

    /// <summary>An alias of another type, with optional pointer depth and array shape.</summary>
    Typedef,
}
