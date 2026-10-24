namespace CStructSharp.Diagnostics;

/// <summary>What a failed <c>@expect</c> or <c>@supports</c> constraint says about the data it checked.</summary>
public enum ConstraintKind
{
    /// <summary>An <c>@expect</c> condition failed: the value is not valid for the structure, so the data is malformed.</summary>
    Malformed = 0,

    /// <summary>A <c>@supports</c> condition failed: the value may be valid data, but the layout does not support it (an unknown version, an unimplemented feature).</summary>
    Unsupported = 1,
}
