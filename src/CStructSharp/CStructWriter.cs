namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Syntax;
using CStructSharp.Values;
using CStructSharp.Writing;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     Contains the stream-writing half of <see cref="CStruct"/>.
///     It writes the same layout model used by the reader, including arrays, alignment, unions, pointers, and bitfields.
/// </summary>
public partial class CStruct
{
    /// <summary>Replaces one bitfield inside its shared storage value without changing neighboring bits.</summary>
    private void WriteBitFieldValue(
        CompiledField compiledField,
        object value,
        CStructElementWriterState state)
    {
        // A bitfield always lives inside a primitive number. A nested struct has no single number to edit; an enum
        // or flag value is first resolved to its raw bits and then merged like any other slice.
        if (compiledField.Composite is not null)
        {
            throw new InvalidOperationException("Bitfields cannot be structs.");
        }

        if (compiledField.Enum is { } compiledBitfieldEnum)
        {
            value = compiledBitfieldEnum.Integer.ToRawBits(
                EnumFieldValueParser.GetEnumValue(compiledBitfieldEnum, value, state.BindingMode));
        }

        // Work out the size of the whole storage value first, not just the small field being changed.
        int byteSize = compiledField.BitStorageSize ??
                       throw new InvalidOperationException(
                           "Compiled bitfield has no storage size: " + compiledField.Name);
        bool storageIsLittleEndian = compiledField.BitStorageIsLittleEndian ??
                                     throw new InvalidOperationException(
                                         "Compiled bitfield has no byte order: " + compiledField.Name);
        int elementBitSize = checked(byteSize * 8);
        if (state.CurrentBitOffset + compiledField.BitSize > elementBitSize)
        {
            throw new InvalidOperationException("Bitfield exceeds its storage unit: " + compiledField.Name);
        }

        // Validate the selected slice before reading or changing its shared storage unit.
        ulong fieldValue = BitfieldCodecTable.ValidateBitfieldWriteValue(compiledField.Name, compiledField.BitSize, value);

        // Bitfields share bytes. Read the existing bytes so neighboring fields survive this update.
        long curPos = state.Stream.Position;
        byte[] buffer = new byte[byteSize];
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = state.Stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                if (state.Options is UpdateOptions)
                {
                    // Update promises to modify bytes that already exist. Extending a partially present storage
                    // unit would manufacture neighbouring bits and overwrite data the caller did not supply, so stop
                    // before the later write can mutate the stream.
                    throw new CStructReadException("Cannot update a bitfield whose complete storage unit is not present.");
                }

                // A new Serialize/Write destination may not contain the rest of this storage unit yet. Only a
                // genuine end of stream is zero-extended; a legal short read is retried so neighbouring bits cannot
                // be accidentally erased.
                Array.Clear(buffer, offset, buffer.Length - offset);
                break;
            }

