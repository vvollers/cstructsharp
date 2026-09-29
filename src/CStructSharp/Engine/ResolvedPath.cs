namespace CStructSharp.Engine;

using CStructSharp.Addressing;
using CStructSharp.Compilation;

/// <summary>
///     The storage a path selects, as the compiled engine's <see cref="TargetResolver"/> found it: its absolute address and
///     the facts a byte address alone cannot express - the kind of target, the array selection, the bitfield unit, the
///     pointers followed, the nesting depth above it - which the selected read, the address query and the length query
///     use. It holds what the interpreter's resolved target holds for the same path.
/// </summary>
/// <remarks>
///     A value created once per resolution. <see cref="Declared"/> and <see cref="Indexes"/> identify the member the path
///     selected, so the member's compiled read can be cached although every resolution builds a new element view.
/// </remarks>
internal readonly struct ResolvedPath
{
    /// <summary>Gets which kind of storage the path selected.</summary>
    public ResolvedTargetKind Kind { get; init; }

    /// <summary>Gets the absolute byte address of the selected storage (the pointed-to storage for a <c>.value</c> target).</summary>
    public long Address { get; init; }

    /// <summary>
    ///     Gets the compiled field the target is described by: a root's field (none for a struct root), the selected field
    ///     or element view, the pointer whose <c>.address</c> was selected, or the pointer (at the level followed last) whose
    ///     <c>.value</c> was selected.
    /// </summary>
    public CompiledField? Effective { get; init; }

    /// <summary>Gets the struct or union the target's type resolves to (through any pointer levels), or <see langword="null"/>.</summary>
    public CompiledCompositeType? TargetComposite { get; init; }

    /// <summary>Gets the first bit of a bitfield target within its storage unit.</summary>
    public int BitOffset { get; init; }

    /// <summary>Gets the size in bytes of a bitfield target's storage unit, or 0 when the target is not a bitfield.</summary>
    public int BitStorageSize { get; init; }

    /// <summary>Gets a value indicating whether the declared field is an array, whether or not the path indexed it.</summary>
    public bool IsArray { get; init; }

    /// <summary>Gets the element count of the dimension left unindexed, or <see langword="null"/> when the target is not array-shaped.</summary>
    public int? ArrayLength { get; init; }

    /// <summary>Gets the index that selected one element, or <see langword="null"/> when the target is not one element.</summary>
    public int? SelectedArrayIndex { get; init; }

    /// <summary>Gets the pointer levels still declared on the target after the followed accessors.</summary>
    public int RemainingPointerDepth { get; init; }

    /// <summary>Gets how many <c>.value</c> accessors the path followed.</summary>
    public int PointerAccessorsConsumed { get; init; }

    /// <summary>Gets the address the last followed pointer stored, or <see langword="null"/> when no pointer was followed.</summary>
    public long? PointerTargetAddress { get; init; }

    /// <summary>Gets the nesting depth above the target, which a read of the target continues from.</summary>
    public int ContainingStructureDepth { get; init; }

    /// <summary>Gets the declared member (or root field) the path selected, or <see langword="null"/> for a struct root.</summary>
    public CompiledField? Declared { get; init; }

    /// <summary>Gets the number of indexes the path applied to <see cref="Declared"/>.</summary>
    public int Indexes { get; init; }

    /// <summary>Gets a value indicating whether the target selects one element of an array rather than the declared collection.</summary>
    public bool SelectsArrayElement => this.SelectedArrayIndex.HasValue;
}
