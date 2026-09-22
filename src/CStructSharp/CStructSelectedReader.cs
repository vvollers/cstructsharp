namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>Reads a selected nested object without materializing unrelated siblings.</summary>
public partial class CStruct
{
    /// <summary>The general reader's scalar capture rule; see <see cref="LayoutVariableCapture"/>.</summary>
    private static void CaptureScalar(CStructOperationContext state, string name, object? value)
    {
        LayoutVariableCapture.Capture(state.Variables, name, value);
    }

    /// <summary>A <c>char[N]</c> buffer is one Latin-1 character per byte, exactly as the per-element <c>char</c> reader produces.</summary>
    private static string ReadLatin1Characters(ReadOnlySpan<byte> bytes)
    {
        Span<char> chars = bytes.Length <= 256 ? stackalloc char[bytes.Length] : new char[bytes.Length];
        for (int index = 0; index < chars.Length; index++)
        {
            chars[index] = (char)bytes[index];
        }

        return new string(chars);
    }

    /// <summary>Restores the expression context so overlapping union views cannot influence one another or later fields.</summary>
    private static void RestoreVariables(
        Dictionary<string, Expr> destination,
        IReadOnlyDictionary<string, Expr> snapshot)
    {
        destination.Clear();
        foreach (KeyValuePair<string, Expr> variable in snapshot)
        {
            destination.Add(variable.Key, variable.Value);
        }
    }

    /// <summary>Requires a semantic target that has reached a concrete struct rather than pointer storage.</summary>
    private static CompiledCompositeType ResolveStructTarget(ResolvedTarget target)
    {
        bool stopsAtPointerStorage = target.Kind == ResolvedTargetKind.PointerAddress ||
                                     ((target.Kind is ResolvedTargetKind.Field or ResolvedTargetKind.ArrayElement) &&
                                      target.EffectiveCompiledField?.PointerDepth > 0);
        if (stopsAtPointerStorage ||
            target.RemainingPointerDepth > 0 ||
            target.TargetComposite is not { } composite)
        {
            throw new CStructPathException("The selected path does not resolve to a struct object.");
        }

        return composite;
    }

