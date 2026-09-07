namespace CStructSharp;

using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>Tracks semantic context while traversal descends through fields, unions, arrays, and pointers.</summary>
internal sealed class TargetResolutionContext
{
    /// <summary>Creates a traversal context from already snapshotted path metadata.</summary>
    public TargetResolutionContext(
        IReadOnlyList<CStructElement> debugPrefix,
        IReadOnlyList<int> selectedIndexes,
        long? unionStorageAddress = null,
        int? unionStorageSize = null,
        long? pointerStorageAddress = null,
        long? pointerTargetAddress = null,
        int pointerAccessorsConsumed = 0)
    {
        this.DebugPrefix = debugPrefix;
        this.SelectedIndexes = selectedIndexes;
        this.UnionStorageAddress = unionStorageAddress;
        this.UnionStorageSize = unionStorageSize;
        this.PointerStorageAddress = pointerStorageAddress;
        this.PointerTargetAddress = pointerTargetAddress;
        this.PointerAccessorsConsumed = pointerAccessorsConsumed;
    }

    public IReadOnlyList<CStructElement> DebugPrefix { get; }

    public int PointerAccessorsConsumed { get; }

    public long? PointerStorageAddress { get; }

    public long? PointerTargetAddress { get; }

    public IReadOnlyList<int> SelectedIndexes { get; }

    public long? UnionStorageAddress { get; }

    public int? UnionStorageSize { get; }

    /// <summary>Returns a context with one declared field and optional array selection appended.</summary>
    public TargetResolutionContext EnterField(Field field, int? selectedIndex)
    {
        CStructElement[] debugPrefix = Append(this.DebugPrefix, field);
        IReadOnlyList<int> selectedIndexes = selectedIndex.HasValue
                                                 ? Append(this.SelectedIndexes, selectedIndex.Value)
                                                 : this.SelectedIndexes;
        return new TargetResolutionContext(
            debugPrefix,
            selectedIndexes,
            this.UnionStorageAddress,
            this.UnionStorageSize,
            this.PointerStorageAddress,
            this.PointerTargetAddress,
            this.PointerAccessorsConsumed);
    }

    /// <summary>Returns a context for fields that overlap in one union storage range.</summary>
    public TargetResolutionContext EnterUnion(long address, int size)
    {
        return new TargetResolutionContext(
            this.DebugPrefix,
            this.SelectedIndexes,
            address,
            size,
            this.PointerStorageAddress,
            this.PointerTargetAddress,
            this.PointerAccessorsConsumed);
    }

    /// <summary>Returns a context after following one explicit pointer <c>.value</c> accessor.</summary>
    public TargetResolutionContext FollowPointer(long storageAddress, long targetAddress)
    {
        return new TargetResolutionContext(
            this.DebugPrefix,
            this.SelectedIndexes,
            this.UnionStorageAddress,
            this.UnionStorageSize,
            storageAddress,
            targetAddress,
            checked(this.PointerAccessorsConsumed + 1));
    }

    /// <summary>Appends one immutable semantic value to an existing read-only list.</summary>
    private static T[] Append<T>(IReadOnlyList<T> values, T value)
    {
        var result = new T[values.Count + 1];
        for (int index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }

        result[^1] = value;
        return result;
    }
}
