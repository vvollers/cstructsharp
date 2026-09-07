namespace CStructSharp;

/// <summary>Identifies how the terminal path segment relates to its backing storage.</summary>
internal enum ResolvedTargetKind
{
    Root,
    Field,
    ArrayElement,
    PointerAddress,
    PointerValue,
}
