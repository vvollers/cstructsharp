namespace CStructSharp;

using System;
using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>
///     Carries path semantics that cannot be reconstructed from a byte address, pending the complete compiled IR.
/// </summary>
internal sealed class ResolvedTarget
{
    /// <summary>Creates one immutable target and snapshots collection data owned by the traversal.</summary>
    public ResolvedTarget(
        long address,
        ResolvedTargetKind kind,
        Field? declaredField,
        Field? effectiveField,
        Field? writableField,
        CStructElement? targetElement,
        IReadOnlyList<CStructElement> debugPrefix,
        string? codecName,
        bool isArray,
        int? arrayLength,
        int? selectedArrayIndex,
        IReadOnlyList<int> selectedIndexes,
        int bitOffset,
        int bitStorageSize,
        long? unionStorageAddress,
        int? unionStorageSize,
        long? pointerStorageAddress,
        long? pointerTargetAddress,
        int pointerAccessorsConsumed,
        int remainingPointerDepth,
        int alignment,
        int? fixedSize,
        int containingStructureDepth,
        CompiledField? effectiveCompiledField = null,
        CompiledField? writableCompiledField = null)
    {
        this.Address = address;
        this.Kind = kind;
        this.DeclaredField = declaredField;
        this.EffectiveField = effectiveField;
        this.WritableField = writableField;
        this.TargetElement = targetElement;
        this.DebugPrefix = Array.AsReadOnly(Copy(debugPrefix));
        this.CodecName = codecName;
        this.IsArray = isArray;
        this.ArrayLength = arrayLength;
        this.SelectedArrayIndex = selectedArrayIndex;
        this.SelectedIndexes = Array.AsReadOnly(Copy(selectedIndexes));
        this.BitOffset = bitOffset;
        this.BitStorageSize = bitStorageSize;
        this.UnionStorageAddress = unionStorageAddress;
        this.UnionStorageSize = unionStorageSize;
        this.PointerStorageAddress = pointerStorageAddress;
        this.PointerTargetAddress = pointerTargetAddress;
        this.PointerAccessorsConsumed = pointerAccessorsConsumed;
        this.RemainingPointerDepth = remainingPointerDepth;
        this.Alignment = alignment;
        this.FixedSize = fixedSize;
        this.ContainingStructureDepth = containingStructureDepth;
        this.EffectiveCompiledField = effectiveCompiledField;
        this.WritableCompiledField = writableCompiledField;
    }

    public long Address { get; }

    public int Alignment { get; }

    public int? ArrayLength { get; }

    public int BitOffset { get; }

    public int BitStorageSize { get; }

    public string? CodecName { get; }

    /// <summary>Gets the active structure depth above a selected target object.</summary>
    public int ContainingStructureDepth { get; }

    public Field? DeclaredField { get; }

    public IReadOnlyList<CStructElement> DebugPrefix { get; }

    public Field? EffectiveField { get; }

    public CompiledField? EffectiveCompiledField { get; }

    public int? FixedSize { get; }

    public bool IsArray { get; }

    public ResolvedTargetKind Kind { get; }

    public int PointerAccessorsConsumed { get; }

    public long? PointerStorageAddress { get; }

    public long? PointerTargetAddress { get; }

    public int RemainingPointerDepth { get; }

    public int? SelectedArrayIndex { get; }

    public IReadOnlyList<int> SelectedIndexes { get; }

    public CStructElement? TargetElement { get; }

    public long? UnionStorageAddress { get; }

    public int? UnionStorageSize { get; }

    public Field? WritableField { get; }

    public CompiledField? WritableCompiledField { get; }

    /// <summary>Returns whether this target selects one array item instead of the declared collection.</summary>
    public bool SelectsArrayElement => this.SelectedArrayIndex.HasValue;

    /// <summary>Returns whether resolving this target followed at least one pointer value.</summary>
    public bool TraversesPointer => this.PointerAccessorsConsumed > 0;

    /// <summary>Copies a read-only list without retaining a caller-owned mutable collection.</summary>
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
