namespace CStructSharp.Addressing;

using System;
using System.Collections.Generic;
using CStructSharp.Compilation;

/// <summary>
///     The storage a path selects: its absolute address plus the path facts (array selection, bitfield window,
///     pointer traversal, compiled field) that a byte address alone cannot express. Selected reads, writes and
///     queries decode or encode the target from these facts.
/// </summary>
/// <remarks>
///     Create one through the factory for its kind (<see cref="Root"/>, <see cref="Field"/>,
///     <see cref="PointerAddress"/> or <see cref="PointerValue"/>). A target is immutable; the lists it holds are
///     owned by the traversal that created it.
/// </remarks>
internal sealed class ResolvedTarget
{
    /// <summary>Creates one immutable target and snapshots collection data owned by the traversal.</summary>
    private ResolvedTarget(
        long address,
        ResolvedTargetKind kind,
        CompiledCompositeType? targetComposite,
        IReadOnlyList<string> debugPrefix,
        IReadOnlyList<int> selectedIndexes,
        int alignment,
        int containingStructureDepth,
        CompiledField? effectiveCompiledField,
        CompiledField? writableCompiledField)
    {
        this.Address = address;
        this.Kind = kind;
        this.TargetComposite = targetComposite;
        this.DebugPrefix = Snapshot(debugPrefix);
        this.SelectedIndexes = Snapshot(selectedIndexes);
        this.Alignment = alignment;
        this.ContainingStructureDepth = containingStructureDepth;
        this.EffectiveCompiledField = effectiveCompiledField;
        this.WritableCompiledField = writableCompiledField;
    }

    /// <summary>Gets the absolute byte address of the selected storage.</summary>
    public long Address { get; }

    /// <summary>Gets the alignment, in bytes, of the selected storage.</summary>
    public int Alignment { get; }

    /// <summary>Gets the element count of an unindexed array target, or <see langword="null"/> for a scalar target.</summary>
    public int? ArrayLength { get; private init; }

    /// <summary>Gets the first bit of a bitfield target within its storage unit, counted from the unit's low bit.</summary>
    public int BitOffset { get; private init; }

    /// <summary>Gets the size, in bytes, of a bitfield target's storage unit, or 0 when the target is not a bitfield.</summary>
    public int BitStorageSize { get; private init; }

    /// <summary>Gets the active structure depth above a selected target object.</summary>
    public int ContainingStructureDepth { get; }

    /// <summary>Gets the path names from the root to the target, used to name debug records.</summary>
    public IReadOnlyList<string> DebugPrefix { get; }

    /// <summary>Gets the compiled field whose declaration the target reads, or <see langword="null"/> for a struct root.</summary>
    public CompiledField? EffectiveCompiledField { get; }

    /// <summary>Gets whether the target is a whole array rather than one element or a scalar.</summary>
    public bool IsArray { get; private init; }

    /// <summary>Gets which kind of storage the path selected.</summary>
    public ResolvedTargetKind Kind { get; }

    /// <summary>Gets how many contextual <c>.value</c> accessors the path followed.</summary>
    public int PointerAccessorsConsumed { get; private init; }

    /// <summary>Gets the address the last followed pointer stored, or <see langword="null"/> when no pointer was followed.</summary>
    public long? PointerTargetAddress { get; private init; }

    /// <summary>Gets the pointer levels still declared on the target after the followed accessors.</summary>
    public int RemainingPointerDepth { get; private init; }

    /// <summary>Gets the selected index of the outermost dimension, or <see langword="null"/> when no element is selected.</summary>
    public int? SelectedArrayIndex { get; private init; }

    /// <summary>Gets every index the path supplied, in path order.</summary>
    public IReadOnlyList<int> SelectedIndexes { get; }

    /// <summary>The struct or union the target's type resolves to (through any pointer levels), or <see langword="null"/> for a primitive or enum.</summary>
    public CompiledCompositeType? TargetComposite { get; }

    /// <summary>Gets the compiled field a write encodes through, or <see langword="null"/> when the target cannot be written as one field.</summary>
    public CompiledField? WritableCompiledField { get; }

    /// <summary>Returns whether this target selects one array item instead of the declared collection.</summary>
    public bool SelectsArrayElement => this.SelectedArrayIndex.HasValue;

    /// <summary>Returns whether resolving this target followed at least one pointer value.</summary>
    public bool TraversesPointer => this.PointerAccessorsConsumed > 0;

    /// <summary>Creates the target of a one-segment path: the root declaration itself.</summary>
    /// <param name="address">The root's absolute start.</param>
    /// <param name="name">The root's declared name.</param>
    /// <param name="composite">The struct or union the root resolves to, if any.</param>
    /// <param name="rootField">The compiled field of a typedef or enum root, if any.</param>
    /// <param name="selection">The root's array shape; a root never selects an element.</param>
    /// <param name="alignment">The root's alignment, in bytes.</param>
    /// <returns>The root target.</returns>
    public static ResolvedTarget Root(long address, string name, CompiledCompositeType? composite, CompiledField? rootField, ArraySelection selection, int alignment)
    {
        return new ResolvedTarget(address, ResolvedTargetKind.Root, composite, new[] { name, }, Array.Empty<int>(), alignment, 0, rootField, rootField)
        {
            IsArray = selection.IsArray,
            ArrayLength = selection.Length,
        };
    }

