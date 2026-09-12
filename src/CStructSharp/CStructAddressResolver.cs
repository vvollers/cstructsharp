namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CStructSharp.Structure;
using CstructEnum = CStructSharp.Structure.Enum;

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
            throw new CStructPathException("Unknown root element: " + segments[0].Name);
        }

        long rootStart = stream.Position;
        CStructElement declaredRoot = root;
        CStructElement? resolvedRoot = this.compiledModelQueries.ResolveCompiledNamedElement(root);
        if (segments.Count == 1)
        {
            CStructElement targetElement = resolvedRoot ?? declaredRoot;
            Struct? rootTargetStruct = targetElement as Struct;
            if (rootTargetStruct is not null)
            {
                state.EnsureStructureDepth(1);
            }

            int? fixedSize = rootTargetStruct is null
                                 ? null
                                 : this.TryGetStructFixedSize(rootTargetStruct, state.Variables);
            int alignment = rootTargetStruct is null
                                ? 1
                                : this.compiledSizeQueries.GetCompiledComposite(rootTargetStruct).Symbol.Alignment;
            return new ResolvedTarget(
                rootStart,
                ResolvedTargetKind.Root,
                null,
                null,
                null,
                targetElement,
                new CStructElement[] { targetElement, },
                targetElement.Name.Name,
                false,
                null,
                null,
                Array.Empty<int>(),
                0,
                0,
                rootTargetStruct?.IsUnion == true ? rootStart : null,
                rootTargetStruct?.IsUnion == true ? fixedSize : null,
                null,
                null,
                0,
                0,
                alignment,
                fixedSize,
                0);
        }

        if (resolvedRoot is not Struct rootStruct)
        {
            throw new CStructPathException("Root path cannot contain child segments: " + segments[0].Name);
        }

        var context = new TargetResolutionContext(new CStructElement[] { rootStruct, }, Array.Empty<int>());

        return this.ResolveTargetInStruct(rootStruct, rootStart, segments, 1, state, context);
    }

    /// <summary>Finds a requested child while measuring only fields that precede it.</summary>
    private ResolvedTarget ResolveTargetInStruct(
        Struct strct,
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
        Struct strct,
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

        if (strct.IsUnion)
        {
            context = context.EnterUnion(
                structStart,
                this.compiledSizeQueries.GetCompiledStructSizeInBytes(
                    this.compiledSizeQueries.GetCompiledComposite(strct),
                    state.Variables,
                    false));
        }

        PathSegment requested = segments[pathIndex];
        if (strct.IsUnion)
        {
            CompiledField compiledUnionField = this.FindCompiledField(strct, requested.Name);
            Field effectiveUnionField = compiledUnionField.EffectiveField;
            int bitStorageSize = effectiveUnionField.BitSize > 0
                                     ? compiledUnionField.BitStorageSize ??
                                       throw new InvalidOperationException(
                                           "Compiled bitfield has no storage size: " + effectiveUnionField.Name.Name)
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

        CompiledCompositeType composite = this.compiledSizeQueries.GetCompiledComposite(strct);
        var variableScope = new ConditionalVariableScope(composite, state.Variables);
        var selection = new ConditionalFieldSelection(this.layoutExpressionEvaluator);
        var cursor = new CompositeFieldPlacementCursor(structStart, this.Aligned);

        foreach (CompiledField compiledField in composite.Fields)
        {
            if (!selection.IsActive(compiledField, state.Variables))
            {
                continue;
            }

            Field declaredField = compiledField.Declaration;
            Field field = compiledField.EffectiveField;
            (long fieldStart, int bitOffset) = cursor.AdvanceToField(compiledField);
            this.ValidateOffsetAssertionAtRuntime(compiledField, fieldStart, state.Variables);

            if (string.Equals(declaredField.Name.Name, requested.Name, StringComparison.Ordinal))
            {
                int selectedBitStorageSize = field.BitSize > 0
                                                 ? compiledField.BitStorageSize ??
                                                   throw new InvalidOperationException(
                                                       "Compiled bitfield has no storage size: " + field.Name.Name)
                                                 : 0;
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

            // An anonymous promoted member (LANG-14) consumes no path segment of its own - retry the same
            // segment/pathIndex against its own (recursively promoted) fields before giving up. The pre-check
            // below is a pure, side-effect-free tree walk, so a miss costs nothing and a hit is guaranteed to
            // succeed (construction already rejected any flattened-namespace collision), meaning a genuine
            // downstream error inside the matched field can never be misreported as "unknown field" here.
            if (composite.PromotedFields.Contains(compiledField) &&
                declaredField is Struct promotedStruct &&
                this.TryFindCompiledField(promotedStruct, requested.Name, out _))
            {
                return this.ResolveTargetInStruct(promotedStruct, fieldStart, segments, pathIndex, state, context);
            }

            this.CaptureLayoutVariable(compiledField, fieldStart, bitOffset, state);
            if (field.BitSize == 0)
            {
                cursor.CompleteField(this.MeasureFieldEnd(compiledField, fieldStart, state));
            }

            variableScope.CompleteField(compiledField, state.Variables);
        }

        throw new CStructPathException($"Unknown field '{requested.Name}' in '{strct.Name.Name}'.");
    }

    /// <summary>
    ///     Resolves array selection, nested structures, and contextual pointer accessors for one field. An
    ///     N-dimensional array (LANG-05) peels one dimension per supplied index, exactly mirroring a single
    ///     dimension's own bounds-check-then-advance step (ADR-016 decision 5); supplying fewer indices than the
    ///     field has dimensions leaves the target array-shaped, selecting the corresponding lower-dimensional
    ///     sub-array (ADR-016 decision 4) rather than one scalar/struct element.
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
        Field declaredField = compiledField.Declaration;
        PathSegment segment = segments[pathIndex];
        bool declaredIsArray = compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime;

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
            int dimensionCount = this.GetBoundedArrayCount(resolvedField, state);
            if (suppliedIndex >= dimensionCount)
            {
                throw new CStructPathException(
                    $"Array index {suppliedIndex} is out of range for {segment.Name} with length {dimensionCount}.");
            }

            elementStart = this.GetArrayElementStart(resolvedField, elementStart, suppliedIndex, state);
            resolvedField = resolvedField.SelectArrayElement();
        }

        bool remainingIsArray = resolvedField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime;
        int? arrayLength = remainingIsArray ? this.GetBoundedArrayCount(resolvedField, state) : null;
        int? selectedArrayIndex = segment.Indexes.Count > 0 && !remainingIsArray ? segment.Indexes[^1] : null;

        context = context.EnterField(declaredField, segment.Indexes);

        if (remainingIsArray && pathIndex + 1 < segments.Count)
        {
            throw new CStructPathException("An array index is required before traversing: " + segment.Name);
        }

        if (pathIndex == segments.Count - 1)
        {
            return this.CreateFieldTarget(
                resolvedField,
                elementStart,
                declaredIsArray,
                selectedArrayIndex,
                bitOffset,
                bitStorageSize,
                arrayLength,
                state,
                context);
        }

        Field field = resolvedField.EffectiveField;
        CStructElement? namedElement = resolvedField.NamedElement;
        PathSegment next = segments[pathIndex + 1];
        if (field.PointerDepth > 0)
        {
            if (string.Equals(next.Name, "address", StringComparison.Ordinal))
            {
                if (next.Indexes.Count > 0 || pathIndex + 1 != segments.Count - 1)
                {
                    throw new CStructPathException("Pointer .address must be the terminal path segment.");
                }

                return this.CreatePointerAddressTarget(
                    resolvedField,
                    elementStart,
                    declaredIsArray,
                    arrayLength,
                    selectedArrayIndex,
                    state,
                    context);
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
                declaredIsArray,
                arrayLength,
                selectedArrayIndex);
        }

        if (namedElement is Struct nestedStruct)
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
        bool isArray,
        int? arrayLength,
        int? selectedArrayIndex)
    {
        Field field = compiledField.EffectiveField;
        CStructElement? namedElement = compiledField.NamedElement;
        if (!state.DereferencePointers)
        {
            throw new CStructPathException("Pointer dereference is disabled for the selected path.");
        }

        if (state.PointerDereferenceDepth >= state.MaxPointerDepth)
        {
            throw new CStructReadLimitException("Maximum pointer dereference depth exceeded.");
        }

        this.EnsurePointerTargetSize(
            field.PointerDepth,
            compiledField,
            state);

        long target = this.ReadPointerTargetAddress(pointerStorage, state);
        context = context.FollowPointer(pointerStorage, target);
        (long Address, string TypeName, int PointerDepth) targetKey =
            (target, field.Type.Name, field.PointerDepth);
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
                    field.PointerDepth - 1,
                    isArray,
                    arrayLength,
                    selectedArrayIndex,
                    state,
                    context);
            }

            if (target == 0)
            {
                throw new CStructPathException("Cannot traverse through a null pointer target.");
            }

            if (field.PointerDepth > 1)
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
                        this.CreatePointerTargetCompiledField(compiledField, field.PointerDepth - 1);
                    return this.CreatePointerAddressTarget(
                        remainingAddressField,
                        target,
                        isArray,
                        arrayLength,
                        selectedArrayIndex,
                        state,
                        context);
                }

                if (!string.Equals(next.Name, "value", StringComparison.Ordinal))
                {
                    throw new CStructPathException("Expected another pointer accessor for a multi-level pointer.");
                }

                CompiledField remainingField =
                    this.CreatePointerTargetCompiledField(compiledField, field.PointerDepth - 1);
                return this.ResolvePointerTarget(
                    remainingField,
                    target,
                    segments,
                    valueSegmentIndex + 1,
                    state,
                    context,
                    isArray,
                    arrayLength,
                    selectedArrayIndex);
            }

            if (namedElement is Struct targetStruct)
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
    ///     indexed), or a partially indexed multidimensional sub-array (LANG-05). <paramref name="resolvedField"/>
    ///     is already peeled exactly once per supplied index by the caller, so it directly describes what this
    ///     target reads or writes - no further peeling happens here.
    /// </summary>
    private ResolvedTarget CreateFieldTarget(
        CompiledField resolvedField,
        long address,
        bool isArray,
        int? selectedArrayIndex,
        int bitOffset,
        int bitStorageSize,
        int? arrayLength,
        CStructOperationContext state,
        TargetResolutionContext context)
    {
        Field field = resolvedField.EffectiveField;
        CStructElement? namedElement = resolvedField.NamedElement;
        int alignment = resolvedField.Alignment;

        // The peeled shape's own precomputed storage size is already correct for every case (unindexed, a
        // multidimensional sub-array, or one fully selected element), since every dimension beyond the outermost
        // is always fixed (Seam 4) and TotalFixedElementCount already accounts for all of them. Only a genuinely
        // 1-D runtime/flexible-count array (the one shape whose static storage size is never known) falls back to
        // multiplying the leaf element size by this specific target's own runtime-evaluated length.
        int? fixedSize = resolvedField.FixedStorageSize ??
                         (resolvedField.FixedElementSize.HasValue
                              ? checked(resolvedField.FixedElementSize.Value * (arrayLength ?? 1))
                              : null);
        long? unionStorageAddress = context.UnionStorageAddress;
        int? unionStorageSize = context.UnionStorageSize;
        if (namedElement is Struct { IsUnion: true, } union)
        {
            unionStorageAddress = address;
            unionStorageSize = this.compiledSizeQueries.GetCompiledComposite(union).Symbol.FixedSize;
        }

        if (field.PointerDepth == 0 && namedElement is Struct)
        {
            state.EnsureStructureDepth(state.StructureDepth + 1);
        }

        return new ResolvedTarget(
            address,
            selectedArrayIndex.HasValue ? ResolvedTargetKind.ArrayElement : ResolvedTargetKind.Field,
            resolvedField.Declaration,
            field,
            field,
            namedElement,
            context.DebugPrefix,
            resolvedField.CodecName,
            isArray,
            arrayLength,
            selectedArrayIndex,
            context.SelectedIndexes,
            bitOffset,
            bitStorageSize,
            unionStorageAddress,
            unionStorageSize,
            field.PointerDepth > 0 ? address : context.PointerStorageAddress,
            context.PointerTargetAddress,
            context.PointerAccessorsConsumed,
            field.PointerDepth,
            alignment,
            fixedSize,
            state.StructureDepth,
            resolvedField,
            resolvedField);
    }

    /// <summary>Creates a target for pointer storage selected by a contextual <c>.address</c> accessor.</summary>
    private ResolvedTarget CreatePointerAddressTarget(
        CompiledField compiledField,
        long address,
        bool isArray,
        int? arrayLength,
        int? selectedArrayIndex,
        CStructOperationContext state,
        TargetResolutionContext context)
    {
        Field field = compiledField.EffectiveField;
        CStructElement? namedElement = compiledField.NamedElement;
        return new ResolvedTarget(
            address,
            ResolvedTargetKind.PointerAddress,
            compiledField.Declaration,
            field,
            null,
            namedElement,
            context.DebugPrefix,
            "pointer",
            isArray,
            arrayLength,
            selectedArrayIndex,
            context.SelectedIndexes,
            0,
            0,
            context.UnionStorageAddress,
            context.UnionStorageSize,
            address,
            context.PointerTargetAddress,
            context.PointerAccessorsConsumed,
            field.PointerDepth,
            this.PointerSize,
            this.PointerSize,
            state.StructureDepth,
            compiledField,
            null);
    }

    /// <summary>Creates a target for the storage reached by one or more contextual <c>.value</c> accessors.</summary>
    private ResolvedTarget CreatePointerValueTarget(
        CompiledField compiledField,
        long address,
        int remainingPointerDepth,
        bool isArray,
        int? arrayLength,
        int? selectedArrayIndex,
        CStructOperationContext state,
        TargetResolutionContext context)
    {
        Field field = compiledField.EffectiveField;
        CStructElement? namedElement = compiledField.NamedElement;
        CompiledField writableCompiledField =
            this.CreatePointerTargetCompiledField(compiledField, remainingPointerDepth);
        Field writableField = writableCompiledField.EffectiveField;
        int alignment = writableCompiledField.Alignment;
        int? fixedSize = writableCompiledField.FixedElementSize;
        long? unionStorageAddress = context.UnionStorageAddress;
        int? unionStorageSize = context.UnionStorageSize;
        if (remainingPointerDepth == 0 && namedElement is Struct { IsUnion: true, } union)
        {
            unionStorageAddress = address;
            unionStorageSize = this.compiledSizeQueries.GetCompiledComposite(union).Symbol.FixedSize;
        }

        if (remainingPointerDepth == 0 && namedElement is Struct)
        {
            state.EnsureStructureDepth(state.StructureDepth + 1);
        }

        return new ResolvedTarget(
            address,
            ResolvedTargetKind.PointerValue,
            compiledField.Declaration,
            field,
            writableField,
            namedElement,
            context.DebugPrefix,
            writableCompiledField.CodecName,
            isArray,
            arrayLength,
            selectedArrayIndex,
            context.SelectedIndexes,
            0,
            0,
            unionStorageAddress,
            unionStorageSize,
            context.PointerStorageAddress,
            address,
            context.PointerAccessorsConsumed,
            remainingPointerDepth,
            alignment,
            fixedSize,
            state.StructureDepth,
            compiledField,
            writableCompiledField);
    }

    /// <summary>Builds the exact compiled writable field remaining after explicit pointer dereferences.</summary>
    private CompiledField CreatePointerTargetCompiledField(CompiledField field, int remainingPointerDepth)
    {
        bool isTerminatedTarget = remainingPointerDepth == 0 && field.TerminatedReader is not null;
        string? terminatedCodec = isTerminatedTarget
                                      ? CharacterFieldTypes.GetStringPointerHandlerKey(field.EffectiveField.Type)
                                      : null;
        return field.SelectPointerTarget(
            remainingPointerDepth,
            terminatedCodec,
            field.TerminatedReader,
            field.TerminatedWriter,
            this.PointerSize);
    }

    /// <summary>Returns a struct's fixed extent, or null when a runtime-sized member prevents compilation of one.</summary>
    private int? TryGetStructFixedSize(Struct strct, IReadOnlyDictionary<string, Expr> variables)
    {
        try
        {
            return this.compiledSizeQueries.GetCompiledStructSizeInBytes(
                this.compiledSizeQueries.GetCompiledComposite(strct),
                variables,
                true);
        }
        catch (CStructLayoutException)
        {
            return null;
        }
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
    ///     Returns one selected array element's start at the current dimension, measuring prior dynamic struct
    ///     elements when necessary. A caller addressing an N-dimensional array (LANG-05) calls this once per
    ///     supplied index, against the shape remaining after each prior call's own <see cref="CompiledField.SelectArrayElement"/>
    ///     peel - the same "repeat the existing single-dimension operation once per dimension" mechanism every
    ///     other N-D consumer uses (ADR-016 decision 5).
    /// </summary>
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

        Field field = compiledField.EffectiveField;
        CStructElement? namedElement = compiledField.NamedElement;
        if (namedElement is Struct nested)
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

        if (Leb128Codec.IsType(compiledField.CodecName) && field.PointerDepth == 0)
        {
            state.Stream.Position = fieldStart;
            int leaves = checked(index * (elementField.Array.TotalFixedElementCount ?? 1));
            for (int i = 0; i < leaves; i++)
            {
                _ = compiledField.Reader!(state.Stream);
            }

            return state.Stream.Position;
        }

        int elementSize = this.compiledSizeQueries.GetCompiledFieldElementSize(compiledField, state.Variables, false);
        return checked(fieldStart + ((long)elementSize * index));
    }

    /// <summary>Evaluates one fixed array count and rejects it before traversal can loop over excessive elements.</summary>
    private int GetBoundedArrayCount(CompiledField field, CStructOperationContext state)
    {
        int count = this.compiledSizeQueries.GetCompiledArrayCount(field, state.Variables, false);
        if (count > state.MaxArrayElements)
        {
            throw new CStructReadLimitException(
                "Array length exceeds the configured limit: " + field.EffectiveField.Name.Name);
        }

        return count;
    }

    /// <summary>
    ///     Evaluates the total leaf element count across every dimension of a (possibly multidimensional,
    ///     LANG-05) array and rejects it before a per-leaf walk can loop over excessive elements. Unlike
    ///     <see cref="GetBoundedArrayCount"/> (the current/outermost dimension's own count, used for per-dimension
    ///     bounds checks), this is the flat row-major leaf count a full measurement walk must actually visit -
    ///     the two coincide for every 1-D field, since a 1-D shape's only dimension is both.
    /// </summary>
    private int GetBoundedTotalElementCount(CompiledField field, CStructOperationContext state)
    {
        int count = this.compiledSizeQueries.GetCompiledFieldTotalElementCount(field, state.Variables, false);
        if (count > state.MaxArrayElements)
        {
            throw new CStructReadLimitException(
                "Array length exceeds the configured limit: " + field.EffectiveField.Name.Name);
        }

        return count;
    }

    /// <summary>Measures one complete field without decoding unrelated pointer targets.</summary>
    private long MeasureFieldEnd(CompiledField compiledField, long fieldStart, CStructOperationContext state)
    {
        Field field = compiledField.EffectiveField;
        CStructElement? namedElement = compiledField.NamedElement;

        if (namedElement is Struct nested)
        {
            int count = compiledField.Array.Kind == CompiledArrayKind.Scalar
                            ? 1
                            : this.GetBoundedTotalElementCount(compiledField, state);

            // Unlike GetArrayElementStart, this walk cannot be replaced by FixedElementSize * count even when the
            // element size is statically known: a later sibling field of the containing struct is still to be
            // resolved after this call returns, and MeasureStructEnd's per-field CaptureLayoutVariable calls may
            // expose a scalar from inside one of these elements (by its bare field name) that a later field's
            // runtime array-count expression depends on. Skipping elements here could silently drop that capture.
            long current = fieldStart;
            for (int i = 0; i < count; i++)
            {
                current = this.MeasureStructEnd(nested, current, state);
            }

            return current;
        }

        if (compiledField.Array.Kind == CompiledArrayKind.Scalar &&
            field.PointerDepth == 0 &&
            PrimitiveCodecs.IsVariableLengthType(compiledField.CodecName))
        {
            state.Stream.Position = fieldStart;
            _ = compiledField.Reader?.Invoke(state.Stream) ??
                throw new InvalidOperationException(
                    "Compiled named string has no reader: " + field.Name.Name);
            return state.Stream.Position;
        }

        if (compiledField.Array.Kind == CompiledArrayKind.Flexible)
        {
            if (!CharacterFieldTypes.IsCharArrayField(field))
            {
                throw new CStructLayoutException(
                    "Only character fields can use an unsized array declarator: " + field.Name.Name);
            }

            state.Stream.Position = fieldStart;
            _ = compiledField.TerminatedReader?.Invoke(state.Stream) ??
                throw new InvalidOperationException(
                    "Compiled unsized character array has no reader: " + field.Name.Name);
            return state.Stream.Position;
        }

        if (Leb128Codec.IsType(compiledField.CodecName) && field.PointerDepth == 0)
        {
            int count = compiledField.Array.Kind == CompiledArrayKind.Scalar ? 1 : this.GetBoundedTotalElementCount(compiledField, state);
            state.Stream.Position = fieldStart;
            for (int i = 0; i < count; i++)
            {
                _ = compiledField.Reader!(state.Stream);
            }

            return state.Stream.Position;
        }

        int scalarCount = compiledField.Array.Kind == CompiledArrayKind.Scalar
                              ? 1
                              : this.GetBoundedTotalElementCount(compiledField, state);
        int elementSize = compiledField.FixedElementSize ??
                          this.compiledSizeQueries.GetCompiledFieldElementSize(compiledField, state.Variables, false);
        int storageSize = checked(elementSize * scalarCount);
        return checked(fieldStart + storageSize);
    }

    /// <summary>Measures a potentially runtime-sized nested struct and captures scalar variables in declaration order.</summary>
    private long MeasureStructEnd(Struct strct, long structStart, CStructOperationContext state)
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
    private long MeasureStructEndCore(Struct strct, long structStart, CStructOperationContext state)
    {
        if (strct.IsUnion)
        {
            this.ValidateCompositeTraversalLimits(strct, state);
            return checked(
                structStart +
                this.compiledSizeQueries.GetCompiledStructSizeInBytes(
                    this.compiledSizeQueries.GetCompiledComposite(strct),
                    state.Variables,
                    false));
        }

        var cursor = new CompositeFieldPlacementCursor(structStart, this.Aligned);

        var variableScope = new ConditionalVariableScope(this.compiledSizeQueries.GetCompiledComposite(strct), state.Variables);
        var selection = new ConditionalFieldSelection(this.layoutExpressionEvaluator);
        foreach (CompiledField compiledField in this.compiledSizeQueries.GetCompiledComposite(strct).Fields)
        {
            if (!selection.IsActive(compiledField, state.Variables))
            {
                continue;
            }

            Field field = compiledField.EffectiveField;
            (long fieldStart, int bitOffset) = cursor.AdvanceToField(compiledField);
            this.ValidateOffsetAssertionAtRuntime(compiledField, fieldStart, state.Variables);

            this.CaptureLayoutVariable(compiledField, fieldStart, bitOffset, state);
            if (field.BitSize == 0)
            {
                cursor.CompleteField(this.MeasureFieldEnd(compiledField, fieldStart, state));
            }

            variableScope.CompleteField(compiledField, state.Variables);
        }

        int structAlignment = this.compiledSizeQueries.GetCompiledComposite(strct).Symbol.Alignment;
        return cursor.FinishComposite(structAlignment);
    }

    /// <summary>
    ///     Validates array and nesting work hidden inside a fixed-size composite without reading bytes solely to
    ///     calculate an already compiled extent.
    /// </summary>
    private void ValidateCompositeTraversalLimits(Struct strct, CStructOperationContext state)
    {
        foreach (CompiledField compiledField in this.compiledSizeQueries.GetCompiledComposite(strct).Fields)
        {
            Field field = compiledField.EffectiveField;
            CStructElement? namedElement = compiledField.NamedElement;
            int count = compiledField.Array.Kind == CompiledArrayKind.Scalar
                            ? 1
                            : this.GetBoundedArrayCount(compiledField, state);
            if (count == 0 || field.PointerDepth > 0 || namedElement is not Struct nested)
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

    /// <summary>
    ///     Validates a field's runtime-resolved placement against its own <c>@N</c> offset assertion (LANG-15),
    ///     when present. Skips fields already validated eagerly at construction time by
    ///     <c>CStructCompiledModel.PlaceCompiledFields</c> - <see cref="CompiledField.FixedOffset"/> is exactly the
    ///     signal for "already checked," since it is set only when that pass could compute the offset statically.
    ///     This closes the gap for a field whose offset depends on an earlier runtime-length sibling, where no
    ///     static check was possible.
    /// </summary>
    private void ValidateOffsetAssertionAtRuntime(
        CompiledField compiledField,
        long fieldStart,
        IReadOnlyDictionary<string, Expr> variables)
    {
        Expr? assertion = compiledField.Declaration.OffsetAssertionExpression;
        if (assertion is null || compiledField.FixedOffset.HasValue)
        {
            return;
        }

        int asserted = this.layoutExpressionEvaluator.Evaluate(
            assertion,
            variables,
            "offset assertion for " + compiledField.Declaration.Name.Name);
        if (asserted < 0)
        {
            throw new CStructLayoutException(
                "Explicit offset assertion must be non-negative: " +
                compiledField.Declaration.Name.Name +
                " = " +
                asserted);
        }

        if (asserted != fieldStart)
        {
            throw new CStructLayoutException(
                $"Field '{compiledField.Declaration.Name.Name}' asserts offset {asserted} but computed offset is {fieldStart}.");
        }
    }

    /// <summary>Reads one preceding scalar into the expression environment without following pointer targets.</summary>
    private void CaptureLayoutVariable(
        CompiledField compiledField,
        long fieldStart,
        int bitOffset,
        CStructOperationContext state)
    {
        Field field = compiledField.EffectiveField;
        if (compiledField.Array.Kind != CompiledArrayKind.Scalar)
        {
            return;
        }

        if (field.PointerDepth == 0 && FixedPointCodec.IsType(compiledField.CodecName))
        {
            state.Variables.Remove(field.Name.Name);
            return;
        }

        CStructElement? namedElement = compiledField.NamedElement;
        object value;
        state.Stream.Position = fieldStart;

        if (field.PointerDepth > 0)
        {
            value = this.ReadPointerAddress(state);
        }
        else if (namedElement is CstructEnum enm)
        {
            value = compiledField.Reader?.Invoke(state.Stream) ??
                    throw new InvalidOperationException(
                        "Compiled enum has no storage reader: " + enm.Name.Name);
            BigInteger exact = this.compiledModelQueries.GetCompiledEnum(enm).Integer.FromStorageValue(value);
            this.UpdateExactLayoutVariable(state.Variables, field.Name.Name, exact);
            return;
        }
        else if (namedElement is not null || compiledField.Reader is not Func<Stream, object> reader)
        {
            return;
        }
        else
        {
            value = reader(state.Stream);
        }

        if (field.BitSize > 0)
        {
            value = BitfieldCodecTable.ExtractBitfieldValue(value, bitOffset, field.BitSize);
        }

        try
        {
            state.Variables[field.Name.Name] = new Literal(Convert.ToInt32(value));
        }
        catch (Exception exception) when (exception is OverflowException or InvalidCastException or FormatException)
        {
            // A scalar outside the expression language's Int32 domain is still a valid field; it simply cannot be a count.
            // A parsed scalar shadows any caller/define value with the same spelling. Keeping the older value would
            // resolve a path against data contradicted by the stream.
            state.Variables.Remove(field.Name.Name);
        }
    }

    /// <summary>Finds one exact compiled field name in a struct.</summary>
    private CompiledField FindCompiledField(Struct strct, string name)
    {
        return this.TryFindCompiledField(strct, name, out CompiledField? field)
            ? field!
            : throw new CStructPathException($"Unknown field '{name}' in '{strct.Name.Name}'.");
    }

    /// <summary>
    ///     Finds one exact compiled field name in a struct, recursing into every anonymous promoted member's own
    ///     fields (LANG-14) when the name isn't one of this level's own - never throws. Purely an in-memory,
    ///     side-effect-free tree walk, so it is safe to call speculatively before attempting a real, I/O-touching
    ///     resolution.
    /// </summary>
    private bool TryFindCompiledField(Struct strct, string name, out CompiledField? field)
    {
        CompiledCompositeType composite = this.compiledSizeQueries.GetCompiledComposite(strct);
        if (composite.FieldsByName.TryGetValue(name, out field))
        {
            return true;
        }

        foreach (CompiledField promoted in composite.PromotedFields)
        {
            if (promoted.Declaration is Struct promotedStruct &&
                this.TryFindCompiledField(promotedStruct, name, out field))
            {
                return true;
            }
        }

        field = null;
        return false;
    }
}
