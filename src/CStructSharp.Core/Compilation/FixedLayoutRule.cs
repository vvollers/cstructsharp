namespace CStructSharp.Compilation;

/// <summary>
///     The structural rule for a fixed layout: a struct whose every member is read by seeking to a build-time offset,
///     taking a known number of bytes and decoding them, with no decision that depends on the data. The runtime's static
///     read plan, the generator's fixed reader and the mapped-type fixed reader all start from this rule; each then adds
///     only the checks about which codecs its own decoder handles.
/// </summary>
internal static class FixedLayoutRule
{
    /// <summary>The deepest nesting level a fixed layout may reach, counting the outermost struct as level 0.</summary>
    public const int MaximumNestingDepth = 64;

    /// <summary>
    ///     Returns whether a composite can have a fixed layout: a struct (not a union) with a fixed size and no
    ///     conditional members of its own.
    /// </summary>
    /// <param name="composite">The compiled composite.</param>
    /// <returns>Whether the composite itself qualifies; each member still needs <see cref="IsFixedMember"/>.</returns>
    public static bool IsFixedComposite(CompiledCompositeType composite)
        => !composite.IsUnion && composite.Symbol.FixedSize is not null && !composite.HasDirectConditionalFields;

    /// <summary>
    ///     Returns whether a member can be read by offset: placed at a build-time offset (so the layout already checked
    ///     any offset assertion), unconditional, neither a pointer nor a bitfield, and either a scalar or a
    ///     one-dimensional array of fixed length. A nested struct member additionally needs its own fixed layout.
    /// </summary>
    /// <param name="field">The compiled member.</param>
    /// <returns>Whether the member's placement and shape qualify; its codec is checked by the consumer.</returns>
    public static bool IsFixedMember(CompiledField field)
        => field.FixedOffset is not null && field.BitSize == 0 && !field.IsZeroWidthBitfield && field.PointerDepth == 0 &&
           !field.IsConditional &&
           (field.Array.Kind == CompiledArrayKind.Scalar ||
            (field.Array.Kind == CompiledArrayKind.Fixed && field.Array.Dimensions.Length == 1 && field.Array.FixedCount is not null));
}
