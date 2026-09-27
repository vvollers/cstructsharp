namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>Resolves semantic path targets by walking only the selected compiled layout prefix.</summary>
public partial class CStruct
{
    /// <summary>
    ///     Resolves a path target without materializing the root or following pointers that are not on the path.
    ///     Earlier scalar fields are read only when they may supply runtime layout variables.
    /// </summary>
    private ResolvedTarget ResolveTargetFromLayout(
        CStructOperationContext state,
        IReadOnlyList<PathSegment> segments)
    {
        try
        {
            return this.ResolveTargetFromLayoutCore(state, segments);
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, state.Stream);
            throw;
        }
    }

    /// <summary>Performs semantic target traversal after the public context wrapper has been established.</summary>
    private ResolvedTarget ResolveTargetFromLayoutCore(
        CStructOperationContext state,
        IReadOnlyList<PathSegment> segments)
    {
        Stream stream = state.Stream;
        if (stream is null || !stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Address resolution requires a readable, seekable stream.", nameof(stream));
        }

        if (segments.Count == 0)
        {
            throw new CStructPathException("Path is empty.");
        }

        if (!this.compiledModelQueries.TryGetCompiledDeclaration(segments[0].Name, out CStructElement? root))
        {
            throw this.compiledModelQueries.UnknownRoot(segments[0].Name);
        }

        long rootStart = stream.Position;
        CStructElement declaredRoot = root;
        CStructElement? resolvedRoot = this.compiledModelQueries.ResolveCompiledNamedElement(root);
        if (segments.Count == 1)
        {
            CStructElement targetElement = resolvedRoot ?? declaredRoot;
            CompiledCompositeType? rootTargetStruct = targetElement is Struct rootStructDeclaration
                                                          ? this.compiledSizeQueries.GetCompiledComposite(rootStructDeclaration)
                                                          : null;
            if (rootTargetStruct is not null)
            {
                state.EnsureStructureDepth(1);
            }

            // A typedef, enum, or spelled root (`uint16[EOF]`) is one compiled field: expose it so the length
            // query and selected reads see its array shape and codec like a nested field's.
            CompiledField? rootField = declaredRoot is Typedef or CstructEnum ? this.compiledModelQueries.GetCompiledRootField(declaredRoot) : null;
            bool rootIsArray = rootField is not null &&
                               rootField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;
            int? rootArrayLength = rootIsArray ? this.GetBoundedArrayCount(rootField!, state, rootStart) : null;
            int alignment = rootTargetStruct is null
                                ? rootField?.Alignment ?? 1
                                : rootTargetStruct.Symbol.Alignment;
            return ResolvedTarget.Root(
                rootStart,
                targetElement.Name.Name,
                rootTargetStruct ?? rootField?.TargetComposite,
                rootField,
                new ArraySelection(rootIsArray, rootArrayLength, null),
                alignment);
        }

        if (resolvedRoot is not Struct rootStruct)
        {
            throw new CStructPathException("Root path cannot contain child segments: " + segments[0].Name);
        }

        var context = new TargetResolutionContext(new[] { rootStruct.Name.Name, }, Array.Empty<int>());

        return this.ResolveTargetInStruct(this.compiledSizeQueries.GetCompiledComposite(rootStruct), rootStart, segments, 1, state, context);
    }

    /// <summary>Finds a requested child while measuring only fields that precede it.</summary>
    private ResolvedTarget ResolveTargetInStruct(
        CompiledCompositeType strct,
        long structStart,
        IReadOnlyList<PathSegment> segments,
        int pathIndex,
        CStructOperationContext state,
        TargetResolutionContext context)
    {
        state.EnterStructure();
        try
        {
            return this.ResolveTargetInStructCore(
                strct,
                structStart,
                segments,
                pathIndex,
                state,
                context);
        }
        finally
        {
            state.ExitStructure();
        }
    }

    /// <summary>Resolves one already-budgeted structure level.</summary>
    private ResolvedTarget ResolveTargetInStructCore(
        CompiledCompositeType composite,
        long structStart,
        IReadOnlyList<PathSegment> segments,
        int pathIndex,
        CStructOperationContext state,
        TargetResolutionContext context)
    {
        if (pathIndex >= segments.Count)
        {
            throw new CStructPathException("Path ended before selecting a field.");
        }

        PathSegment requested = segments[pathIndex];
        if (composite.IsUnion)
        {
            if (!composite.FieldsByName.ContainsKey(requested.Name))
            {
                // An anonymous struct member of the union contributes its own names to the union's
                // namespace; every member starts at the union's address.
                foreach (CompiledField promoted in composite.PromotedFields)
                {
                    if (promoted.Composite is { } promotedMember && promotedMember.TryFindField(requested.Name, out _))
                    {
                        return this.ResolveTargetInStruct(promotedMember, structStart, segments, pathIndex, state, context);
                    }
                }
            }

            CompiledField compiledUnionField = composite.FindField(requested.Name);
            int bitStorageSize = compiledUnionField.BitSize > 0
                                     ? compiledUnionField.BitStorageSize ??
                                       throw new InvalidOperationException(
                                           "Compiled bitfield has no storage size: " + compiledUnionField.Name)
                                     : 0;
            return this.ResolveTargetInField(
                compiledUnionField,
                structStart,
                segments,
                pathIndex,
                state,
                context,
                0,
                bitStorageSize);
        }

        var variableScope = composite.HasDirectConditionalFields ? new ConditionalVariableScope(composite, state.Variables) : null;
        var selection = composite.HasDirectConditionalFields ? new ConditionalFieldSelection(this.layoutExpressionEvaluator, composite.ConditionalGroupCount) : null;
        var cursor = new CompositeFieldPlacementCursor(structStart, this.Aligned, this.BitfieldPacking, this.highBitFirst);

        foreach (CompiledField compiledField in composite.Fields)
        {
            if (selection?.IsActive(compiledField, state.Variables) == false)
            {
                continue;
            }

            (long fieldStart, int bitOffset, int unitSize) = cursor.AdvanceToField(compiledField);
            if (compiledField.IsZeroWidthBitfield)
            {
                continue;
            }

            if (string.Equals(compiledField.Name, requested.Name, StringComparison.Ordinal))
            {
                int selectedBitStorageSize = compiledField.BitSize > 0 ? unitSize : 0;
                return this.ResolveTargetInField(
                    compiledField,
                    fieldStart,
                    segments,
                    pathIndex,
                    state,
                    context,
                    bitOffset,
                    selectedBitStorageSize);
            }

            // An anonymous promoted member consumes no path segment of its own - retry the same
            // segment/pathIndex against its own (recursively promoted) fields before giving up. The pre-check
            // below is a pure, side-effect-free tree walk, so a miss costs nothing and a hit is guaranteed to
            // succeed (construction already rejected any flattened-namespace collision), meaning a genuine
            // downstream error inside the matched field can never be misreported as "unknown field" here.
            if (composite.PromotedFields.Contains(compiledField) &&
                compiledField.Composite is { } promotedStruct &&
                promotedStruct.TryFindField(requested.Name, out _))
            {
                return this.ResolveTargetInStruct(promotedStruct, fieldStart, segments, pathIndex, state, context);
            }

            this.CaptureLayoutVariable(compiledField, fieldStart, bitOffset, unitSize, state);
            if (compiledField.BitSize == 0)
            {
                cursor.CompleteField(this.MeasureFieldEnd(compiledField, fieldStart, state));
            }

            variableScope?.CompleteField(compiledField, state.Variables);
        }

        throw new CStructPathException($"Unknown field '{requested.Name}' in '{composite.Name}'.");
    }

    /// <summary>
    ///     Resolves array selection, nested structures, and contextual pointer accessors for one field. An
    ///     N-dimensional array peels one dimension per supplied index, exactly mirroring a single
    ///     dimension's own bounds-check-then-advance step; supplying fewer indices than the
    ///     field has dimensions leaves the target array-shaped, selecting the corresponding lower-dimensional
    ///     sub-array rather than one scalar/struct element.
    /// </summary>
    private ResolvedTarget ResolveTargetInField(
        CompiledField compiledField,
        long fieldStart,
        IReadOnlyList<PathSegment> segments,
        int pathIndex,
        CStructOperationContext state,
        TargetResolutionContext context,
        int bitOffset,
        int bitStorageSize)
    {
        PathSegment segment = segments[pathIndex];
        bool declaredIsArray = compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or
                               CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;

        if (segment.Indexes.Count > 0 && !declaredIsArray)
        {
            throw new CStructPathException("Field is not an indexable fixed array: " + segment.Name);
        }

        int totalDimensions = compiledField.Array.Dimensions.Length;
        if (segment.Indexes.Count > totalDimensions)
        {
            throw new CStructPathException(
                $"Too many array indices for {segment.Name}: expected at most {totalDimensions}, got " +
                $"{segment.Indexes.Count}.");
        }

        CompiledField resolvedField = compiledField;
        long elementStart = fieldStart;
        foreach (int suppliedIndex in segment.Indexes)
        {
            int dimensionCount = this.GetBoundedArrayCount(resolvedField, state, elementStart);
            if (suppliedIndex >= dimensionCount)
            {
                throw new CStructPathException(
                    $"Array index {suppliedIndex} is out of range for {segment.Name} with length {dimensionCount}.");
            }

            elementStart = this.GetArrayElementStart(resolvedField, elementStart, suppliedIndex, state);
            resolvedField = resolvedField.SelectArrayElement();
        }

        bool remainingIsArray = resolvedField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or
                                CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;
        int? arrayLength = remainingIsArray ? this.GetBoundedArrayCount(resolvedField, state, elementStart) : null;
        int? selectedArrayIndex = segment.Indexes.Count > 0 && !remainingIsArray ? segment.Indexes[^1] : null;
        var selection = new ArraySelection(declaredIsArray, arrayLength, selectedArrayIndex);

        context = context.EnterField(compiledField.Name, segment.Indexes);

        if (remainingIsArray && pathIndex + 1 < segments.Count)
        {
            throw new CStructPathException("An array index is required before traversing: " + segment.Name);
        }

        if (pathIndex == segments.Count - 1)
        {
            return this.CreateFieldTarget(
                resolvedField,
                elementStart,
                selection,
                bitOffset,
                bitStorageSize,
                state,
                context);
        }

        PathSegment next = segments[pathIndex + 1];
        if (resolvedField.PointerDepth > 0)
        {
            if (string.Equals(next.Name, "address", StringComparison.Ordinal))
            {
                if (next.Indexes.Count > 0 || pathIndex + 1 != segments.Count - 1)
                {
                    throw new CStructPathException("Pointer .address must be the terminal path segment.");
                }

                return ResolvedTarget.PointerAddress(resolvedField, elementStart, this.PointerSize, selection, state.StructureDepth, context);
            }

            if (!string.Equals(next.Name, "value", StringComparison.Ordinal) || next.Indexes.Count > 0)
            {
                throw new CStructPathException("Expected pointer accessor '.value' or '.address' after: " + segment.Name);
            }

            return this.ResolvePointerTarget(
                resolvedField,
                elementStart,
                segments,
                pathIndex + 1,
                state,
                context,
                selection);
        }

        if (resolvedField.Composite is { } nestedStruct)
        {
            return this.ResolveTargetInStruct(
                nestedStruct,
                elementStart,
                segments,
                pathIndex + 1,
                state,
                context);
        }

        throw new CStructPathException("Cannot traverse through scalar field: " + segment.Name);
    }

    /// <summary>Follows only explicitly requested pointer-value segments and then enters a struct target when needed.</summary>
    private ResolvedTarget ResolvePointerTarget(
        CompiledField compiledField,
        long pointerStorage,
        IReadOnlyList<PathSegment> segments,
        int valueSegmentIndex,
        CStructOperationContext state,
        TargetResolutionContext context,
        ArraySelection selection)
    {
        if (!state.DereferencePointers)
        {
            throw new CStructPathException("Pointer dereference is disabled for the selected path.");
        }

        if (state.PointerDereferenceDepth >= state.MaxPointerDepth)
        {
            throw new CStructReadLimitException(ReadFailures.PointerDepthLimit);
        }

        if (compiledField.HasCountedTarget)
        {
            // The count may name a sibling declared after the pointer, which path resolution never reads.
            throw new CStructPathException(
                "A path cannot select the target of a @count pointer; read the containing struct instead: " + compiledField.Name);
        }

        this.EnsurePointerTargetSize(
            compiledField.PointerDepth,
            compiledField,
            state,
            1);

        long target = this.ReadPointerTargetAddress(pointerStorage, state);
        context = context.FollowPointer(target);
        (long Address, string TypeName, int PointerDepth) targetKey =
            (target, compiledField.TypeSpelling, compiledField.PointerDepth);
        if (target != 0 && !state.ActivePointerTargets.Add(targetKey))
        {
            throw new CStructReadException("Cyclic pointer target detected at stream address " + target + ".");
        }

        state.PointerDereferenceDepth++;
        try
        {
            if (valueSegmentIndex == segments.Count - 1)
            {
                return this.CreatePointerValueTarget(
                    compiledField,
                    target,
                    compiledField.PointerDepth - 1,
                    selection,
                    state,
                    context);
            }

            if (target == 0)
            {
                throw new CStructPathException("Cannot traverse through a null pointer target.");
            }

            if (compiledField.PointerDepth > 1)
            {
                PathSegment next = segments[valueSegmentIndex + 1];
                if (next.Indexes.Count > 0)
                {
                    throw new CStructPathException("Pointer accessors cannot have array indexes.");
                }

                if (string.Equals(next.Name, "address", StringComparison.Ordinal))
                {
                    if (valueSegmentIndex + 1 != segments.Count - 1)
                    {
                        throw new CStructPathException("Pointer .address must be the terminal path segment.");
                    }

                    CompiledField remainingAddressField =
                        this.CreatePointerTargetCompiledField(compiledField, compiledField.PointerDepth - 1);
                    return ResolvedTarget.PointerAddress(remainingAddressField, target, this.PointerSize, selection, state.StructureDepth, context);
                }

                if (!string.Equals(next.Name, "value", StringComparison.Ordinal))
                {
                    throw new CStructPathException("Expected another pointer accessor for a multi-level pointer.");
                }

                CompiledField remainingField =
                    this.CreatePointerTargetCompiledField(compiledField, compiledField.PointerDepth - 1);
                return this.ResolvePointerTarget(
                    remainingField,
                    target,
                    segments,
                    valueSegmentIndex + 1,
                    state,
                    context,
                    selection);
            }

            if (compiledField.TargetComposite is { } targetStruct)
            {
                return this.ResolveTargetInStruct(
                    targetStruct,
                    target,
                    segments,
                    valueSegmentIndex + 1,
                    state,
                    context);
            }

            throw new CStructPathException("Cannot traverse beyond a scalar pointer target.");
        }
        finally
        {
            state.PointerDereferenceDepth--;
            if (target != 0)
            {
                state.ActivePointerTargets.Remove(targetKey);
            }
        }
    }

    /// <summary>
    ///     Creates a semantic target for an ordinary field, one fully selected array element (every dimension
    ///     indexed), or a partially indexed multidimensional sub-array. <paramref name="resolvedField"/>
    ///     is already peeled exactly once per supplied index by the caller, so it directly describes what this
    ///     target reads or writes - no further peeling happens here.
    /// </summary>
    private ResolvedTarget CreateFieldTarget(
        CompiledField resolvedField,
        long address,
        ArraySelection selection,
        int bitOffset,
        int bitStorageSize,
        CStructOperationContext state,
        TargetResolutionContext context)
    {
        if (resolvedField.Composite is not null)
        {
            state.EnsureStructureDepth(state.StructureDepth + 1);
        }

        return ResolvedTarget.Field(resolvedField, address, selection, bitOffset, bitStorageSize, state.StructureDepth, context);
    }

    /// <summary>Creates a target for the storage reached by one or more contextual <c>.value</c> accessors.</summary>
    private ResolvedTarget CreatePointerValueTarget(
        CompiledField compiledField,
        long address,
        int remainingPointerDepth,
        ArraySelection selection,
        CStructOperationContext state,
        TargetResolutionContext context)
    {
        CompiledField writableCompiledField =
            this.CreatePointerTargetCompiledField(compiledField, remainingPointerDepth);
        if (remainingPointerDepth == 0 && compiledField.TargetComposite is not null)
        {
            state.EnsureStructureDepth(state.StructureDepth + 1);
        }

        return ResolvedTarget.PointerValue(compiledField, writableCompiledField, address, remainingPointerDepth, selection, state.StructureDepth, context);
    }

    /// <summary>Builds the exact compiled writable field remaining after explicit pointer dereferences.</summary>
    private CompiledField CreatePointerTargetCompiledField(CompiledField field, int remainingPointerDepth)
    {
        bool isTerminatedTarget = remainingPointerDepth == 0 && field.HasTerminatedCodec;
        string? terminatedCodec = isTerminatedTarget
                                      ? CharacterFieldTypes.GetStringPointerHandlerKey(field.EffectiveField.Type)
                                      : null;
        return field.SelectPointerTarget(
            remainingPointerDepth,
            terminatedCodec,
            this.PointerSize);
    }

    /// <summary>Reads one encoded pointer address and applies the selected absolute/relative addressing mode.</summary>
    private long ReadPointerTargetAddress(long pointerStorage, CStructOperationContext state)
    {
        state.Stream.Position = pointerStorage;
        long storedAddress = this.ReadPointerAddress(state);
        if (storedAddress == 0)
        {
            return 0;
        }

        long target;
        try
        {
            target = CStructPointerArithmetic.ResolveTargetAddress(
                storedAddress,
                state.AddressingMode,
                state.PointerOrigin);
        }
        catch (OverflowException exception)
        {
            throw new CStructPathException("Relative pointer target overflowed the stream address range.", exception);
        }

        if (storedAddress != 0 && (target < 0 || target >= state.Stream.Length))
        {
            throw new CStructReadException("Pointer target is outside the readable stream range: " + target);
        }

        return target;
    }

    /// <summary>
    ///     Returns one selected array element's start at the current dimension, measuring prior variable-size
    ///     elements when necessary. A caller addressing an N-dimensional array calls this once per
    ///     supplied index, against the shape remaining after each prior call's own <see cref="CompiledField.SelectArrayElement"/>
    ///     peel - the same "repeat the existing single-dimension operation once per dimension" mechanism every
    ///     other N-D consumer uses.
    /// </summary>
    /// <param name="compiledField">The array shape remaining at the current dimension.</param>
    /// <param name="fieldStart">The absolute byte address of this array or sub-array.</param>
    /// <param name="index">The zero-based element index, already checked against the dimension's count.</param>
    /// <param name="state">The input cursor, read limits and captured layout variables used while measuring.</param>
    /// <returns>The selected element's absolute byte address.</returns>
    /// <exception cref="CStructReadException">A preceding element cannot be read within the input or read limits.</exception>
    /// <exception cref="OverflowException">The selected extent cannot fit in a signed stream coordinate.</exception>
    private long GetArrayElementStart(
        CompiledField compiledField,
        long fieldStart,
        int index,
        CStructOperationContext state)
    {
        if (index == 0)
        {
            return fieldStart;
        }

        // The peeled shape's own storage size is exactly the stride for one step at the current dimension: for a
        // 1-D array this is unchanged (peeling reaches Scalar directly, whose storage size is the plain element
        // size), and for a multidimensional array it is the size of the whole remaining sub-array - a known
        // per-element size means every element, including any nested struct's own fields, has a statically fixed
        // layout with no runtime-dependent count or size, so the selected element's start is one multiplication
        // instead of a per-element walk. This is also safe with respect to layout-variable capture: resolving one
        // array element never continues on to a later sibling field of the containing struct (the caller returns
        // or recurses into the selected element as soon as this method returns), and nothing inside a statically
        // fixed-size element can itself depend on a captured variable. So skipping the walk over the preceding
        // elements cannot omit a variable capture that anything still to be resolved needs.
        CompiledField elementField = compiledField.SelectArrayElement();
        if (elementField.FixedStorageSize is int fixedElementStride)
        {
            return checked(fieldStart + ((long)fixedElementStride * index));
        }

        if (compiledField.Composite is { } nested)
        {
            // Advancing one index at this dimension skips exactly the number of leaf structs one sub-array
            // element contains - 1 for every 1-D case (unchanged, since a peeled 1-D shape is Scalar, whose own
            // total element count is 1), or the peeled shape's own total for a multidimensional one.
            int leavesPerStep = elementField.Array.TotalFixedElementCount ?? 1;
            long current = fieldStart;
            int totalLeavesToSkip = checked(index * leavesPerStep);
            for (int i = 0; i < totalLeavesToSkip; i++)
            {
                current = this.MeasureStructEnd(nested, current, state);
            }

            return current;
        }

        if (compiledField.Codec.IsLeb128 || compiledField.Codec.IsTerminatedText || (compiledField.Codec.IsCustom && !compiledField.FixedElementSize.HasValue))
        {
            state.Stream.Position = fieldStart;
            int leaves = checked(index * (elementField.Array.TotalFixedElementCount ?? 1));
            for (int i = 0; i < leaves; i++)
            {
                _ = this.codecs.ReaderOf(compiledField)!(state.Stream);
            }

            return state.Stream.Position;
        }

        int elementSize = this.compiledSizeQueries.GetCompiledFieldElementSize(compiledField, state.Variables, false);
        return checked(fieldStart + ((long)elementSize * index));
    }

    /// <summary>Evaluates one fixed array count and rejects it before traversal can loop over excessive elements.</summary>
    private int GetBoundedArrayCount(CompiledField field, CStructOperationContext state, long fieldStart)
    {
        if (field.Array.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
        {
            return this.CountDataSizedElements(field, state, fieldStart);
        }

        int count = this.compiledSizeQueries.GetCompiledArrayCount(field, state.Variables, false);
        if (count > state.MaxArrayElements)
        {
            throw new CStructReadLimitException(
                ReadFailures.ArrayLengthLimit(count, state.MaxArrayElements));
        }

        return count;
    }

    /// <summary>
    ///     Evaluates the total leaf element count across every dimension of a (possibly multidimensional)
    ///     Array and rejects it before a per-leaf walk can loop over excessive elements. Unlike
    ///     <see cref="GetBoundedArrayCount"/> (the current/outermost dimension's own count, used for per-dimension
    ///     bounds checks), this is the flat row-major leaf count a full measurement walk must actually visit -
    ///     the two coincide for every 1-D field, since a 1-D shape's only dimension is both.
    /// </summary>
    private int GetBoundedTotalElementCount(CompiledField field, CStructOperationContext state, long fieldStart)
    {
        if (field.Array.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
        {
            return this.CountDataSizedElements(field, state, fieldStart);
        }

        int count = this.compiledSizeQueries.GetCompiledFieldTotalElementCount(field, state.Variables, false);
        if (count > state.MaxArrayElements)
        {
            throw new CStructReadLimitException(
                ReadFailures.ArrayLengthLimit(count, state.MaxArrayElements));
        }

        return count;
    }

    /// <summary>The element count of a data-sized array at a known start, restoring the stream position afterwards.</summary>
    private int CountDataSizedElements(CompiledField field, CStructOperationContext state, long fieldStart)
    {
        int elementSize = field.FixedElementSize ??
                          throw new InvalidOperationException("Data-sized array has no fixed element size: " + field.Name);
        long position = state.Stream.Position;
        try
        {
            return field.Array.Kind == CompiledArrayKind.ToEnd
                       ? DynamicArrayExtent.CountToEnd(state.Stream, fieldStart, elementSize, state.MaxArrayElements, field.Name)
                       : DynamicArrayExtent.CountTerminated(state.Stream, fieldStart, elementSize, state.MaxArrayElements, field.Name);
        }
        finally
        {
            state.Stream.Position = position;
        }
    }

    /// <summary>Measures one complete field without decoding unrelated pointer targets.</summary>
    /// <param name="compiledField">The field whose scalar or complete array extent is needed.</param>
    /// <param name="fieldStart">The absolute byte address at which the field begins.</param>
    /// <param name="state">The input cursor, read limits and captured variables; variable-size values are read.</param>
    /// <returns>The absolute byte address immediately after the field, including its terminators.</returns>
    /// <exception cref="CStructReadException">A value cannot be read within the input or read limits.</exception>
    /// <exception cref="OverflowException">The complete field extent cannot fit in a signed stream coordinate.</exception>
    private long MeasureFieldEnd(CompiledField compiledField, long fieldStart, CStructOperationContext state)
    {
        // A pointer to a struct occupies the pointer width, never the pointee's extent: measuring the pointee here
        // placed every sibling after `item *p` at the wrong offset and recursed until the nesting limit for a
        // self-referential `node *next`. Pointer fields fall through to the fixed-size arithmetic below.
        if (compiledField.Composite is { } nested)
        {
            int count = compiledField.Array.Kind == CompiledArrayKind.Scalar
                            ? 1
                            : this.GetBoundedTotalElementCount(compiledField, state, fieldStart);

            // Unlike GetArrayElementStart, this walk cannot be replaced by FixedElementSize * count even when the
            // element size is statically known: a later sibling field of the containing struct is still to be
            // resolved after this call returns, and MeasureStructEnd's per-field CaptureLayoutVariable calls may
            // expose a scalar from inside one of these elements (by its bare field name) that a later field's
            // runtime array-count expression depends on. Skipping elements here could silently drop that capture.
            long current = fieldStart;
            string? outerPrefix = state.QualifiedPrefix;
            if (compiledField.HasQualifiedPrefix && compiledField.Array.Kind == CompiledArrayKind.Scalar)
            {
                // A field named through a dotted path (`hdr.n`) republishes its nested values under the prefix.
                state.QualifiedPrefix = outerPrefix is null ? compiledField.QualifiedPrefix : outerPrefix + compiledField.QualifiedPrefix;
            }

            for (int i = 0; i < count; i++)
            {
                current = this.MeasureStructEnd(nested, current, state);
            }

            state.QualifiedPrefix = outerPrefix;
            return compiledField.Array.Kind == CompiledArrayKind.Terminated
                       ? checked(current + (compiledField.FixedElementSize ?? 0))
                       : current;
        }

        if (compiledField.Array.Kind == CompiledArrayKind.Scalar && compiledField.Codec.IsTerminatedText)
        {
            state.Stream.Position = fieldStart;
            _ = this.codecs.ReaderOf(compiledField)?.Invoke(state.Stream) ??
                throw new InvalidOperationException(
                    "Compiled named string has no reader: " + compiledField.Name);
            return state.Stream.Position;
        }

        if (compiledField.Array.Kind == CompiledArrayKind.Flexible)
        {
            if (!compiledField.IsCharacterArray)
            {
                throw new CStructLayoutException(
                    "Only character fields can use an unsized array declarator: " + compiledField.Name);
            }

            state.Stream.Position = fieldStart;
            _ = this.codecs.TerminatedReaderOf(compiledField)?.Invoke(state.Stream) ??
                throw new InvalidOperationException(
                    "Compiled unsized character array has no reader: " + compiledField.Name);
            return state.Stream.Position;
        }

        if (compiledField.Codec.IsLeb128 || compiledField.Codec.IsTerminatedText || (compiledField.Codec.IsCustom && !compiledField.FixedElementSize.HasValue))
        {
            // Variable-length integers, terminated strings and custom codecs are measured by their readers.
            int count = compiledField.Array.Kind == CompiledArrayKind.Scalar ? 1 : this.GetBoundedTotalElementCount(compiledField, state, fieldStart);
            state.Stream.Position = fieldStart;
            for (int i = 0; i < count; i++)
            {
                _ = this.codecs.ReaderOf(compiledField)!(state.Stream);
            }

            return state.Stream.Position;
        }

        int scalarCount = compiledField.Array.Kind == CompiledArrayKind.Scalar
                              ? 1
                              : this.GetBoundedTotalElementCount(compiledField, state, fieldStart);
        int elementSize = compiledField.FixedElementSize ??
                          this.compiledSizeQueries.GetCompiledFieldElementSize(compiledField, state.Variables, false);
        if (compiledField.Array.Kind == CompiledArrayKind.Terminated)
        {
            // The terminator element is part of the field's extent.
            scalarCount = CountStoredTerminatedElements(scalarCount);
        }

        int storageSize = checked(elementSize * scalarCount);
        return checked(fieldStart + storageSize);
    }

    /// <summary>Measures a potentially runtime-sized nested struct and captures scalar variables in declaration order.</summary>
    private long MeasureStructEnd(CompiledCompositeType strct, long structStart, CStructOperationContext state)
    {
        state.EnterStructure();
        try
        {
            return this.MeasureStructEndCore(strct, structStart, state);
        }
        finally
        {
            state.ExitStructure();
        }
    }

    /// <summary>Measures one structure whose nesting budget has already been claimed.</summary>
    private long MeasureStructEndCore(CompiledCompositeType composite, long structStart, CStructOperationContext state)
    {
        if (composite.IsUnion)
        {
            this.ValidateCompositeTraversalLimits(composite, state);
            return checked(
                structStart +
                this.compiledSizeQueries.GetCompiledStructSizeInBytes(composite, state.Variables, false));
        }

        var cursor = new CompositeFieldPlacementCursor(structStart, this.Aligned, this.BitfieldPacking, this.highBitFirst);

        var variableScope = composite.HasDirectConditionalFields ? new ConditionalVariableScope(composite, state.Variables) : null;
        var selection = composite.HasDirectConditionalFields ? new ConditionalFieldSelection(this.layoutExpressionEvaluator, composite.ConditionalGroupCount) : null;
        foreach (CompiledField compiledField in composite.Fields)
        {
            if (selection?.IsActive(compiledField, state.Variables) == false)
            {
                continue;
            }

            (long fieldStart, int bitOffset, int unitSize) = cursor.AdvanceToField(compiledField);
            if (compiledField.IsZeroWidthBitfield)
            {
                continue;
            }

            this.CaptureLayoutVariable(compiledField, fieldStart, bitOffset, unitSize, state);
            if (compiledField.BitSize == 0)
            {
                cursor.CompleteField(this.MeasureFieldEnd(compiledField, fieldStart, state));
            }

            variableScope?.CompleteField(compiledField, state.Variables);
        }

        return cursor.FinishComposite(composite.Symbol.Alignment);
    }

    /// <summary>
    ///     Validates array and nesting work hidden inside a fixed-size composite without reading bytes solely to
    ///     calculate an already compiled extent.
    /// </summary>
    private void ValidateCompositeTraversalLimits(CompiledCompositeType composite, CStructOperationContext state)
    {
        foreach (CompiledField compiledField in composite.Fields)
        {
            // A data-sized array's count is not known without a position; its element type's own limits are still
            // checked once through the element walk that follows a real resolution.
            int count = compiledField.Array.Kind is CompiledArrayKind.Scalar or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated
                            ? 1
                            : this.GetBoundedArrayCount(compiledField, state, 0);
            if (count == 0 || compiledField.Composite is not { } nested)
            {
                continue;
            }

            state.EnterStructure();
            try
            {
                this.ValidateCompositeTraversalLimits(nested, state);
            }
            finally
            {
                state.ExitStructure();
            }
        }
    }

    /// <summary>Captures a field whose value the resolver does not read: only a non-integer field reaches here, and its capture makes the name unusable.</summary>
    /// <param name="compiledField">The field.</param>
    /// <param name="captures">Whether any expression can name the field.</param>
    /// <param name="state">The operation state.</param>
    private void CaptureWithoutValue(CompiledField compiledField, bool captures, CStructOperationContext state)
    {
        if (captures)
        {
            LayoutVariableCapture.Capture(state.Variables, compiledField.Name, compiledField, null);
            state.PublishQualified(compiledField.Name);
        }
    }

    /// <summary>Reads one preceding scalar into the expression environment without following pointer targets.</summary>
    private void CaptureLayoutVariable(
        CompiledField compiledField,
        long fieldStart,
        int bitOffset,
        int unitSize,
        CStructOperationContext state)
    {
        // An unreferenced field still moves the stream exactly as before - reported failure offsets depend on
        // it - but publishes nothing.
        bool captures = compiledField.CapturesLayoutVariable || state.CaptureAllLayoutVariables;

        // Arrays and fixed-point values are never read here; like every value that is not an integer, their capture
        // only makes a shared name unusable, exactly as the reader's does.
        if (compiledField.Array.Kind != CompiledArrayKind.Scalar || (compiledField.PointerDepth == 0 && compiledField.IsFixedPoint))
        {
            this.CaptureWithoutValue(compiledField, captures, state);
            return;
        }

        object value;
        state.Stream.Position = fieldStart;

        if (compiledField.PointerDepth > 0)
        {
            value = this.ReadPointerAddress(state);
        }
        else if (compiledField.Enum is { } enm)
        {
            value = this.codecs.ReaderOf(compiledField)?.Invoke(state.Stream) ??
                    throw new InvalidOperationException(
                        "Compiled enum has no storage reader: " + enm.Name);
            if (captures)
            {
                LayoutVariableCapture.Capture(state.Variables, compiledField.Name, compiledField, enm.Integer.FromStorageValue(value));
                state.PublishQualified(compiledField.Name);
            }

            return;
        }
        else if (compiledField.Composite is not null || this.codecs.ReaderOf(compiledField) is not Func<Stream, object> reader)
        {
            this.CaptureWithoutValue(compiledField, captures, state);
            return;
        }
        else if (compiledField.BitSize > 0 && unitSize != compiledField.Codec.Size)
        {
            // A packed SysV window: read the placed unit rather than the declared type.
            value = BinaryPrimitiveIO.ReadBitfieldUnit(state.Stream, unitSize, compiledField.BitStorageIsLittleEndian ?? true);
        }
        else
        {
            value = reader(state.Stream);
        }

        if (!captures)
        {
            return;
        }

        if (compiledField.BitSize > 0)
        {
            int unitBits = checked(unitSize * 8);
            value = BitfieldCodecTable.ExtractBitfieldValue(value, BitfieldCodecTable.EffectiveShift(bitOffset, compiledField.BitSize, unitBits, this.highBitFirst), compiledField.BitSize);
        }

        // A parsed scalar shadows any caller/define value with the same spelling. Keeping the older value would
        // resolve a path against data contradicted by the stream. See LayoutVariableCapture for the exact rule.
        LayoutVariableCapture.Capture(state.Variables, compiledField.Name, compiledField, value);

        state.PublishQualified(compiledField.Name);
    }

    /// <summary>Includes the all-zero terminator in a data-sized array's stored element count.</summary>
    /// <param name="valueCount">The nonnegative count of values before the terminator.</param>
    /// <returns>The number of stored elements, including the terminator.</returns>
    /// <exception cref="OverflowException">The stored count exceeds the Int32 element-count domain.</exception>
    internal static int CountStoredTerminatedElements(int valueCount) => checked(valueCount + 1);
}
