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
            AttachExceptionContext(exception, segments, state.Stream);
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

        if (!this.TryGetCompiledDeclaration(segments[0].Name, out CStructElement? root))
        {
            throw new CStructPathException("Unknown root element: " + segments[0].Name);
        }

        long rootStart = stream.Position;
        CStructElement declaredRoot = root;
        CStructElement? resolvedRoot = this.ResolveCompiledNamedElement(root);
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
                                : this.GetCompiledComposite(rootTargetStruct).Symbol.Alignment;
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
                this.GetCompiledStructSizeInBytes(
                    this.GetCompiledComposite(strct),
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

        long current = structStart;
        long activeBitUnitStart = -1;
        int activeBitUnitSize = 0;
        int activeBitUnitBitsUsed = 0;
        int activeBitUnitAlignment = 0;
        string? activeBitUnitType = null;

        foreach (CompiledField compiledField in this.GetCompiledComposite(strct).Fields)
        {
            Field declaredField = compiledField.Declaration;
            Field field = compiledField.EffectiveField;
            long fieldStart;
            int bitOffset = 0;

            if (field.BitSize > 0)
            {
                int unitSize = compiledField.BitStorageSize ??
                               throw new InvalidOperationException(
                                   "Compiled bitfield has no storage size: " + field.Name.Name);
                int alignment = compiledField.Alignment;
                bool startsNewUnit = this.StartsNewBitfieldUnit(
                    activeBitUnitType,
                    activeBitUnitSize,
                    activeBitUnitAlignment,
                    activeBitUnitBitsUsed,
                    field,
                    unitSize,
                    alignment);
                if (startsNewUnit)
                {
                    current = this.Aligned ? this.AlignUp(current, alignment) : current;
                    activeBitUnitStart = current;
                    current = checked(current + unitSize);
                    activeBitUnitSize = unitSize;
                    activeBitUnitAlignment = alignment;
                    activeBitUnitType = field.Type.Name;
                    activeBitUnitBitsUsed = 0;
                }

                fieldStart = activeBitUnitStart;
                bitOffset = activeBitUnitBitsUsed;
                activeBitUnitBitsUsed += field.BitSize;
            }
            else
            {
                activeBitUnitStart = -1;
                activeBitUnitSize = 0;
                activeBitUnitBitsUsed = 0;
                activeBitUnitAlignment = 0;
                activeBitUnitType = null;

                int alignment = compiledField.Alignment;
                current = this.Aligned ? this.AlignUp(current, alignment) : current;
                fieldStart = current;
            }

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

            this.CaptureLayoutVariable(compiledField, fieldStart, bitOffset, state);
            if (field.BitSize == 0)
            {
                current = this.MeasureFieldEnd(compiledField, fieldStart, state);
            }
        }

        throw new CStructPathException($"Unknown field '{requested.Name}' in '{strct.Name.Name}'.");
    }

    /// <summary>Resolves array selection, nested structures, and contextual pointer accessors for one field.</summary>
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
        Field field = compiledField.EffectiveField;
        CStructElement? namedElement = compiledField.NamedElement;
        bool isArray = compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime;
        int count = isArray ? this.GetBoundedArrayCount(compiledField, state) : 1;
        int selectedIndex = segment.Index ?? 0;

        if (segment.Index.HasValue)
        {
            if (!isArray)
            {
                throw new CStructPathException("Field is not an indexable fixed array: " + segment.Name);
            }

            if (selectedIndex >= count)
            {
                throw new CStructPathException(
                    $"Array index {selectedIndex} is out of range for {segment.Name} with length {count}.");
            }
        }
        else if (isArray && pathIndex + 1 < segments.Count)
        {
            throw new CStructPathException("An array index is required before traversing: " + segment.Name);
        }

        context = context.EnterField(declaredField, segment.Index);
        long elementStart = this.GetArrayElementStart(compiledField, fieldStart, selectedIndex, state);
        if (pathIndex == segments.Count - 1)
        {
            return this.CreateFieldTarget(
                compiledField,
                elementStart,
                isArray,
                segment.Index,
                bitOffset,
                bitStorageSize,
                isArray ? count : null,
                state,
                context);
        }

        PathSegment next = segments[pathIndex + 1];
        if (field.PointerDepth > 0)
        {
            if (string.Equals(next.Name, "address", StringComparison.Ordinal))
            {
                if (next.Index.HasValue || pathIndex + 1 != segments.Count - 1)
                {
                    throw new CStructPathException("Pointer .address must be the terminal path segment.");
                }

                return this.CreatePointerAddressTarget(
                    compiledField,
                    elementStart,
                    isArray,
                    isArray ? count : null,
                    segment.Index,
                    state,
                    context);
            }

            if (!string.Equals(next.Name, "value", StringComparison.Ordinal) || next.Index.HasValue)
            {
                throw new CStructPathException("Expected pointer accessor '.value' or '.address' after: " + segment.Name);
            }

            return this.ResolvePointerTarget(
                compiledField,
                elementStart,
                segments,
                pathIndex + 1,
                state,
                context,
                isArray,
                isArray ? count : null,
                segment.Index);
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
                if (next.Index.HasValue)
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

    /// <summary>Creates a semantic target for an ordinary field or one selected fixed-array element.</summary>
    private ResolvedTarget CreateFieldTarget(
        CompiledField compiledField,
        long address,
        bool isArray,
        int? selectedArrayIndex,
        int bitOffset,
        int bitStorageSize,
        int? arrayLength,
        CStructOperationContext state,
        TargetResolutionContext context)
    {
        Field field = compiledField.EffectiveField;
        CStructElement? namedElement = compiledField.NamedElement;
        CompiledField writableCompiledField = selectedArrayIndex.HasValue
                                                  ? compiledField.SelectArrayElement()
                                                  : compiledField;
        Field writableField = writableCompiledField.EffectiveField;
        int alignment = writableCompiledField.Alignment;
        int? fixedSize = selectedArrayIndex.HasValue
                             ? compiledField.FixedElementSize
                             : compiledField.FixedElementSize.HasValue
                                 ? checked(compiledField.FixedElementSize.Value * (arrayLength ?? 1))
                                 : null;
        long? unionStorageAddress = context.UnionStorageAddress;
        int? unionStorageSize = context.UnionStorageSize;
        if (namedElement is Struct { IsUnion: true, } union)
        {
            unionStorageAddress = address;
            unionStorageSize = this.GetCompiledComposite(union).Symbol.FixedSize;
        }

        if (field.PointerDepth == 0 && namedElement is Struct)
        {
            state.EnsureStructureDepth(state.StructureDepth + 1);
        }

        return new ResolvedTarget(
            address,
            selectedArrayIndex.HasValue ? ResolvedTargetKind.ArrayElement : ResolvedTargetKind.Field,
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
            compiledField,
            writableCompiledField);
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
            unionStorageSize = this.GetCompiledComposite(union).Symbol.FixedSize;
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
                                      ? GetStringPointerHandlerKey(field.EffectiveField.Type)
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
            return this.GetCompiledStructSizeInBytes(
                this.GetCompiledComposite(strct),
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

    /// <summary>Returns one selected array element's start, measuring prior dynamic struct elements when necessary.</summary>
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

        Field field = compiledField.EffectiveField;
        CStructElement? namedElement = compiledField.NamedElement;
        if (namedElement is Struct nested)
        {
            long current = fieldStart;
            for (int i = 0; i < index; i++)
            {
                current = this.MeasureStructEnd(nested, current, state);
            }

            return current;
        }

        int elementSize = compiledField.FixedElementSize ??
                          this.GetCompiledFieldElementSize(compiledField, state.Variables, false);
        return checked(fieldStart + ((long)elementSize * index));
    }

    /// <summary>Evaluates one fixed array count and rejects it before traversal can loop over excessive elements.</summary>
    private int GetBoundedArrayCount(CompiledField field, CStructOperationContext state)
    {
        int count = this.GetCompiledArrayCount(field, state.Variables, false);
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
                            : this.GetBoundedArrayCount(compiledField, state);
            long current = fieldStart;
            for (int i = 0; i < count; i++)
            {
                current = this.MeasureStructEnd(nested, current, state);
            }

            return current;
        }

        if (compiledField.Array.Kind == CompiledArrayKind.Scalar &&
            field.PointerDepth == 0 &&
            IsVariableLengthType(compiledField.CodecName))
        {
            state.Stream.Position = fieldStart;
            _ = compiledField.Reader?.Invoke(state.Stream) ??
                throw new InvalidOperationException(
                    "Compiled named string has no reader: " + field.Name.Name);
            return state.Stream.Position;
        }

        if (compiledField.Array.Kind == CompiledArrayKind.Flexible)
        {
            if (!IsCharArrayField(field))
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

        int scalarCount = compiledField.Array.Kind == CompiledArrayKind.Scalar
                              ? 1
                              : this.GetBoundedArrayCount(compiledField, state);
        int elementSize = compiledField.FixedElementSize ??
                          this.GetCompiledFieldElementSize(compiledField, state.Variables, false);
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
                this.GetCompiledStructSizeInBytes(
                    this.GetCompiledComposite(strct),
                    state.Variables,
                    false));
        }

        long current = structStart;
        long activeBitUnitStart = -1;
        int activeBitUnitSize = 0;
        int activeBitUnitBitsUsed = 0;
        int activeBitUnitAlignment = 0;
        string? activeBitUnitType = null;

        foreach (CompiledField compiledField in this.GetCompiledComposite(strct).Fields)
        {
            Field field = compiledField.EffectiveField;
            long fieldStart;
            int bitOffset = 0;

            if (field.BitSize > 0)
            {
                int unitSize = compiledField.BitStorageSize ??
                               throw new InvalidOperationException(
                                   "Compiled bitfield has no storage size: " + field.Name.Name);
                int alignment = compiledField.Alignment;
                bool startsNew = this.StartsNewBitfieldUnit(
                    activeBitUnitType,
                    activeBitUnitSize,
                    activeBitUnitAlignment,
                    activeBitUnitBitsUsed,
                    field,
                    unitSize,
                    alignment);
                if (startsNew)
                {
                    current = this.Aligned ? this.AlignUp(current, alignment) : current;
                    activeBitUnitStart = current;
                    current = checked(current + unitSize);
                    activeBitUnitSize = unitSize;
                    activeBitUnitAlignment = alignment;
                    activeBitUnitType = field.Type.Name;
                    activeBitUnitBitsUsed = 0;
                }

                fieldStart = activeBitUnitStart;
                bitOffset = activeBitUnitBitsUsed;
                activeBitUnitBitsUsed += field.BitSize;
            }
            else
            {
                activeBitUnitStart = -1;
                activeBitUnitSize = 0;
                activeBitUnitBitsUsed = 0;
                activeBitUnitAlignment = 0;
                activeBitUnitType = null;
                int alignment = compiledField.Alignment;
                current = this.Aligned ? this.AlignUp(current, alignment) : current;
                fieldStart = current;
            }

            this.CaptureLayoutVariable(compiledField, fieldStart, bitOffset, state);
            if (field.BitSize == 0)
            {
                current = this.MeasureFieldEnd(compiledField, fieldStart, state);
            }
        }

        int structAlignment = this.GetCompiledComposite(strct).Symbol.Alignment;
        return this.Aligned ? this.AlignUp(current, structAlignment) : current;
    }

    /// <summary>
    ///     Validates array and nesting work hidden inside a fixed-size composite without reading bytes solely to
    ///     calculate an already compiled extent.
    /// </summary>
    private void ValidateCompositeTraversalLimits(Struct strct, CStructOperationContext state)
    {
        foreach (CompiledField compiledField in this.GetCompiledComposite(strct).Fields)
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
            BigInteger exact = this.GetCompiledEnum(enm).Integer.FromStorageValue(value);
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
            value = ExtractBitfieldValue(value, bitOffset, field.BitSize);
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
        CompiledCompositeType composite = this.GetCompiledComposite(strct);
        if (composite.FieldsByName.TryGetValue(name, out CompiledField? field))
        {
            return field;
        }

        throw new CStructPathException($"Unknown field '{name}' in '{strct.Name.Name}'.");
    }
}