    /// <summary>Creates the target of a declared field or one of its array elements.</summary>
    /// <param name="field">The field's compiled shape, peeled to the selected dimension.</param>
    /// <param name="address">The field's or element's absolute start.</param>
    /// <param name="selection">The array selection at the target.</param>
    /// <param name="bitOffset">The bitfield's first bit within its unit; 0 for other fields.</param>
    /// <param name="bitStorageSize">The bitfield unit's size in bytes; 0 for other fields.</param>
    /// <param name="structureDepth">The structure depth above the target.</param>
    /// <param name="context">The traversal context at the target.</param>
    /// <returns>A <see cref="ResolvedTargetKind.Field"/> or <see cref="ResolvedTargetKind.ArrayElement"/> target.</returns>
    public static ResolvedTarget Field(CompiledField field, long address, ArraySelection selection, int bitOffset, int bitStorageSize, int structureDepth, TargetResolutionContext context)
    {
        ResolvedTargetKind kind = selection.Index.HasValue ? ResolvedTargetKind.ArrayElement : ResolvedTargetKind.Field;
        return new ResolvedTarget(address, kind, field.TargetComposite, context.DebugPrefix, context.SelectedIndexes, field.Alignment, structureDepth, field, field)
        {
            BitOffset = bitOffset,
            BitStorageSize = bitStorageSize,
            RemainingPointerDepth = field.PointerDepth,
            IsArray = selection.IsArray,
            ArrayLength = selection.Length,
            SelectedArrayIndex = selection.Index,
            PointerAccessorsConsumed = context.PointerAccessorsConsumed,
            PointerTargetAddress = context.PointerTargetAddress,
        };
    }

    /// <summary>Creates the target of a contextual <c>.address</c> accessor: the pointer's own stored bits.</summary>
    /// <param name="field">The pointer field's compiled shape.</param>
    /// <param name="address">The pointer storage's absolute start.</param>
    /// <param name="pointerSize">The pointer width, in bytes, which is also its alignment.</param>
    /// <param name="selection">The array selection at the target.</param>
    /// <param name="structureDepth">The structure depth above the target.</param>
    /// <param name="context">The traversal context at the target.</param>
    /// <returns>A <see cref="ResolvedTargetKind.PointerAddress"/> target.</returns>
    public static ResolvedTarget PointerAddress(CompiledField field, long address, int pointerSize, ArraySelection selection, int structureDepth, TargetResolutionContext context)
    {
        return new ResolvedTarget(address, ResolvedTargetKind.PointerAddress, field.TargetComposite, context.DebugPrefix, context.SelectedIndexes, pointerSize, structureDepth, field, null)
        {
            RemainingPointerDepth = field.PointerDepth,
            IsArray = selection.IsArray,
            ArrayLength = selection.Length,
            SelectedArrayIndex = selection.Index,
            PointerAccessorsConsumed = context.PointerAccessorsConsumed,
            PointerTargetAddress = context.PointerTargetAddress,
        };
    }

    /// <summary>Creates the target reached by one or more contextual <c>.value</c> accessors.</summary>
    /// <param name="field">The pointer field's compiled shape.</param>
    /// <param name="writableField">The compiled shape of the pointed-to storage.</param>
    /// <param name="address">The pointed-to storage's absolute start.</param>
    /// <param name="remainingPointerDepth">The pointer levels left after the followed accessors.</param>
    /// <param name="selection">The array selection at the target.</param>
    /// <param name="structureDepth">The structure depth above the target.</param>
    /// <param name="context">The traversal context at the target.</param>
    /// <returns>A <see cref="ResolvedTargetKind.PointerValue"/> target.</returns>
    public static ResolvedTarget PointerValue(CompiledField field, CompiledField writableField, long address, int remainingPointerDepth, ArraySelection selection, int structureDepth, TargetResolutionContext context)
    {
        return new ResolvedTarget(address, ResolvedTargetKind.PointerValue, field.TargetComposite, context.DebugPrefix, context.SelectedIndexes, writableField.Alignment, structureDepth, field, writableField)
        {
            RemainingPointerDepth = remainingPointerDepth,
            IsArray = selection.IsArray,
            ArrayLength = selection.Length,
            SelectedArrayIndex = selection.Index,
            PointerAccessorsConsumed = context.PointerAccessorsConsumed,
            PointerTargetAddress = address,
        };
    }

    /// <summary>Copies a read-only list without retaining a caller-owned mutable collection.</summary>
    /// <remarks>
    ///     An array the target owns: the traversal context builds a fresh array at every step and never mutates
    ///     it, so its arrays are kept as they are; any other list (a caller's own collection) is copied.
    /// </remarks>
    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> values)
    {
        return values is T[] array ? array : Copy(values);
    }

    /// <summary>Copies a read-only list into a new array.</summary>
    private static T[] Copy<T>(IReadOnlyList<T> values)
    {
        var result = new T[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }

        return result;
    }
}
