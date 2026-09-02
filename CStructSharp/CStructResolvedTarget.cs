namespace CStructSharp;

using System;
using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>Defines the immutable semantic result produced by internal layout-path traversal.</summary>
public partial class CStruct
{
    /// <summary>Identifies how the terminal path segment relates to its backing storage.</summary>
    private enum ResolvedTargetKind
    {
        Root,
        Field,
        ArrayElement,
        PointerAddress,
        PointerValue,
    }

    /// <summary>
    ///     Carries path semantics that cannot be reconstructed from a byte address, pending the complete compiled IR.
    /// </summary>
    private sealed class ResolvedTarget
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

    /// <summary>Tracks semantic context while traversal descends through fields, unions, arrays, and pointers.</summary>
    private sealed class TargetResolutionContext
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
}