            offset += read;
        }

        // Keep the bits belonging to earlier fields and replace only this field's masked range.
        ulong existing = BinaryPrimitiveIO.ReadUnsigned(buffer, storageIsLittleEndian);
        ulong newValue = BitfieldCodecTable.MergeBitfieldValue(
            existing,
            fieldValue,
            BitfieldCodecTable.EffectiveShift(state.CurrentBitOffset, compiledField.BitSize, elementBitSize, this.highBitFirst),
            compiledField.BitSize);

        // Convert the merged number back to bytes, then overwrite exactly this storage unit.
        byte[] output = BinaryPrimitiveIO.WriteUnsigned(newValue, byteSize, storageIsLittleEndian);
        state.Stream.Position = curPos;
        state.Stream.Write(output, 0, output.Length);

        // Keep the stream at the start while later bitfields share this same unit.
        state.CurrentBitOffset += compiledField.BitSize;
        int bitOffsetInBytes = 1 + (state.CurrentBitOffset / 8);
        if (bitOffsetInBytes > byteSize)
        {
            // This field finished the unit. The next field starts in a fresh primitive value.
            state.CurrentBitOffset -= elementBitSize;
            state.CurrentBitfieldType = null;
        }
        else
        {
            // More bitfields fit here, so remember where the complete storage unit ends for the next normal field.
            state.Stream.Position = curPos;
            state.NextPosition = curPos + byteSize;
        }
    }

    /// <summary>Writes one compiled layout element, dispatching to struct, typedef, define, or field handling.</summary>
    private void WriteCStructElement(
        CStructElement element,
        object data,
        CStructElementWriterState state,
        long unionPosition = -1)
    {
        while (true)
        {
            switch (element)
            {
            case Struct s:
                this.WriteStruct(this.compiledSizeQueries.GetCompiledComposite(s), data, state);
                return;

            case Typedef t:
                {
                    if (t.Struct is not null)
                    {
                        // Match the reader's established root-inline-typedef projection.
                        this.WriteStruct(this.compiledSizeQueries.GetCompiledComposite(t.Struct), data, state);
                        return;
                    }

                    // Root aliases use the same immutable field projection as aliases nested inside a struct.
                    this.WriteFieldValue(this.compiledModelQueries.GetCompiledRootField(t), data, state, -1);
                    return;
                }

            case CstructEnum enm:
                this.WriteFieldValue(this.compiledModelQueries.GetCompiledRootField(enm), data, state, -1);
                return;

            case Defines d:
                // Defines influence later array sizes and expressions; writing one only updates the working variable map.
                state.Variables[d.Name.Name] = new Literal(
                    this.layoutExpressionEvaluator.Evaluate(
                        d.Value,
                        state.Variables,
                        "definition " + d.Name.Name,
                        ExpressionFailureDomain.Write));
                return;
            case Field f:
                throw new InvalidOperationException(
                    "Root field execution requires a compiled descriptor: " + f.Name.Name);
            default:
                throw new InvalidOperationException("Unsupported element type for writing: " + element.GetType().Name);
            }
        }
    }

    /// <summary>Writes one struct or union while charging exactly one active composite-depth level.</summary>
    private void WriteStruct(CompiledCompositeType composite, object data, CStructElementWriterState state)
    {
        if (data is null)
        {
            throw new CStructWriteException("Null is not valid for struct or union value: " + composite.Name);
        }

        if (state.RejectUnknownMembers)
        {
            // Checked before the static plan, which writes nested composites without re-entering this method.
            RejectUnknownMembers(composite, data, state.BindingMode);
        }

        // Static write plan (E2.10): a fully fixed composite is encoded into one block and written once when that
        // is exactly equivalent to the field-by-field path below (see TryWriteStaticPlan for the conditions).
        if (!composite.IsUnion && this.TryWriteStaticPlan(composite, data, state))
        {
            return;
        }

        state.EnterStructure();
        try
        {
            if (composite.IsUnion)
            {
                this.WriteUnion(composite, data, state);
                return;
            }

            var variableScope = composite.HasDirectConditionalFields ? new ConditionalVariableScope(composite, state.Variables) : null;
            var selection = composite.HasDirectConditionalFields ? new ConditionalFieldSelection(this.layoutExpressionEvaluator, composite.ConditionalGroupCount, ExpressionFailureDomain.Write) : null;
            var cursor = new CompositeFieldPlacementCursor(state.Stream.Position, state.Aligned);

            foreach (CompiledField field in composite.Fields)
            {
                if (selection?.IsActive(field, state.Variables) == false)
                {
                    foreach (string name in field.VisibleNames)
                    {
                        if (PocoDataBinding.TryGetMemberValue(data, name, state.BindingMode, out _))
                        {
                            throw new CStructWriteException("Inactive conditional field supplied: " + name);
                        }
                    }

                    continue;
                }

                if (composite.PromotedFields.Contains(field))
                {
                    if (field.Composite is { IsUnion: true, } promotedUnion)
                    {
                        this.WritePromotedUnion(promotedUnion, field, data, state, cursor);
                        variableScope?.CompleteField(field, state.Variables);
                        continue;
                    }

                    // An anonymous promoted member (LANG-14) has no name to look up - splice its own children
                    // into the same `data` object the parent struct already uses. WriteFieldValue's existing
                    // Struct dispatch recurses WriteStruct with this same `data`, so the promoted member's own
                    // fields are looked up directly on it, with no nested member of its own.
                    this.WriteFieldValue(field, data, state, -1, cursor);
                    variableScope?.CompleteField(field, state.Variables);
                    continue;
                }

                if (field.IsUnnamed)
                {
                    // An anonymous nonzero-width bitfield (LANG-17) or a `_` padding field is pure padding with no
                    // caller-supplied value - there is no member to look up, so write its canonical zero bits directly.
                    this.WriteFieldValue(field, CreatePaddingValue(field), state, -1, cursor);
                    variableScope?.CompleteField(field, state.Variables);
                    continue;
                }

                // Require every ordinary struct field. Missing values would make the byte layout ambiguous.
                try
                {
                    object fieldValue = PocoDataBinding.GetMemberValueOrThrow(
                        data,
                        field.Name,
                        state.BindingMode);
                    this.WriteFieldValue(field, fieldValue, state, -1, cursor);
                }
                catch (CStructException exception) when (exception.NoteMember(field.Name, field.DisplayTypeSpelling))
                {
                    throw;
                }

                variableScope?.CompleteField(field, state.Variables);
            }

            // A final aligned tail is part of the struct's storage size, not merely a cursor adjustment. Materialize
            // it for a newly serialized stream so Serialize().Length exactly matches GetStructSizeInBytes(...).
            this.CompleteStructTailPadding(composite, state, cursor);
        }
        finally
        {
            state.ExitStructure();
        }
    }

    /// <summary>
    ///     Writes an anonymous promoted union from the parent's data: the union has no name of its own, so the
    ///     member to write is chosen from the members the data supplies - the widest one first, so a value that
    ///     came from a parse (where every view is present) reproduces the complete storage - and the rest of the
    ///     union extent is cleared, exactly as <see cref="UnionValue.FromMember"/> would do.
    /// </summary>
    private void WritePromotedUnion(CompiledCompositeType composite, CompiledField field, object data, CStructElementWriterState state, CompositeFieldPlacementCursor cursor)
    {
        (long unionPosition, _) = cursor.AdvanceToField(field);
        this.ValidateOffsetAssertionAtRuntime(field, unionPosition, state.Variables);
        state.Stream.Position = unionPosition;
        int unionSize = this.compiledSizeQueries.GetCompiledStructSizeInBytes(composite, state.Variables, false);

        CompiledField? selected = null;
        object? selectedValue = null;
        foreach (CompiledField member in composite.Fields.OrderByDescending(item => item.FixedStorageSize ?? int.MaxValue))
        {
            if (composite.PromotedFields.Contains(member))
            {
                if (this.SuppliesAnyPromotedMember(member, data, state))
                {
                    selected = member;
                    selectedValue = data;
                    break;
                }

                continue;
            }

            string name = member.Name;
            if (name.Length > 0 && PocoDataBinding.TryGetMemberValue(data, name, state.BindingMode, out object? value))
            {
                selected = member;
                selectedValue = value;
                break;
            }
        }

        if (selected is null)
        {
            throw new CStructWriteException(
                "No member of the anonymous union was supplied; provide one of: " +
                string.Join(", ", composite.Shape.Names));
        }

        byte[] stagedBytes = new byte[unionSize];
        using (var stagingStream = new MemoryStream(stagedBytes, writable: true))
        {
            var stagingState = new CStructElementWriterState(
                stagingStream,
                new LayoutVariables(state.Variables),
                state.Aligned,
                state.Options,
                state.StructureDepth);
            this.WriteFieldValue(selected, selectedValue!, stagingState, 0);
        }

        long unionEnd = checked(unionPosition + unionSize);
        state.Stream.Write(stagedBytes, 0, stagedBytes.Length);
        state.Stream.Position = unionEnd;
        state.NextPosition = unionEnd;
        cursor.CompleteField(unionEnd);
    }

    /// <summary>Whether the data supplies at least one leaf of an anonymous promoted member (transitively).</summary>
    private bool SuppliesAnyPromotedMember(CompiledField promoted, object data, CStructElementWriterState state)
    {
        if (promoted.Type.Symbol.Definition is not CompiledCompositeType composite)
        {
            return false;
        }

        foreach (CompiledField member in composite.Fields)
        {
            if (composite.PromotedFields.Contains(member))
            {
                if (this.SuppliesAnyPromotedMember(member, data, state))
                {
                    return true;
                }

                continue;
            }

            string name = member.Name;
            if (name.Length > 0 && PocoDataBinding.TryGetMemberValue(data, name, state.BindingMode, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Validates and stages a complete explicit union value before submitting its fixed extent once.</summary>
    private void WriteUnion(CompiledCompositeType union, object data, CStructElementWriterState state)
    {
        UnionValue? unionValue = data as UnionValue;
        if (unionValue is null)
        {
            throw new CStructWriteException(
                "A whole union write requires UnionValue.FromRaw or UnionValue.FromMember: " + union.Name);
        }

        if (!string.Equals(unionValue.UnionName, union.Name, StringComparison.Ordinal))
        {
            throw new CStructWriteException(
                $"Union value '{unionValue.UnionName}' cannot be written as '{union.Name}'.");
        }

        int unionSize = this.compiledSizeQueries.GetCompiledStructSizeInBytes(
            union,
            state.Variables,
            false);
        byte[]? rawStorage = unionValue.HasRawStorage ? unionValue.GetRawStorageArray() : null;
        if (rawStorage is not null && rawStorage.Length != unionSize)
        {
            throw new CStructWriteException(
                $"Raw storage length mismatch for {union.Name}: expected {unionSize}, got {rawStorage.Length}.");
        }

        long unionPosition = state.Stream.Position;
        long unionEnd = checked(unionPosition + unionSize);
        if (!unionValue.HasSelection)
        {
            state.Stream.Write(rawStorage!, 0, rawStorage!.Length);
            state.Stream.Position = unionEnd;
            state.NextPosition = unionEnd;
            return;
        }

        string selectedMember = unionValue.SelectedMember!;
        CompiledField? selected = union.Fields.FirstOrDefault(
            field => string.Equals(field.Name, selectedMember, StringComparison.Ordinal));
        if (selected is null)
        {
            throw new CStructWriteException(
                $"Union '{union.Name}' has no member named '{selectedMember}'.");
        }

        // Build the complete union extent away from the destination. New writes and clearing updates start at zero;
        // preserving updates copy the existing extent before the selected member is overlaid.
        byte[] stagedBytes = new byte[unionSize];
        if (state.Options is UpdateOptions { ClearUnionStorage: false, })
        {
            try
            {
                state.Stream.ReadExactly(stagedBytes);
            }
            catch (EndOfStreamException exception)
            {
                throw new CStructReadException(
                    "Cannot preserve union storage because the complete existing extent is not present.",
                    exception);
            }
            finally
            {
                state.Stream.Position = unionPosition;
            }
        }

        using (var stagingStream = new MemoryStream(stagedBytes, writable: true))
        {
            var stagingState = new CStructElementWriterState(
                stagingStream,
                new LayoutVariables(state.Variables),
                state.Aligned,
                state.Options,
                state.StructureDepth);
            try
            {
                this.WriteFieldValue(selected, unionValue.SelectedValue!, stagingState, 0);
            }
            catch (CStructWriteException)
            {
                throw;
            }
            catch (Exception exception) when (exception is InvalidOperationException or
                                              ArgumentException or ArithmeticException or
                                              FormatException or InvalidCastException or
                                              NotSupportedException)
            {
                throw new CStructWriteException(
                    $"Cannot write selected union member '{union.Name}.{selectedMember}'.",
                    exception);
            }
        }

        state.Stream.Write(stagedBytes, 0, stagedBytes.Length);
        state.Stream.Position = unionEnd;
        state.NextPosition = unionEnd;
    }

    /// <summary>
    ///     <see cref="UnknownMemberPolicy.Reject"/>: every member the supplied value carries must be one the composite
    ///     declares. Dictionaries are matched by exact key (the lookup the writer performs); .NET objects by the
    ///     case-insensitive member resolution POCO binding uses. A parsed <see cref="UnionValue"/> is trusted.
    /// </summary>
    private static void RejectUnknownMembers(CompiledCompositeType composite, object data, PocoBindingMode bindingMode)
    {
        StructShape shape = composite.Shape;
        switch (data)
        {
            case UnionValue:
                return;
            case IDictionary<string, object?> members:
                foreach (string key in members.Keys)
                {
                    if (!shape.TryGetIndex(key, out _))
                    {
                        throw UnknownMember(composite, key);
                    }
                }

                break;
            default:
                foreach (string member in PocoDataBinding.EnumerateMemberNames(TypedValueConverter.DeclaredMappedType(data.GetType()), bindingMode))
                {
                    if (!shape.TryGetIndex(member, out _) &&
                        !shape.Names.Any(name => string.Equals(name, member, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw UnknownMember(composite, member);
                    }
                }

                break;
        }

        RejectUnknownNestedMembers(composite, data, bindingMode);
    }

    /// <summary>Applies the same check to every by-value nested struct the composite declares, arrays included.</summary>
    private static void RejectUnknownNestedMembers(CompiledCompositeType composite, object data, PocoBindingMode bindingMode)
    {
        foreach (CompiledField field in composite.Fields)
        {
            if (field.Composite is not { } nested)
            {
                continue;
            }

            if (composite.PromotedFields.Contains(field))
            {
                // A promoted member's children live on the same data object; only its own nested composites need checking.
                RejectUnknownNestedMembers(nested, data, bindingMode);
                continue;
            }

            if (field.IsUnnamed || !PocoDataBinding.TryGetMemberValue(data, field.Name, bindingMode, out object? value) || value is null)
            {
                continue;
            }

            try
            {
                if (field.Array.Kind == CompiledArrayKind.Scalar)
                {
                    RejectUnknownMembers(nested, value, bindingMode);
                }
                else if (value is System.Collections.IEnumerable elements and not string)
                {
                    foreach (object? element in elements)
                    {
                        if (element is not null)
                        {
                            RejectUnknownMembers(nested, element, bindingMode);
                        }
                    }
                }
            }
            catch (CStructException exception) when (exception.NoteMember(field.Name, field.DisplayTypeSpelling))
            {
                throw;
            }
        }
    }

    private static CStructWriteException UnknownMember(CompiledCompositeType composite, string member)
    {
        string declared = composite.Shape.Names.Length == 0 ? "no members" : string.Join(", ", composite.Shape.Names);
        return new CStructWriteException(
            $"'{member}' is not a member of '{composite.Name}' (WriteOptions.UnknownMembers is Reject). The layout declares: {declared}.");
    }

    /// <summary>The all-zero value an unnamed padding field is written with: a zero scalar, or one zero per fixed element.</summary>
    private static object CreatePaddingValue(CompiledField field)
    {
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return 0;
        }

        if (field.IsCharacterArray)
        {
            return string.Empty;
        }

        var zeroes = new object[field.Array.TotalFixedElementCount ?? 0];
        Array.Fill(zeroes, 0);
        return zeroes;
    }

    private void WriteFieldValue(
        CompiledField compiledField,
        object value,
        CStructElementWriterState state,
        long unionPosition,
        CompositeFieldPlacementCursor? cursor = null)
    {
        // Keep a local field because an unsized character array is treated as a terminated string for writing.
        if (value is null &&
            (compiledField.PointerDepth == 0 || compiledField.Array.Kind != CompiledArrayKind.Scalar))
        {
            throw new CStructWriteException(
                "Null is valid only for a scalar pointer field: " + compiledField.Name);
        }

        CompiledField valueField = compiledField;
        int numFieldValues = 1;
        bool unknownArray = false;
        bool hasFixedArrayDeclarator =
            compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime;
        bool dataSizedArray = compiledField.Array.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;

        if (dataSizedArray)
        {
            // A data-sized array writes exactly the supplied elements (plus its terminator, below).
            unknownArray = true;
        }
        else if (compiledField.Array.Kind != CompiledArrayKind.Scalar)
        {
            if (compiledField.Array.Kind == CompiledArrayKind.Flexible)
            {
                // C-style char[] has no fixed count here. Select a string handler that writes its terminator.
                unknownArray = true;
                if (compiledField.IsCharElement)
                {
                    valueField = compiledField.SelectPointerTarget(
                        0,
                        CharacterFieldTypes.CstringType.Name,
                        compiledField.TerminatedReader,
                        compiledField.TerminatedWriter,
                        this.PointerSize);
                }
                else if (compiledField.IsWideCharElement)
                {
                    string handler = CharacterFieldTypes.GetStringPointerHandlerKey(compiledField.EffectiveField.Type);
                    valueField = compiledField.SelectPointerTarget(
                        0,
                        handler,
                        compiledField.TerminatedReader,
                        compiledField.TerminatedWriter,
                        this.PointerSize);
                }
            }
            else if (compiledField.Array.Dimensions.Length > 1)
            {
                // Every dimension of a multidimensional array is compile-time-fixed (LANG-05's
                // fixed-dimensions-only slice), so the total leaf count is already known without evaluating any
                // expression against the current write state.
                numFieldValues = compiledField.Array.TotalFixedElementCount ??
                                 throw new InvalidOperationException(
                                     "Multidimensional array has no fixed total element count: " +
                                     compiledField.Name);
                if (numFieldValues > state.Options.MaxArrayElements)
                {
                    throw new CStructWriteLimitException(
                        "Array length exceeds the configured write limit: " + compiledField.Name);
                }
            }
            else
            {
                // Fixed array counts may refer to an earlier field or #define, so calculate them from the current state.
                numFieldValues = this.layoutExpressionEvaluator.Evaluate(
                    compiledField.Array.CountExpression ??
                    throw new InvalidOperationException(
                        "Compiled array has no count expression: " + compiledField.Name),
                    state.Variables,
                    "array length for " + compiledField.Name,
                    ExpressionFailureDomain.Write);
                if (numFieldValues < 0)
                {
                    throw new CStructWriteException("Array length cannot be negative: " + compiledField.Name);
                }

                if (numFieldValues > state.Options.MaxArrayElements)
                {
                    throw new CStructWriteLimitException(
                        "Array length exceeds the configured write limit: " + compiledField.Name);
                }
            }
        }

        bool isArray = hasFixedArrayDeclarator || unknownArray;
        BigInteger? writtenEnumValue = null;
        if (unknownArray && PrimitiveCodecs.IsVariableLengthType(valueField.TypeSpelling))
        {
            // A terminated string view writes one value through its string codec, not element by element.
            isArray = false;
        }

        bool positionIsResolvedTarget = state.PositionIsResolvedTarget;
        state.PositionIsResolvedTarget = false;
        bool useLegacyPlacement = cursor is null || unionPosition != -1 || positionIsResolvedTarget;

        if (unionPosition != -1)
        {
            // Each union member begins at the same address, just as it does while reading.
            state.Stream.Position = unionPosition;
            state.CurrentBitOffset = 0;
            state.CurrentBitfieldType = null;
        }

        if (useLegacyPlacement)
        {
            if (state.CurrentBitOffset > 0 && compiledField.BitSize == 0)
            {
                // A normal field cannot share a partly used bitfield storage unit. Move past that unit first.
                state.CurrentBitOffset = 0;
                state.CurrentBitfieldType = null;
                state.Stream.Position = state.NextPosition;
            }

            if (compiledField.BitSize > 0)
            {
                int bitCapacity = checked(
                    (compiledField.BitStorageSize ??
                     throw new InvalidOperationException(
                         "Compiled bitfield has no storage size: " + compiledField.Name)) * 8);
                int activeUnitSize = state.CurrentBitfieldType is null
                                         ? 0
                                         : state.CurrentBitfieldSize;
                bool startsNewStorageUnit = state.CurrentBitOffset > 0 &&
                                            LayoutMath.StartsNewBitfieldUnit(
                                                state.CurrentBitfieldType,
                                                activeUnitSize,
                                                activeUnitSize,
                                                state.CurrentBitOffset,
                                                compiledField.BitUnitType,
                                                compiledField.BitSize,
                                                bitCapacity / 8,
                                                bitCapacity / 8);
                if (startsNewStorageUnit)
                {
                    state.Stream.Position = state.NextPosition;
                    state.CurrentBitOffset = 0;
                    state.CurrentBitfieldType = null;
                }

                if (state.CurrentBitOffset == 0)
                {
                    state.CurrentBitfieldType = compiledField.BitUnitType;
                    state.CurrentBitfieldSize = compiledField.BitStorageSize ??
                                                throw new InvalidOperationException(
                                                    "Compiled bitfield has no storage size: " +
                                                    compiledField.Name);
                }
            }

            long curPos = state.Stream.Position;

            if (state.Aligned && !positionIsResolvedTarget)
            {
                // Apply alignment after resolving the real field type, because pointers and aliases can change its boundary.
                int structAlignment = valueField.Alignment;
                if (structAlignment != state.CurrentFieldAlignment && state.CurrentBitOffset > 0)
                {
                    curPos = state.NextPosition;
                    state.CurrentBitOffset = 0;
                    state.CurrentBitfieldType = null;
                }

                // Advance to the next boundary. The bytes skipped here are the layout's padding.
                state.Stream.Position = LayoutMath.AlignUp(curPos, structAlignment);
                curPos = state.Stream.Position;

                state.CurrentFieldAlignment = structAlignment;
            }
        }
        else
        {
            // The cursor already knows this field's start (and, for a bitfield, whether it continues the active
            // storage unit or opens a new one) - apply its decision once, for every array element, instead of
            // re-deriving it per element the way the legacy path above does.
            (long fieldStart, int bitOffset) = cursor!.AdvanceToField(valueField);
            this.ValidateOffsetAssertionAtRuntime(valueField, fieldStart, state.Variables);
            state.Stream.Position = fieldStart;
            if (compiledField.BitSize > 0)
            {
                state.CurrentBitOffset = bitOffset;
                state.CurrentBitfieldType = compiledField.BitUnitType;
                state.CurrentBitfieldSize = compiledField.BitStorageSize ??
                                            throw new InvalidOperationException(
                                                "Compiled bitfield has no storage size: " + compiledField.Name);
            }
            else
            {
                state.CurrentBitOffset = 0;
                state.CurrentBitfieldType = null;
            }
        }

        if (isArray && compiledField.Array.Dimensions.Length > 1)
        {
            // Multidimensional arrays are never Flexible/unknownArray (Seam 4 requires every dimension fixed for
            // N >= 2), so the caller-supplied value is always an N-deep nested collection to flatten, mirroring
            // the reader's flat-then-reshape approach in reverse: flatten first, then write the same flat
            // sequence a 1-D array of the same total count would already write.
            int[] dimensionSizes = compiledField.Array.Dimensions
                .Select(
                    dimension => dimension.FixedCount ??
                                 throw new InvalidOperationException(
                                     "Multidimensional array dimension has no fixed count: " +
                                     compiledField.Name))
                .ToArray();

            if (valueField.IsCharacterArray)
            {
                // The innermost dimension of a fixed string table collapses one caller-supplied string per row,
                // exactly like today's single-dimension char[32] buffer; only the outer dimensions flatten.
                int rowSize = dimensionSizes[^1];
                List<object> rows = this.FlattenNestedArrayValues(value!, dimensionSizes[..^1], compiledField.Name);
                foreach (object row in rows)
                {
                    string rowString = row as string ??
                                        WriteValueMaterialization.ConvertToBoundedCharString(row, rowSize, compiledField.Name);
                    this.WriteFixedCharArray(compiledField, rowString, rowSize, state);
                }
            }
            else
            {
                List<object> leaves = this.FlattenNestedArrayValues(value!, dimensionSizes, compiledField.Name);
                for (int i = 0; i < leaves.Count; i++)
                {
                    _ = this.WriteSingleFieldValue(compiledField, leaves[i], state);
                }
            }
        }
        else if (isArray)
        {
            if (valueField.IsCharacterArray ||
                (!compiledField.IsPointer && BoundedTextCodec.IsType(compiledField.TypeSpelling)))
            {
                // Character arrays accept either one string or a collection of characters and always fill the declared size.
                string str = value as string ??
                             WriteValueMaterialization.ConvertToBoundedCharString(value!, numFieldValues, compiledField.Name);
                this.WriteFixedCharArray(compiledField, str, numFieldValues, state);
            }
            else
            {
                // Other arrays are written item by item so nested structs, enums, and pointers use their normal logic.
                int materializationLimit = unknownArray ? state.Options.MaxArrayElements : numFieldValues;
                IList<object> items = WriteValueMaterialization.ConvertToObjectList(
                    value!,
                    materializationLimit,
                    compiledField.Name);
                int count = unknownArray ? items.Count : numFieldValues;
                if (count > state.Options.MaxArrayElements)
                {
                    throw new CStructWriteLimitException(
                        "Array length exceeds the configured write limit: " + compiledField.Name);
                }

                if (!unknownArray && items.Count != count)
                {
                    throw new CStructWriteException(
                        $"Array length mismatch for {compiledField.Name}: expected {count}, got {items.Count}.");
                }

                for (int i = 0; i < count; i++)
                {
                    _ = this.WriteSingleFieldValue(compiledField, items[i], state);
                }

                if (compiledField.Array.Kind == CompiledArrayKind.Terminated)
                {
                    // One all-zero element closes the array.
                    int elementSize = compiledField.FixedElementSize ??
                                      throw new InvalidOperationException("Data-sized array has no fixed element size: " + compiledField.Name);
                    state.WriteZeroes(elementSize);
                }
            }
        }
        else
        {
            writtenEnumValue = this.WriteSingleFieldValue(valueField, value!, state);
        }

        // A partially filled bitfield intentionally leaves Position at the start of its shared unit. Preserve the
        // recorded end in that case so the next ordinary field, a union reservation, or struct tail starts after it.
        state.NextPosition = Math.Max(state.NextPosition, state.Stream.Position);

        if (!useLegacyPlacement && compiledField.BitSize == 0)
        {
            // Bitfields skip this: the cursor already reserved their whole storage unit's span when it opened,
            // mirroring how CStructAddressResolver's own cursor usage never completes a bitfield.
            cursor!.CompleteField(state.Stream.Position);
        }

        // Later fields may use this field in an expression, so keep the writer's variable map in step with the bytes.
        if (!compiledField.CapturesLayoutVariable && !state.CaptureAllLayoutVariables)
        {
            // No expression in this layout can name the field (E2.6): nothing to publish.
        }
        else if (writtenEnumValue is BigInteger exactEnumValue)
        {
            this.UpdateExactLayoutVariable(state.Variables, compiledField.Name, exactEnumValue);
        }
        else
        {
            if (compiledField.Codec.IsFixedPoint || compiledField.Codec.IsIdentifier)
            {
                state.Variables.Remove(compiledField.Name);
            }
            else
            {
                WriterVariableProjection.UpdateVariablesFromValue(state, compiledField.Name, value!);
            }

            state.PublishQualified(compiledField.Name);
        }
    }

    /// <summary>
    ///     Materializes a caller-supplied N-deep nested collection into a flat, row-major list of leaf values,
    ///     validating that every level matches its declared dimension size exactly. Each level is materialized
    ///     with the same <see cref="WriteValueMaterialization.ConvertToObjectList"/> a single-dimension array
    ///     already uses once - this just repeats that call once per remaining dimension.
    /// </summary>
    private List<object> FlattenNestedArrayValues(object value, IReadOnlyList<int> dimensionSizes, string fieldName)
    {
        IList<object> level = WriteValueMaterialization.ConvertToObjectList(value, dimensionSizes[0], fieldName);
        if (level.Count != dimensionSizes[0])
        {
            throw new CStructWriteException(
                $"Array length mismatch for {fieldName}: expected {dimensionSizes[0]}, got {level.Count}.");
        }

        if (dimensionSizes.Count == 1)
        {
            return level as List<object> ?? level.ToList();
        }

        int[] remainingDimensions = [.. dimensionSizes.Skip(1),];
        var flattened = new List<object>();
        foreach (object item in level)
        {
            flattened.AddRange(this.FlattenNestedArrayValues(item, remainingDimensions, fieldName));
        }

        return flattened;
    }

    /// <summary>Writes a fixed character array and fills any remaining slots with zero characters.</summary>
    private void WriteFixedCharArray(
        CompiledField compiledField,
        string value,
        int count,
        CStructElementWriterState state)
    {
        if (BoundedTextCodec.IsType(compiledField.TypeSpelling))
        {
            state.EnsureStringBytes(count);
            if (BoundedTextCodec.IsUtf16(compiledField.TypeSpelling) && (count & 1) != 0)
            {
                throw new CStructWriteException("UTF-16 byte capacity must be even.");
            }

            byte[] encoded;
            try
            {
                int length = BoundedTextCodec.GetByteCount(compiledField.TypeSpelling, value);
                if (length > count)
                {
                    throw new CStructWriteException($"Encoded string is too long for {compiledField.Name}: {length} encoded bytes > {count}.");
                }

                encoded = BoundedTextCodec.Encode(compiledField.TypeSpelling, value);
            }
            catch (EncoderFallbackException exception)
            {
                throw new CStructWriteException("String cannot be represented in the selected encoding.", exception);
            }

            state.Stream.Write(encoded, 0, encoded.Length);
            state.WriteZeroes(count - encoded.Length);
            return;
        }

        // Fixed arrays must consume their declared byte count. Reject too much input instead of silently truncating it.
        if (value.Length > count)
        {
            throw new CStructWriteException($"String is too long for {compiledField.Name}: {value.Length} > {count}.");
        }

        long encodedByteCount = checked((long)count * (compiledField.IsWideCharElement ? 2 : 1));
        state.EnsureStringBytes(encodedByteCount);

        // Padding with NUL matches the usual C character-buffer convention.
        string padded = value.PadRight(count, '\0');
        if (compiledField.IsWideCharElement)
        {
            byte[] encoded;
            try
            {
                encoded = this.GetWideCharacterEncoding(compiledField).GetBytes(padded);
            }
            catch (EncoderFallbackException exception)
            {
                throw new CStructWriteException(
                    "Wide-character buffer contains an invalid UTF-16 code-unit sequence.",
                    exception);
            }

            state.Stream.Write(encoded, 0, encoded.Length);
            return;
        }

        foreach (char c in padded)
        {
            this.WritePrimitiveValue(compiledField, state.Stream, c, compiledField.Name);
        }
    }

    /// <summary>
    ///     Advances past the final tail padding of an ordinary struct. New output receives explicit zero bytes because
    ///     seeking past the end of a <see cref="MemoryStream"/> does not extend its length; updates preserve bytes that
    ///     already occupy padding because callers asked to change a field, not to normalize surrounding storage.
    /// </summary>
    private void CompleteStructTailPadding(CompiledCompositeType composite, CStructElementWriterState state, CompositeFieldPlacementCursor cursor)
    {
        // The cursor already tracks the position past any dangling bitfield unit's full reserved span - trust it
        // rather than state.Stream.Position/NextPosition, which a shared bitfield write may have rewound mid-unit.
        state.Stream.Position = cursor.Current;

        if (!this.Aligned)
        {
            return;
        }

        long alignedEnd = cursor.FinishComposite(composite.Symbol.Alignment);
        if (alignedEnd == state.Stream.Position)
        {
            return;
        }

        int paddingLength = checked((int)(alignedEnd - state.Stream.Position));
        if (state.Options is UpdateOptions)
        {
            state.Stream.Position += paddingLength;
        }
        else
        {
            state.WriteZeroes(paddingLength);
        }

        state.NextPosition = state.Stream.Position;
    }

    /// <summary>Writes a pointer address after applying the configured absolute or relative addressing rule.</summary>
    private void WritePointerAddress(Stream stream, long address, WriteOptions options)
    {
        // Callers provide a physical signed stream address. The shared address domain applies relative conversion,
        // null semantics, and pointer-width validation before the stream receives any bytes.
        ulong value = CStructPointerArithmetic.EncodeTargetAddress(
            address,
            options.AddressingMode,
            options.Origin,
            this.PointerSize);

        // The shared primitive helper handles the layout byte order for every supported pointer width.
        byte[] bytes = BinaryPrimitiveIO.WriteUnsigned(value, this.PointerSize, this.IsLittleEndian);
        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Writes one non-array field by choosing the correct primitive, enum, struct, bitfield, or pointer path.</summary>
    private BigInteger? WriteSingleFieldValue(
        CompiledField compiledField,
        object value,
        CStructElementWriterState state)
    {
        // Resolve these once so each case below can choose the smallest correct writing path.
        string fieldTypeName = compiledField.TypeSpelling;
        bool isKnownFieldType = compiledField.Writer is not null;

        if (compiledField.PointerDepth > 0)
        {
            // At the field itself, a pointer writes only its address. Update handles writing through .value separately.
            long address = CStructPointerArithmetic.ConvertTargetAddress(value);
            this.WritePointerAddress(state.Stream, address, state.Options);
            return null;
        }

        if (compiledField.BitSize > 0)
        {
            // Bitfields must merge into a shared storage value rather than write a standalone primitive.
            this.WriteBitFieldValue(compiledField, value, state);
            return null;
        }

        // Named layout types are enums or nested composites; both need more than a primitive handler call.
        if (compiledField.Enum is { } compiledEnum)
        {
            BigInteger enumValue = EnumFieldValueParser.GetEnumValue(compiledEnum, value, state.BindingMode);
            (compiledField.Writer ??
             throw new InvalidOperationException(
                 "Compiled enum has no storage writer: " + compiledEnum.Name))(
                state.Stream,
                compiledEnum.Integer.ToStorageValue(enumValue));
            return enumValue;
        }

        if (compiledField.Composite is { } strct)
        {
            // A field named through a dotted path (`hdr.n`) republishes its nested values under the
            // qualified prefix while its body is written.
            string? outerPrefix = state.QualifiedPrefix;
            if (compiledField.HasQualifiedPrefix && compiledField.Array.Kind == CompiledArrayKind.Scalar)
            {
                state.QualifiedPrefix = outerPrefix is null ? compiledField.QualifiedPrefix : outerPrefix + compiledField.QualifiedPrefix;
            }

            this.WriteStruct(strct, value, state);
            state.QualifiedPrefix = outerPrefix;
            return null;
        }

        if (!isKnownFieldType)
        {
            throw new InvalidOperationException($"No handler for field type {fieldTypeName}");
        }

        // The remaining case is a normal primitive type registered when this layout was created.
        this.WritePrimitiveValue(compiledField, state.Stream, value, compiledField.Name);
        return null;
    }

    /// <summary>Translates only expected caller-value conversion failures from a compiled primitive codec.</summary>
    private void WritePrimitiveValue(
        CompiledField field,
        Stream stream,
        object value,
        string fieldName)
    {
        Action<Stream, object> writer = field.Writer ??
                                        throw new InvalidOperationException(
                                            "Compiled field has no writer: " + field.CodecName);
        try
        {
            if (stream is WriteBudgetStream { IsSparseUpdate: true } && (field.Codec.IsLeb128 || (field.Codec.IsCustom && !field.FixedElementSize.HasValue)))
            {
                long start = stream.Position;
                _ = field.Reader!(stream);
                long available = stream.Position - start;
                stream.Position = start;
                using var encoded = new MemoryStream();
                writer(encoded, value);
                if (encoded.Length != available)
                {
                    throw new CStructWriteException("LEB128 updates must preserve the existing encoded byte length.");
                }

                stream.Write(encoded.GetBuffer(), 0, (int)encoded.Length);
                return;
            }

            writer(stream, value);
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or
                                          FormatException or InvalidCastException)
        {
            throw new CStructWriteException(DescribeUnwritableValue(value, field), exception);
        }
    }

    /// <summary>
    ///     Says what was supplied and what the field accepts: the value as text, its CLR type when that is the
    ///     problem, and the integer range of a fixed-width codec when the value is a number outside it.
    /// </summary>
    private static string DescribeUnwritableValue(object? value, CompiledField field)
    {
        string shown = value switch
        {
            null => "null",
            string text => "\"" + text + "\"",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.GetType().Name,
        };
        string range = field.Codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => "0 to 255",
            PrimitiveCodecKind.Int8 => "-128 to 127",
            PrimitiveCodecKind.UInt16 => "0 to 65535",
            PrimitiveCodecKind.Int16 => "-32768 to 32767",
            PrimitiveCodecKind.UInt24 => "0 to 16777215",
            PrimitiveCodecKind.Int24 => "-8388608 to 8388607",
            PrimitiveCodecKind.UInt32 => "0 to 4294967295",
            PrimitiveCodecKind.Int32 => "-2147483648 to 2147483647",
            PrimitiveCodecKind.UInt48 => "0 to 281474976710655",
            PrimitiveCodecKind.Int48 => "-140737488355328 to 140737488355327",
            PrimitiveCodecKind.UInt64 => "0 to 18446744073709551615",
            PrimitiveCodecKind.Int64 => "-9223372036854775808 to 9223372036854775807",
            _ => string.Empty,
        };
        string accepts = range.Length > 0 ? $"{field.TypeSpelling} accepts {range}" : field.TypeSpelling;
        return value is string or null || value is not IFormattable
                   ? $"Value {shown} cannot be written as {field.TypeSpelling}."
                   : $"Value {shown} does not fit: {accepts}.";
    }

    /// <summary>
    ///     Creates a new byte array while snapshotting expression variables from a read-only caller view.
    /// </summary>
    internal byte[] SerializeCore(
        string elementNameOrPath,
        object data,
        LayoutVariableInput variables,
        WriteOptions? options = null)
    {
        // Serialize is the convenience entry point: write to a temporary stream, then hand its complete contents to the caller.
        using var stream = new MemoryStream();
        this.WriteStreamCore(stream, elementNameOrPath, data, variables, options);
        return stream.ToArray();
    }

    /// <summary>
    ///     Updates a selected value while snapshotting expression variables from a read-only caller view. All
    ///     library-detectable failures occur against bounded sparse staging before destination commit, and the
    ///     replacement cannot extend the existing stream.
    /// </summary>
    internal void UpdateStreamCore(
        Stream stream,
        string elementNameOrPath,
        object value,
        LayoutVariableInput variables,
        UpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek || !stream.CanWrite)
        {
            throw new ArgumentException("Updating requires a readable, writable, seekable stream.", nameof(stream));
        }

        // Updating needs a real path because it starts from bytes that already exist instead of creating a new root value.
        if (string.IsNullOrWhiteSpace(elementNameOrPath))
        {
            throw new CStructPathException("Path is empty.");
        }

        UpdateOptions effectiveOptions = CStructElementWriterState.SnapshotUpdateOptions(options);
        CStructElementWriterState.ValidateWriteOptions(effectiveOptions);

        // Copy caller variables and calculate #defines so array lengths are evaluated exactly as they are for normal writes.
        Dictionary<string, Expr> effectiveVariables = variables.Resolve(this.layoutVariableResolver);
        IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);

        if (segments.Count == 0)
        {
            throw new CStructPathException("Path is empty.");
        }

        string rootName = segments[0].Name;
        if (!this.compiledModelQueries.TryGetCompiledDeclaration(rootName, out CStructElement? rootElement))
        {
            CStructPathException exception = this.compiledModelQueries.UnknownRoot(rootName);
            ExceptionContext.Attach(exception, segments, stream);
            throw exception;
        }

        ReadOperationSettings readOptions = ReadOperationSettings.SnapshotTraversalOptions(effectiveOptions);
        var readState = new CStructOperationContext(
            stream,
            effectiveVariables,
            this.Aligned,
            readOptions);

        // Update promises not to leave the caller's stream somewhere unexpected, even if writing fails.
        long originalPosition = readState.Stream.Position;
        Exception? primaryException = null;
        try
        {
            Dictionary<string, Expr>? layoutVariables = null;
            (string Path, long Start, long End)[]? originalLayout = null;
            if (this.HasConditionalLayout(rootName))
            {
                layoutVariables = new Dictionary<string, Expr>(effectiveVariables);
                originalLayout = this.CaptureUpdateLayout(readState.Stream, originalPosition, rootElement, layoutVariables, readOptions);
                readState.Stream.Position = originalPosition;
            }

            ResolvedTarget target = this.ResolveTargetFromLayout(readState, segments);

            // From here on, all writer helpers use the exact absolute target coordinates without touching caller bytes.
            readState.Stream.Position = target.Address;

            if (target.Kind == ResolvedTargetKind.PointerValue &&
                effectiveOptions.RequireExistingPointerTarget &&
                target.Address == 0)
            {
                throw new CStructReadException("Cannot update a null pointer target when RequireExistingPointerTarget is enabled.");
            }

            // Run the compiled writer once against a sparse view whose baseline reads share the traversal budget.
            using var stagingStream = new SparseUpdateStream(readState.Stream, target.Address);
            var state = new CStructElementWriterState(
                                                       stagingStream,
                                                       effectiveVariables,
                                                       this.Aligned,
                                                       effectiveOptions);

            if (target.Kind == ResolvedTargetKind.PointerAddress)
            {
                // .address changes only the pointer number. It never touches the pointed-to data.
                long addressValue = CStructPointerArithmetic.ConvertTargetAddress(value);
                this.WritePointerAddress(state.Stream, addressValue, effectiveOptions);
            }
            else if (target.Kind == ResolvedTargetKind.Root)
            {
                // A root path selects the complete declared element rather than a single field.
                this.WriteCStructElement(rootElement, value, state);
            }
            else
            {
                CompiledField writableCompiledField = target.WritableCompiledField ??
                                                      throw new CStructPathException(
                                                          "The selected path has no compiled writable field target.");
                state.PositionIsResolvedTarget = true;
                if (target.BitStorageSize > 0)
                {
                    // A later bitfield starts at the same byte address as its predecessors. Seed the shared writer
                    // state from the semantic target so only the selected bit range changes.
                    state.CurrentBitOffset = target.BitOffset;
                    state.CurrentBitfieldType = writableCompiledField.BitUnitType;
                    state.CurrentBitfieldSize = target.BitStorageSize;
                    state.CurrentFieldAlignment = target.Alignment;
                    state.NextPosition = checked(target.Address + target.BitStorageSize);
                }

                this.WriteFieldValue(writableCompiledField, value, state, -1);
            }

            // The caller sees writes only after every library-detectable writer failure has been ruled out.
            if (originalLayout is not null)
            {
                var changedLayout = this.CaptureUpdateLayout(stagingStream, originalPosition, rootElement, layoutVariables!, readOptions);
                if (!originalLayout.SequenceEqual(changedLayout))
                {
                    throw new CStructWriteException("Update changes the active conditional storage layout; serialize a new buffer instead.");
                }
            }

            stagingStream.CommitTo(stream);
        }
        catch (Exception exception)
        {
            primaryException = exception;
            if (exception is CStructException domainException)
            {
                ExceptionContext.Attach(domainException, segments, stream);
            }

            throw;
        }
        finally
        {
            try
            {
                // Keep the position contract on success, validation errors, and physical commit errors alike.
                readState.Stream.Position = originalPosition;
                readState.Complete();
            }
            catch (Exception) when (primaryException is not null)
            {
                // A broken destination may reject restoration after a failed commit; preserve the primary failure.
            }
            catch (CStructException restorationException)
            {
                ExceptionContext.Attach(restorationException, segments, stream);
                throw;
            }
        }
    }

    /// <summary>
    ///     Writes a complete or selected value while snapshotting expression variables from a read-only caller view.
    /// </summary>
    internal void WriteStreamCore(
        Stream stream,
        string elementNameOrPath,
        object data,
        LayoutVariableInput variables,
        WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite || !stream.CanSeek)
        {
            throw new ArgumentException("Writing requires a writable, seekable stream.", nameof(stream));
        }

        // Validate the requested root before touching the stream so bad paths fail without partial output.
        if (string.IsNullOrWhiteSpace(elementNameOrPath))
        {
            throw new CStructPathException("Path is empty.");
        }

        WriteOptions effectiveOptions = CStructElementWriterState.SnapshotWriteOptions(options);
        CStructElementWriterState.ValidateWriteOptions(effectiveOptions);

        // Definitions and supplied variables form the small expression environment used for array counts.
        Dictionary<string, Expr> effectiveVariables = variables.Resolve(this.layoutVariableResolver);
        IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);

        if (segments.Count == 0)
        {
            throw new CStructPathException("Path is empty.");
        }

        string rootName = segments[0].Name;
        if (!this.compiledModelQueries.TryGetCompiledDeclaration(rootName, out CStructElement? rootElement))
        {
            CStructPathException exception = this.compiledModelQueries.UnknownRoot(rootName);
            ExceptionContext.Attach(exception, segments, stream);
            throw exception;
        }

        // Callers may pass either { root: ... } or the root object itself; accept both forms at the public boundary.
        object rootData = PocoDataBinding.NormalizeRootData(data, rootName, effectiveOptions.BindingMode);

        // Keep all write-time choices in one state object for recursive struct and field calls.
        try
        {
            var state = new CStructElementWriterState(
                stream,
                effectiveVariables,
                this.Aligned,
                effectiveOptions);

            if (segments.Count == 1)
            {
                // The common case writes the entire root declaration.
                this.WriteCStructElement(rootElement, rootData, state);
                return;
            }

            IReadOnlyList<PathSegment> childSegments = segments.Skip(1).ToArray();

            object subData = rootData;
            if (childSegments.Count > 0 &&
                PocoDataBinding.TryGetMemberValue(rootData, childSegments[0].Name, effectiveOptions.BindingMode, out _))
            {
                // If the caller supplied a complete root object, walk down to the matching nested source value.
                subData = PocoDataBinding.ResolveDataPath(rootData, childSegments, effectiveOptions.BindingMode);
            }

            // Separately resolve the layout shape so the writer knows whether the selected target is a field, struct, or typedef.
            CompiledField targetField = this.ResolveElementPath(rootElement, childSegments, effectiveVariables);
            this.WriteFieldValue(targetField, subData, state, -1);
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
    }
}
