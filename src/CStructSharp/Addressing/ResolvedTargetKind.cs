namespace CStructSharp.Addressing;

/// <summary>Identifies how the terminal path segment relates to its backing storage.</summary>
internal enum ResolvedTargetKind
{
    /// <summary>The path names only the root declaration, which starts at the operation origin.</summary>
    Root,

    /// <summary>The path ends at a declared field, including a whole array field.</summary>
    Field,

    /// <summary>The path ends at one selected element of an array field.</summary>
    ArrayElement,

    /// <summary>The path ends with <c>.address</c>: the pointer's own stored bits, not the data it points to.</summary>
    PointerAddress,

    /// <summary>The path ends with one or more <c>.value</c> accessors: the pointed-to storage.</summary>
    PointerValue,
}