    /// <summary>Reads one non-union struct through the shared compiled traversal and completes its storage extent.</summary>
    /// <param name="composite">The compiled struct whose fields are read in declaration order.</param>
    /// <param name="destination">The result, or its parent's result for an anonymous promoted struct.</param>
    /// <param name="state">The input position, limits, variables and optional debug records.</param>
    /// <param name="debugStack">The enclosing debug path, or null when no path is needed.</param>
    /// <exception cref="CStructReadException">The input is truncated, invalid or exceeds a read limit.</exception>
    private void ReadCompiledStructInto(
        CompiledCompositeType composite,
        StructValue destination,
        CStructOperationContext state,
        DebugPath? debugStack)
    {
        // Static read plan: a fully fixed composite is read from one span of its exact size. The span is
        // taken only when the whole extent is present and within the read budget, so every truncation and limit
        // failure still comes from the general path below, at the field it always reported.
        // The destination must be this composite's own value: a promoted anonymous member reads into its parent's
        // container, whose slots belong to the parent's shape.
        // Limits that the general reader reports at a field inside the composite are checked up front, so a plan
        // never consumes bytes and then fails: such inputs go to the general reader and fail where they always did.
        // The general reader aligns members to absolute stream positions; the plan's offsets are relative to the
        // struct start, which coincide only when the struct itself starts on its alignment boundary.
        if (!state.Debug && !StaticReadPlan.DisabledForTesting && ReferenceEquals(destination.Shape, composite.Shape) && composite.StaticPlan is StaticReadPlan plan &&
            state.StructureDepth + plan.NestingDepth <= state.MaxNestingDepth && plan.MaximumArrayCount <= state.MaxArrayElements &&
            (!this.Aligned || state.Stream.Position % composite.Symbol.Alignment == 0))
        {
            if (state.Stream.TryReadSpanWithinBudget(plan.Size, out ReadOnlySpan<byte> staticBytes))
            {
                this.ExecuteStaticPlan(plan, staticBytes, destination, state);
                state.CurrentBitOffset = 0;
                state.CurrentBitfieldType = null;
                state.NextPosition = state.Stream.Position;
                return;
            }

            // A stream source (FileStream, a MemoryStream without an exposed buffer) reads the composite's extent
            // into a pooled block first; composites beyond the block size stay on the general reader.
            if (plan.Size <= StaticReadPlan.MaximumBlockSize)
            {
                byte[] block = ArrayPool<byte>.Shared.Rent(plan.Size);
                try
                {
                    if (state.Stream.TryReadBlockWithinBudget(block.AsSpan(0, plan.Size)))
                    {
                        this.ExecuteStaticPlan(plan, block.AsSpan(0, plan.Size), destination, state);
                        state.CurrentBitOffset = 0;
                        state.CurrentBitfieldType = null;
                        state.NextPosition = state.Stream.Position;
                        return;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(block);
                }
            }
        }

        var cursor = new CompositeFieldPlacementCursor(state.Stream.Position, state.Aligned, this.BitfieldPacking, this.highBitFirst);
        var variableScope = composite.HasDirectConditionalFields ? new ConditionalVariableScope(composite, state.Variables) : null;
        var selection = composite.HasDirectConditionalFields ? new ConditionalFieldSelection(this.layoutExpressionEvaluator, composite.ConditionalGroupCount) : null;

        state.EnterStructure();
        try
        {
            foreach (CompiledField field in composite.Fields)
            {
                bool active = selection?.IsActive(field, state.Variables) ?? true;
                if (field.Declaration.Condition is not null)
                {
                    state.ConditionalLayoutTrace?.Add((field.Declaration.Name.Name, state.Stream.Position, active ? 1L : 0L));
                }

                if (!active)
                {
                    continue;
                }

                try
                {
                    this.HandleCStructElement(
                        field.Declaration,
                        destination,
                        state,
                        debugStack,
                        -1,
                        field.Declaration is Struct,
                        field,
                        cursor);
                }
                catch (CStructException exception) when (exception.NoteMember(field.Name, field.DisplayTypeSpelling))
                {
                    // Never entered: the filter records the innermost field and lets the exception propagate.
                    throw;
                }

                variableScope?.CompleteField(field, state.Variables);
            }
        }
        finally
        {
            state.ExitStructure();
        }

        // General field decoding retains padding temporarily for array/text conversion and debug records only.
        destination.Remove(string.Empty);

        // The cursor already tracks the position past any dangling bitfield unit's full reserved span - trust it
        // rather than state.Stream.Position, which a shared bitfield read may have rewound mid-unit for extraction.
        state.Stream.Position = cursor.FinishComposite(composite.Symbol.Alignment);
        state.CurrentBitOffset = 0;
        state.CurrentBitfieldType = null;
        state.NextPosition = state.Stream.Position;
    }

    /// <summary>Runs a static read plan over the composite's bytes, reproducing the general reader's side effects.</summary>
    private void ExecuteStaticPlan(StaticReadPlan plan, ReadOnlySpan<byte> bytes, StructValue destination, CStructOperationContext state)
    {
        state.EnterStructure();
        try
        {
            StaticReadOperation[] operations = plan.Operations;
            for (int index = 0; index < operations.Length; index++)
            {
                StaticReadOperation operation = operations[index];
                CompiledField field = operation.Field;
                switch (operation.Kind)
                {
                case StaticReadKind.Numeric:
                    {
                        object value = field.Codec.ReadNumeric(bytes.Slice(operation.Offset, field.Codec.Size));
                        destination.SetFreshSlot(operation.Slot, value);
                        if (field.CapturesLayoutVariable || state.CaptureAllLayoutVariables)
                        {
                            CaptureScalar(state, field.Declaration.Name.Name, value);
                            state.PublishQualified(field.Declaration.Name.Name);
                        }

                        break;
                    }

                case StaticReadKind.Enum:
                    {
                        object storage = field.Codec.ReadNumeric(bytes.Slice(operation.Offset, field.Codec.Size));
                        EnumValueResult value = CreateEnumValue(field.Enum!, storage);
                        destination.SetFreshSlot(operation.Slot, value);
                        if (field.CapturesLayoutVariable || state.CaptureAllLayoutVariables)
                        {
                            this.UpdateExactLayoutVariable(state.Variables, field.Declaration.Name.Name, value.Value);
                            state.PublishQualified(field.Declaration.Name.Name);
                        }

                        break;
                    }

                case StaticReadKind.CharArray:
                    {
                        if (operation.Count > state.MaxArrayElements)
                        {
                            throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(operation.Count, state.MaxArrayElements));
                        }

                        string text = state.FixedText(ReadLatin1Characters(bytes.Slice(operation.Offset, operation.Count)));
                        destination.SetFreshSlot(operation.Slot, text);
                        if (field.CapturesLayoutVariable || state.CaptureAllLayoutVariables)
                        {
                            state.Variables[field.Declaration.Name.Name] = new Identifier(text);
                            state.PublishQualified(field.Declaration.Name.Name);
                        }

                        break;
                    }

                case StaticReadKind.NumericArray:
                    {
                        if (operation.Count > state.MaxArrayElements)
                        {
                            throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(operation.Count, state.MaxArrayElements));
                        }

                        if (operation.Count == 0)
                        {
                            destination.SetFreshSlot(operation.Slot, new List<object?>(0));
                            break;
                        }

                        IList<object?> values = PrimitiveArrayReader.Decode(bytes.Slice(operation.Offset, operation.Count * field.Codec.Size), field.Codec, operation.Count);
                        destination.SetFreshSlot(operation.Slot, values);
                        if (field.CapturesLayoutVariable || state.CaptureAllLayoutVariables)
                        {
                            CaptureScalar(state, field.Declaration.Name.Name, values[operation.Count - 1]);
                            state.PublishQualified(field.Declaration.Name.Name);
                        }

                        break;
                    }

                case StaticReadKind.Nested:
                    {
                        var nested = new StructValue(operation.NestedComposite!.Shape);
                        destination.SetFreshSlot(operation.Slot, nested);
                        string? outerPrefix = state.QualifiedPrefix;
                        if (field.HasQualifiedPrefix)
                        {
                            state.QualifiedPrefix = outerPrefix is null ? field.QualifiedPrefix : outerPrefix + field.QualifiedPrefix;
                        }

                        this.ExecuteStaticPlan(operation.NestedPlan!, bytes.Slice(operation.Offset, operation.NestedPlan!.Size), nested, state);
                        state.QualifiedPrefix = outerPrefix;
                        break;
                    }

                case StaticReadKind.NestedArray:
                    {
                        if (operation.Count > state.MaxArrayElements)
                        {
                            throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(operation.Count, state.MaxArrayElements));
                        }

                        var elements = new List<object?>(operation.Count);
                        destination.SetFreshSlot(operation.Slot, elements);
                        StaticReadPlan nestedPlan = operation.NestedPlan!;
                        StructShape nestedShape = operation.NestedComposite!.Shape;
                        int elementOffset = operation.Offset;
                        for (int element = 0; element < operation.Count; element++, elementOffset += nestedPlan.Size)
                        {
                            var nested = new StructValue(nestedShape);
                            elements.Add(nested);
                            this.ExecuteStaticPlan(nestedPlan, bytes.Slice(elementOffset, nestedPlan.Size), nested, state);
                        }

                        break;
                    }
                }
            }
        }
        finally
        {
            state.ExitStructure();
        }
    }

    /// <summary>Reads one compiled struct or union at an already resolved address.</summary>
    private (object Result, List<DebugData> DebugData) ParseCompiledStructAt(
        CStructOperationContext state,
        long address,
        CompiledCompositeType target,
        DebugPath? debugPrefix,
        int containingStructureDepth,
        int pointerDereferenceDepth,
        bool debug)
    {
        state.Stream.Position = address;
        state.Debug = debug;
        state.StructureDepth = containingStructureDepth;
        state.PointerDereferenceDepth = pointerDereferenceDepth;

        if (target.IsUnion)
        {
            return (this.ReadUnionValue(target, state, debugPrefix), state.DebugMapping);
        }

        var result = new StructValue(target.Shape);
        this.ReadCompiledStructInto(target, result, state, debugPrefix);

        return (result, state.DebugMapping);
    }

    /// <summary>
    ///     Reads every bounded interpretation of one union from the same address and retains its complete raw storage.
    /// </summary>
    /// <param name="union">The compiled union whose named views share one storage window.</param>
    /// <param name="state">The input position, read limits and debug records; parent variables are restored.</param>
    /// <param name="debugStack">The enclosing debug path, or null for an ordinary read.</param>
    /// <returns>The raw bytes and named member views, excluding unnamed padding.</returns>
    /// <exception cref="CStructReadException">The union storage or a member view cannot be read within the limits.</exception>
    private UnionValue ReadUnionValue(
        CompiledCompositeType union,
        CStructOperationContext state,
        DebugPath? debugStack)
    {
        long unionPosition = state.Stream.Position;
        int unionSize = this.compiledSizeQueries.GetCompiledStructSizeInBytes(union, state.Variables, false);
        long unionEnd = checked(unionPosition + unionSize);
        byte[] rawStorage = new byte[unionSize];
        BinaryPrimitiveIO.ReadExactlyOrThrow(state.Stream, rawStorage);

        state.Stream.Position = unionPosition;
        var decodedMembers = new StructValue(union.Shape);
        bool previousPointerSuppression = state.SuppressPointerDereference;
        var unionInputVariables = new Dictionary<string, Expr>(state.Variables, StringComparer.Ordinal);

        state.EnterStructure();
        try
        {
            // An untagged union does not identify an active member. Decode local views, but never follow an external
            // pointer merely because its address bytes overlap this storage.
            state.SuppressPointerDereference = true;
            foreach (CompiledField field in union.Fields)
            {
                RestoreVariables(state.Variables, unionInputVariables);
                try
                {
                    this.HandleCStructElement(
                        field.Declaration,
                        decodedMembers,
                        state,
                        debugStack,
                        unionPosition,
                        field.Declaration is Struct,
                        field);
                }
                catch (CStructException exception) when (exception.NoteMember(field.Name, field.DisplayTypeSpelling))
                {
                    throw;
                }
            }
        }
        finally
        {
            RestoreVariables(state.Variables, unionInputVariables);
            state.SuppressPointerDereference = previousPointerSuppression;
            state.ExitStructure();
            state.Stream.Position = unionEnd;
            state.NextPosition = unionEnd;
            state.CurrentBitOffset = 0;
            state.CurrentBitfieldType = null;
        }

        // Padding may contribute a debug record and occupies raw storage, but is never a decoded member view.
        decodedMembers.Remove(string.Empty);
        IDictionary<string, object?> memberViews = decodedMembers;
        UnionValue result = UnionValue.FromParsed(union.Name, rawStorage, memberViews);
        if (state.Debug)
        {
            // This record makes the overlapping extent explicit without rereading and recharging the same bytes.
            state.DebugMapping.Add(
                new DebugData
                {
                    Start = unionPosition,
                    End = unionEnd,
                    DebugStack = debugStack,
                    Value = result,
                    Bytes = rawStorage,
                    TypeName = union.Name,
                });
        }

        return result;
    }
}
