namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
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
public sealed partial class CStruct
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
                EnumFieldValueParser.GetEnumValue(compiledBitfieldEnum, value));
        }

        // Work out the size of the whole storage unit first, not just the small field being changed: the placed
        // unit (a packed SysV window may differ from the declared type), or the declared type when nothing placed it.
        int byteSize = state.CurrentBitfieldSize > 0
                           ? state.CurrentBitfieldSize
                           : compiledField.BitStorageSize ??
                             throw new InvalidOperationException(
                                 "Compiled bitfield has no storage size: " + compiledField.Name);
        bool storageIsLittleEndian = compiledField.BitStorageIsLittleEndian ??
                                     throw new InvalidOperationException(
                                         "Compiled bitfield has no byte order: " + compiledField.Name);
        int elementBitSize = checked(byteSize * 8);
        if (state.CurrentBitOffset + compiledField.BitSize > elementBitSize)
        {
            throw new CStructWriteException(LayoutFailures.BitfieldExceedsUnit(compiledField.Name));
        }

        // Validate the selected slice before reading or changing its shared storage unit.
        ulong fieldValue = BitfieldCodecTable.ValidateBitfieldWriteValue(compiledField.Name, compiledField.BitSize, value);

        // Bitfields share bytes. Read the existing bytes so neighboring fields survive this update.
        long curPos = state.Stream.Position;
        Span<byte> scratch = stackalloc byte[8];
        Span<byte> buffer = BinaryPrimitiveIO.UnitOf(scratch, byteSize);
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = state.Stream.Read(buffer[offset..]);
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
                buffer[offset..].Clear();
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
        state.Stream.Position = curPos;
        BinaryPrimitiveIO.WriteUnsigned(state.Stream, newValue, byteSize, storageIsLittleEndian);

        // Keep the stream at the start while later bitfields share this same unit.
        state.CurrentBitOffset += compiledField.BitSize;
        int bitOffsetInBytes = 1 + (state.CurrentBitOffset / 8);
        if (bitOffsetInBytes > byteSize)
        {
            // This field finished the unit. The next field starts in a fresh primitive value.
            state.CurrentBitOffset -= elementBitSize;
            state.BitfieldUnitOpen = false;
        }
        else
        {
            // More bitfields fit here; the placement cursor already reserved the whole unit for the next normal field.
            state.Stream.Position = curPos;
        }
    }

    /// <summary>Writes one compiled layout element, dispatching to struct, typedef, define, or field handling.</summary>
    private void WriteCStructElement(
        CStructElement element,
        object data,
        CStructElementWriterState state)
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

    /// <summary>The composite a struct root or an inline-struct typedef root writes; false for scalar roots.</summary>
    private bool TryGetRootComposite(CStructElement rootElement, [NotNullWhen(true)] out CompiledCompositeType? composite)
    {
        composite = rootElement switch
        {
            Struct s => this.compiledSizeQueries.GetCompiledComposite(s),
            Typedef { Struct: not null } t => this.compiledSizeQueries.GetCompiledComposite(t.Struct),
            _ => null,
        };
        return composite is not null;
    }

    /// <summary>
    ///     Writes one struct or union while charging exactly one active composite-depth level, or none for an anonymous
    ///     promoted struct, whose fields are members of the parent that already holds the level.
    /// </summary>
    /// <param name="composite">The struct or union to write at the stream position.</param>
    /// <param name="data">The value; for a promoted struct, the parent's value that carries its fields.</param>
    /// <param name="state">The destination, limits and variables of the write.</param>
    /// <param name="promoted">Whether <paramref name="composite"/> is an anonymous promoted member of its parent.</param>
    /// <exception cref="CStructWriteException">The value cannot be written.</exception>
    /// <exception cref="CStructWriteLimitException">A write limit, the nesting limit included, is exceeded.</exception>
    private void WriteStruct(CompiledCompositeType composite, object data, CStructElementWriterState state, bool promoted = false)
    {
        state.Options.CancellationToken.ThrowIfCancellationRequested();
        if (data is null)
        {
            throw new CStructWriteException(WriteFailures.NullComposite(composite.Name));
        }

        // A mapped-class instance becomes a StructValue of this composite's shape once, here, so every path
        // below (static plan included) reads plain members.
        data = WriteDataBinding.Materialize(data, composite);
        if (state.RejectUnknownMembers && !promoted)
        {
            // Checked before the static plan, which writes nested composites without re-entering this method. A promoted
            // struct's data is its parent's, whose check already covered every key (the parent's shape includes the
            // promoted members) and every composite nested in the promoted struct.
            RejectUnknownMembers(composite, data);
        }

        // Static write plan: a fully fixed composite is encoded into one block and written once when that
        // is exactly equivalent to the field-by-field path below (see TryWriteStaticPlan for the conditions).
        if (!composite.IsUnion && this.TryWriteStaticPlan(composite, data, state, promoted))
        {
            return;
        }

        if (!promoted)
        {
            state.EnterStructure();
        }

        try
        {
            if (composite.IsUnion)
            {
                this.WriteUnion(composite, data, state);
                return;
            }

            var variableScope = composite.HasDirectConditionalFields ? new ConditionalVariableScope(composite, state.Variables) : null;
            var selection = composite.HasDirectConditionalFields ? new ConditionalFieldSelection(this.layoutExpressionEvaluator, composite.ConditionalGroupCount, ExpressionFailureDomain.Write) : null;
            var cursor = new CompositeFieldPlacementCursor(state.Stream.Position, state.Aligned, this.BitfieldPacking, this.highBitFirst);

            foreach (CompiledField field in composite.Fields)
            {
                if (selection?.IsActive(field, state.Variables) == false)
                {
                    foreach (string name in composite.ConditionalScope!.VisibleNames[field.MemberIndex])
                    {
                        if (WriteDataBinding.TryGetMemberValue(data, name, out _))
                        {
                            throw new CStructWriteException(WriteFailures.InactiveConditionalField(name));
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

                    // An anonymous promoted member has no name to look up - splice its own children
                    // into the same `data` object the parent struct already uses. WriteFieldValue's existing
                    // Struct dispatch recurses WriteStruct with this same `data`, so the promoted member's own
                    // fields are looked up directly on it, with no nested member of its own.
                    this.WriteFieldValue(field, data, state, -1, cursor);
                    variableScope?.CompleteField(field, state.Variables);
                    continue;
                }

                if (field.IsZeroWidthBitfield)
                {
                    // A `: 0` separator writes nothing; the cursor applies its placement effect.
                    (long separatorEnd, _, _) = cursor.AdvanceToField(field);
                    state.Stream.Position = separatorEnd;
                    state.ResetBitfieldUnit();
                    variableScope?.CompleteField(field, state.Variables);
                    continue;
                }

                if (field.IsUnnamed)
                {
                    // An anonymous nonzero-width bitfield or a `_` padding field is pure padding with no
                    // caller-supplied value - there is no member to look up, so write its canonical zero bits directly.
                    this.WriteFieldValue(field, CreatePaddingValue(field), state, -1, cursor);
                    variableScope?.CompleteField(field, state.Variables);
                    continue;
                }

                // Require every ordinary struct field. Missing values would make the byte layout ambiguous.
                try
                {
                    object fieldValue = WriteDataBinding.GetMemberValueOrThrow(data, field.Name);
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
            if (!promoted)
            {
                state.ExitStructure();
            }
        }
    }

    /// <summary>
    ///     Writes an anonymous promoted union from the parent's data. The union has no name of its own, so the member
    ///     to write is chosen from the members the data supplies: the widest one, the first declared among equals, so a
    ///     value that came from a parse (where every view is present) reproduces the complete storage. The member is
    ///     then staged exactly as a named union's selected member is.
    /// </summary>
    /// <param name="composite">The anonymous union.</param>
    /// <param name="field">The union's member field in its parent.</param>
    /// <param name="data">The parent's data, which carries the union's members.</param>
    /// <param name="state">The write state, positioned anywhere; the union is placed by <paramref name="cursor"/>.</param>
    /// <param name="cursor">The parent's placement cursor.</param>
    /// <exception cref="CStructWriteException">No member is supplied, or the member cannot be written.</exception>
    private void WritePromotedUnion(CompiledCompositeType composite, CompiledField field, object data, CStructElementWriterState state, CompositeFieldPlacementCursor cursor)
    {
        (long unionPosition, _, _) = cursor.AdvanceToField(field);
        state.Stream.Position = unionPosition;
        int unionSize = this.compiledSizeQueries.GetCompiledStructSizeInBytes(composite, state.Variables, false);

        // A member without a fixed size is runtime-sized and so counts as the widest.
        CompiledField? selected = null;
        object? selectedValue = null;
        int selectedSize = -1;
        foreach (CompiledField member in composite.Fields)
        {
            int size = member.FixedStorageSize ?? int.MaxValue;
            if (size <= selectedSize)
            {
                continue;
            }

            if (composite.PromotedFields.Contains(member))
            {
                if (this.SuppliesAnyPromotedMember(member, data, state))
                {
                    (selected, selectedValue, selectedSize) = (member, data, size);
                }
            }
            else if (member.Name.Length > 0 && WriteDataBinding.TryGetMemberValue(data, member.Name, out object? value))
            {
                (selected, selectedValue, selectedSize) = (member, value, size);
            }
        }

        if (selected is null)
        {
            throw new CStructWriteException(
                "No member of the anonymous union was supplied; provide one of: " +
                string.Join(", ", composite.Shape.Names));
        }

        byte[] stagedBytes = this.StageUnionMember(composite, selected, selectedValue!, unionSize, state);
        long unionEnd = checked(unionPosition + unionSize);
        state.Stream.Write(stagedBytes, 0, stagedBytes.Length);
        state.Stream.Position = unionEnd;
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
            if (name.Length > 0 && WriteDataBinding.TryGetMemberValue(data, name, out _))
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
                WriteFailures.WholeUnionNeedsSelection(union.Name, "UnionValue.FromRaw or UnionValue.FromMember"));
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
                WriteFailures.RawStorageLengthMismatch(union.Name, unionSize, rawStorage.Length));
        }

        long unionPosition = state.Stream.Position;
        long unionEnd = checked(unionPosition + unionSize);
        if (!unionValue.HasSelection)
        {
            state.Stream.Write(rawStorage!, 0, rawStorage!.Length);
            state.Stream.Position = unionEnd;
            return;
        }

        string selectedMember = unionValue.SelectedMember!;
        CompiledField? selected = union.Fields.FirstOrDefault(
            field => string.Equals(field.Name, selectedMember, StringComparison.Ordinal));
        if (selected is null)
        {
            throw new CStructWriteException(
                WriteFailures.UnknownUnionMember(union.Name, selectedMember));
        }

        byte[] stagedBytes = this.StageUnionMember(union, selected, unionValue.SelectedValue!, unionSize, state);
        state.Stream.Write(stagedBytes, 0, stagedBytes.Length);
        state.Stream.Position = unionEnd;
    }

    /// <summary>
    ///     Builds a union's complete extent with one member written into it, away from the destination, so a failure
    ///     leaves the destination unchanged. The extent starts as zeroes, or - for an update that keeps union storage
    ///     (<see cref="UpdateOptions.ClearUnionStorage"/> false) - as the existing bytes, and the member is written over it.
    /// </summary>
    /// <param name="union">The union, named or anonymous.</param>
    /// <param name="member">The member to write.</param>
    /// <param name="value">The member's value; for a promoted member, the data that carries its fields.</param>
    /// <param name="unionSize">The union's size in bytes.</param>
    /// <param name="state">The write state, positioned at the union's first byte; the position is restored.</param>
    /// <returns>The staged extent.</returns>
    /// <exception cref="CStructReadException">Storage is kept but the existing extent is not complete.</exception>
    /// <exception cref="CStructWriteException">The member cannot be written.</exception>
    private byte[] StageUnionMember(CompiledCompositeType union, CompiledField member, object value, int unionSize, CStructElementWriterState state)
    {
        byte[] stagedBytes = new byte[unionSize];
        if (state.Options is UpdateOptions { ClearUnionStorage: false, })
        {
            long unionPosition = state.Stream.Position;
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

        using var stagingStream = new MemoryStream(stagedBytes, writable: true);
        var stagingState = new CStructElementWriterState(
            stagingStream,
            new LayoutVariables(state.Variables),
            state.Aligned,
            state.Options,
            state.StructureDepth);
        try
        {
            this.WriteFieldValue(member, value, stagingState, 0);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                          ArgumentException or ArithmeticException or
                                          FormatException or InvalidCastException or
                                          NotSupportedException)
        {
            string memberName = union.Name.Length > 0 ? union.Name + "." + member.Name : member.Name;
            throw new CStructWriteException($"Cannot write selected union member '{memberName}'.", exception);
        }

        return stagedBytes;
    }

    /// <summary>
    ///     <see cref="UnknownMemberPolicy.Reject"/>: every member the supplied value carries must be one the composite
    ///     declares, matched by exact key (the lookup the writer performs). A parsed <see cref="UnionValue"/> is
    ///     trusted; a mapped class was materialized into the composite's own shape and so cannot carry an unknown key.
    /// </summary>
    private static void RejectUnknownMembers(CompiledCompositeType composite, object data)
    {
        StructShape shape = composite.Shape;
        if (data is UnionValue)
        {
            return;
        }

        foreach (string key in WriteDataBinding.EnumerateMemberNames(data))
        {
            if (!shape.TryGetIndex(key, out _))
            {
                throw UnknownMember(composite, key);
            }
        }

        RejectUnknownNestedMembers(composite, data);
    }

    /// <summary>Applies the same check to every by-value nested struct the composite declares, arrays included.</summary>
    private static void RejectUnknownNestedMembers(CompiledCompositeType composite, object data)
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
                RejectUnknownNestedMembers(nested, data);
                continue;
            }

            if (field.IsUnnamed || !WriteDataBinding.TryGetMemberValue(data, field.Name, out object? value) || value is null)
            {
                continue;
            }

            try
            {
                if (field.Array.Kind == CompiledArrayKind.Scalar)
                {
                    RejectUnknownMembers(nested, WriteDataBinding.Materialize(value, nested));
                }
                else if (value is System.Collections.IEnumerable elements and not string)
                {
                    foreach (object? element in elements)
                    {
                        if (element is not null)
                        {
                            RejectUnknownMembers(nested, WriteDataBinding.Materialize(element, nested));
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

    /// <summary>Writes one field and publishes numeric values needed by later layout expressions.</summary>
    /// <param name="compiledField">The prepared field, including its array and storage rules.</param>
    /// <param name="value">The caller's scalar or collection value; only scalar pointers accept null.</param>
    /// <param name="state">The destination, limits and variable environment updated by this write.</param>
    /// <param name="unionPosition">The enclosing union's stream byte position, or -1 outside a union.</param>
    /// <param name="cursor">The enclosing struct's placement cursor, or null for root, union or selected-field dispatch.</param>
    /// <exception cref="CStructWriteException">The value or its array length cannot be encoded.</exception>
    private void WriteFieldValue(
        CompiledField compiledField,
        object value,
        CStructElementWriterState state,
        long unionPosition,
        CompositeFieldPlacementCursor? cursor = null)
    {
        if (value is null &&
            (compiledField.PointerDepth == 0 || compiledField.Array.Kind != CompiledArrayKind.Scalar))
        {
            throw new CStructWriteException(
                WriteFailures.NullForNonPointer(compiledField.Name));
        }

        int count = this.WrittenElementCount(compiledField, state, out CompiledField valueField, out bool unknownArray);
        bool isArray = compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime || unknownArray;
        if (unknownArray && PrimitiveCodecs.IsVariableLengthType(valueField.TypeSpelling))
        {
            // A terminated string view writes one value through its string codec, not element by element.
            isArray = false;
        }

        bool positionIsResolvedTarget = state.PositionIsResolvedTarget;
        state.PositionIsResolvedTarget = false;
        bool standalone = PlaceWrittenField(compiledField, valueField, state, unionPosition, cursor, positionIsResolvedTarget);

        BigInteger? writtenEnumValue = null;
        if (isArray)
        {
            this.WriteArrayValue(compiledField, valueField, value!, state, count, unknownArray, standalone);
        }
        else
        {
            writtenEnumValue = this.WriteSingleFieldValue(valueField, value!, state);
        }

        if (!standalone && compiledField.BitSize == 0)
        {
            // Bitfields skip this: the cursor already reserved their whole storage unit's span when it opened,
            // mirroring how CStructAddressResolver's own cursor usage never completes a bitfield.
            cursor!.CompleteField(state.Stream.Position);
        }

        // Later fields may use this field in an expression, so keep the writer's variable map in step with the bytes;
        // see LayoutVariableCapture for the rule every path shares.
        if (compiledField.CapturesLayoutVariable || state.CaptureAllLayoutVariables)
        {
            LayoutVariableCapture.Capture(state.Variables, compiledField.Name, compiledField, writtenEnumValue is BigInteger exact ? exact : value);
            state.PublishQualified(compiledField.Name);
        }
    }

    /// <summary>
    ///     The element count a field's declaration gives for a write: 1 for a scalar, the product of a multidimensional
    ///     array's fixed dimensions, or a one-dimensional count evaluated against the variables written so far. A
    ///     data-sized or unsized array (<paramref name="unknownArray"/>) writes the elements the value supplies; an
    ///     unsized character array is written through its terminated string codec (<paramref name="valueField"/>).
    /// </summary>
    /// <param name="compiledField">The field.</param>
    /// <param name="state">The write state, whose variables and limits apply.</param>
    /// <param name="valueField">The field the value is written as: itself, or its terminated string view.</param>
    /// <param name="unknownArray">Whether the value, not the declaration, gives the element count.</param>
    /// <returns>The declared count; 1 when <paramref name="unknownArray"/>.</returns>
    /// <exception cref="CStructWriteException">The count is negative.</exception>
    /// <exception cref="CStructWriteLimitException">The count is past <c>MaxArrayElements</c>.</exception>
    private int WrittenElementCount(CompiledField compiledField, CStructElementWriterState state, out CompiledField valueField, out bool unknownArray)
    {
        valueField = compiledField;
        unknownArray = compiledField.Array.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated or CompiledArrayKind.Flexible;
        if (compiledField.Array.Kind == CompiledArrayKind.Flexible)
        {
            // C-style char[] has no fixed count here. Select a string handler that writes its terminator.
            if (compiledField.IsCharElement)
            {
                valueField = compiledField.SelectPointerTarget(0, CharacterFieldTypes.CstringType.Name, this.PointerSize);
            }
            else if (compiledField.IsWideCharElement)
            {
                valueField = compiledField.SelectPointerTarget(0, CharacterFieldTypes.GetStringPointerHandlerKey(compiledField.TypeSpelling), this.PointerSize);
            }
        }

        if (unknownArray || compiledField.Array.Kind == CompiledArrayKind.Scalar)
        {
            return 1;
        }

        // The count is checked in the expression domain, so a count beyond Int32 fails the limit check instead of wrapping.
        Int128 count;
        if (compiledField.Array.Dimensions.Length > 1)
        {
            // Every dimension of a multidimensional array is fixed, so the total leaf count is known without evaluating
            // an expression against the current write state.
            count = compiledField.Array.TotalFixedElementCount ??
                    throw new InvalidOperationException("Multidimensional array has no fixed total element count: " + compiledField.Name);
        }
        else
        {
            // Fixed array counts may refer to an earlier field or #define, so calculate them from the current state.
            count = this.layoutExpressionEvaluator.Evaluate(
                compiledField.Array.CountExpression ??
                throw new InvalidOperationException("Compiled array has no count expression: " + compiledField.Name),
                state.Variables,
                "array length for " + compiledField.Name,
                ExpressionFailureDomain.Write);
            if (count < 0)
            {
                throw new CStructWriteException(LayoutFailures.NegativeArrayLength(compiledField.Name));
            }
        }

        if (count > state.Options.MaxArrayElements)
        {
            throw new CStructWriteLimitException(WriteFailures.ArrayLengthLimit(compiledField.Name));
        }

        return (int)count;
    }

    /// <summary>
    ///     Places a field for a write: a union member at the union's start, a struct member where the composite cursor
    ///     puts it (with its bitfield unit, once for every array element). A standalone field - a root declaration, a
    ///     union member, or a resolved path target - has no cursor, starts with no open bitfield unit, and starts exactly
    ///     at the stream position, as in the reader: alignment is measured from the value's own first byte.
    /// </summary>
    /// <param name="compiledField">The field.</param>
    /// <param name="valueField">The field the value is written as, whose alignment the composite cursor applies.</param>
    /// <param name="state">The write state, whose stream and bitfield unit are set.</param>
    /// <param name="unionPosition">The union's start, or -1.</param>
    /// <param name="cursor">The containing struct's cursor, or <see langword="null"/>.</param>
    /// <param name="positionIsResolvedTarget">Whether the stream is at the field's resolved address.</param>
    /// <returns>Whether the field is standalone.</returns>
    private static bool PlaceWrittenField(CompiledField compiledField, CompiledField valueField, CStructElementWriterState state, long unionPosition, CompositeFieldPlacementCursor? cursor, bool positionIsResolvedTarget)
    {
        bool standalone = cursor is null || unionPosition != -1 || positionIsResolvedTarget;
        if (unionPosition != -1)
        {
            // Each union member begins at the same address, just as it does while reading.
            state.Stream.Position = unionPosition;
            state.ResetBitfieldUnit();
        }

        if (standalone)
        {
            if (compiledField.BitSize > 0 && state.BitfieldUnitSeeded)
            {
                // A resolved target arrives with its placed unit; nothing to derive.
                state.BitfieldUnitSeeded = false;
            }
            else if (compiledField.BitSize > 0)
            {
                // A standalone bitfield opens its own storage unit.
                state.BitfieldUnitOpen = true;
                state.CurrentBitfieldSize = compiledField.BitStorageSize ??
                                            throw new InvalidOperationException("Compiled bitfield has no storage size: " + compiledField.Name);
            }

            return true;
        }

        (long fieldStart, int bitOffset, int unitSize) = cursor!.AdvanceToField(valueField);
        state.Stream.Position = fieldStart;
        if (compiledField.BitSize > 0)
        {
            state.CurrentBitOffset = bitOffset;
            state.BitfieldUnitOpen = true;
            state.CurrentBitfieldSize = unitSize;
        }
        else
        {
            state.ResetBitfieldUnit();
        }

        return false;
    }

    /// <summary>
    ///     Writes an array value: a multidimensional array as its flattened row-major leaves (a character table row by
    ///     row), a character array or encoded text buffer as one string, a numeric array as one block where it can be,
    ///     and anything else element by element (a data-sized terminated array then gets its all-zero terminator).
    /// </summary>
    /// <param name="compiledField">The array field.</param>
    /// <param name="valueField">The field the value is written as.</param>
    /// <param name="value">The caller's collection or string.</param>
    /// <param name="state">The write state.</param>
    /// <param name="count">The declared element count; ignored when <paramref name="unknownArray"/>.</param>
    /// <param name="unknownArray">Whether the value gives the element count.</param>
    /// <param name="standalone">Whether the field has no composite cursor.</param>
    /// <exception cref="CStructWriteException">The value has the wrong number of elements.</exception>
    /// <exception cref="CStructWriteLimitException">The value has more elements than <c>MaxArrayElements</c>.</exception>
    private void WriteArrayValue(CompiledField compiledField, CompiledField valueField, object value, CStructElementWriterState state, int count, bool unknownArray, bool standalone)
    {
        if (compiledField.Array.Dimensions.Length > 1)
        {
            // Every dimension is fixed, so the value is an N-deep nested collection to flatten - the reader's
            // flat-then-reshape in reverse - and then written as the same flat sequence a 1-D array writes.
            int[] dimensionSizes = FixedDimensionSizes(compiledField);
            if (valueField.IsCharacterArray)
            {
                // The innermost dimension of a fixed string table collapses one caller-supplied string per row, like a
                // one-dimensional char[32]; only the outer dimensions flatten.
                int rowSize = dimensionSizes[^1];
                foreach (object row in this.FlattenNestedArrayValues(value, dimensionSizes[..^1], compiledField.Name))
                {
                    string rowString = row as string ?? WriteValueMaterialization.ConvertToBoundedCharString(row, rowSize, compiledField.Name);
                    this.WriteFixedCharArray(compiledField, rowString, rowSize, state);
                }
            }
            else
            {
                List<object> leaves = this.FlattenNestedArrayValues(value, dimensionSizes, compiledField.Name);
                for (int i = 0; i < leaves.Count; i++)
                {
                    _ = this.WriteSingleFieldValue(compiledField, leaves[i], state);
                }
            }

            return;
        }

        if (valueField.IsCharacterArray || (!compiledField.IsPointer && BoundedTextCodec.IsType(compiledField.TypeSpelling)))
        {
            // Character arrays accept either one string or a collection of characters and always fill the declared size.
            string text = value as string ?? WriteValueMaterialization.ConvertToBoundedCharString(value, count, compiledField.Name);
            this.WriteFixedCharArray(compiledField, text, count, state);
            return;
        }

        if (!unknownArray && !standalone && count <= state.Options.MaxArrayElements && this.TryWriteTypedArrayBlock(compiledField, value, count, state))
        {
            // Written as one block: the same bytes and budget charge as the element loop below.
            return;
        }

        // Other arrays are written item by item so nested structs, enums, and pointers use their normal logic.
        IList<object> items = WriteValueMaterialization.ConvertToObjectList(value, unknownArray ? state.Options.MaxArrayElements : count, compiledField.Name);
        int written = unknownArray ? items.Count : count;
        if (written > state.Options.MaxArrayElements)
        {
            throw new CStructWriteLimitException(WriteFailures.ArrayLengthLimit(compiledField.Name));
        }

        if (!unknownArray && items.Count != written)
        {
            throw new CStructWriteException(WriteFailures.ArrayLengthMismatch(compiledField.Name, written, items.Count));
        }

        for (int i = 0; i < written; i++)
        {
            if (compiledField.TargetComposite is not null)
            {
                state.Options.CancellationToken.ThrowIfCancellationRequested();
            }

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
            throw new CStructWriteException(WriteFailures.ArrayLengthMismatch(fieldName, dimensionSizes[0], level.Count));
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
                throw new CStructWriteException(WriteFailures.Utf16CapacityOdd);
            }

            byte[] encoded;
            try
            {
                int length = BoundedTextCodec.GetByteCount(compiledField.TypeSpelling, value);
                if (length > count)
                {
                    throw new CStructWriteException(WriteFailures.BoundedTextTooLong(compiledField.Name, length, count));
                }

                encoded = BoundedTextCodec.Encode(compiledField.TypeSpelling, value);
            }
            catch (EncoderFallbackException exception)
            {
                throw new CStructWriteException(WriteFailures.EncodingUnrepresentable, exception);
            }

            state.Stream.Write(encoded, 0, encoded.Length);
            state.WriteZeroes(count - encoded.Length);
            return;
        }

        // Fixed arrays must consume their declared byte count. Reject too much input instead of silently truncating it.
        if (value.Length > count)
        {
            throw new CStructWriteException(WriteFailures.FixedTextTooLong(compiledField.Name, value.Length, count));
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
                throw new CStructWriteException(WriteFailures.InvalidWideText, exception);
            }

            state.Stream.Write(encoded, 0, encoded.Length);
            return;
        }

        if (TryWriteNarrowTextBlock(padded, state))
        {
            return;
        }

        foreach (char c in padded)
        {
            this.WritePrimitiveValue(compiledField, state.Stream, c);
        }
    }

    /// <summary>
    ///     Writes the characters of a narrow <c>char[n]</c> buffer as one block, when the per-character writes could not
    ///     fail part-way: every character fits one byte (Latin-1) and the destination and budget hold the whole block.
    ///     Returns <see langword="false"/>, having written nothing, otherwise, and the per-character writer reports the
    ///     failure at the character where it always has.
    /// </summary>
    /// <param name="text">The padded text, one byte per character.</param>
    /// <param name="state">The write state.</param>
    /// <returns>Whether the text was written.</returns>
    private static bool TryWriteNarrowTextBlock(string text, CStructElementWriterState state)
    {
        if (!CanWriteBlock(state, text.Length))
        {
            return false;
        }

        foreach (char character in text)
        {
            if (character > byte.MaxValue)
            {
                return false;
            }
        }

        byte[]? rented = null;
        Span<byte> block = text.Length <= StackStagingLimit ? stackalloc byte[text.Length] : (rented = ArrayPool<byte>.Shared.Rent(text.Length)).AsSpan(0, text.Length);
        try
        {
            for (int index = 0; index < text.Length; index++)
            {
                block[index] = (byte)text[index];
            }

            state.BudgetStream.WriteBlock(block, block.Length);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        return true;
    }

    /// <summary>
    ///     Writes a numeric array whose storage is the field's own CLR type (a parsed <see cref="PrimitiveArray{T}"/> or a
    ///     plain <c>T[]</c> of exactly <paramref name="count"/> elements) as one block, with the vectorized byte-order
    ///     conversion of the static write plan. Only for a plain numeric field whose destination and budget hold the
    ///     whole block; anything else returns <see langword="false"/> and takes the element loop.
    /// </summary>
    /// <param name="field">The array field.</param>
    /// <param name="value">The caller's array value.</param>
    /// <param name="count">The declared element count.</param>
    /// <param name="state">The write state.</param>
    /// <returns>Whether the array was written.</returns>
    private bool TryWriteTypedArrayBlock(CompiledField field, object? value, int count, CStructElementWriterState state)
    {
        if (value is null || count <= 0 || field.BitSize != 0 || field.PointerDepth > 0 || field.Enum is not null || field.Composite is not null ||
            field.IsUnnamed || !field.Codec.IsFixedWidthNumeric || field.Array.Kind is not (CompiledArrayKind.Fixed or CompiledArrayKind.Runtime) ||
            field.Array.Dimensions.Length > 1)
        {
            return false;
        }

        long length = (long)count * field.Codec.Size;
        if (length > ReadBlock.Size || !CanWriteBlock(state, (int)length))
        {
            return false;
        }

        byte[]? rented = null;
        Span<byte> block = length <= StackStagingLimit ? stackalloc byte[(int)length] : (rented = ArrayPool<byte>.Shared.Rent((int)length)).AsSpan(0, (int)length);
        try
        {
            if (!TryWriteTypedArray(field, block, value, count))
            {
                return false;
            }

            state.BudgetStream.WriteBlock(block, block.Length);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        return true;
    }

    /// <summary>
    ///     Whether <paramref name="length"/> bytes can be written as one block with the outcome of writing them one by
    ///     one: the budget allows them all, and the destination is caller memory with room for them or the writer's own
    ///     growable buffer (an update, or any other stream, keeps the byte-by-byte path).
    /// </summary>
    private static bool CanWriteBlock(CStructElementWriterState state, int length)
    {
        WriteBudgetStream stream = state.BudgetStream;
        if (state.Options is UpdateOptions || stream.IsSparseUpdate || state.GeneralPathOnly || !stream.CanAffordBlock(length, length))
        {
            return false;
        }

        return stream.Inner switch
        {
            FixedBufferStream fixedBuffer => stream.Position + length <= fixedBuffer.Capacity,
            OwnedMemoryStream => true,
            _ => false,
        };
    }

    /// <summary>
    ///     Advances past the final tail padding of an ordinary struct. New output receives explicit zero bytes because
    ///     seeking past the end of a <see cref="MemoryStream"/> does not extend its length; updates preserve bytes that
    ///     already occupy padding because callers asked to change a field, not to normalize surrounding storage.
    /// </summary>
    private void CompleteStructTailPadding(CompiledCompositeType composite, CStructElementWriterState state, CompositeFieldPlacementCursor cursor)
    {
        // The cursor already tracks the position past any dangling bitfield unit's full reserved span - trust it
        // rather than state.Stream.Position, which a shared bitfield write may have rewound mid-unit.
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
        BinaryPrimitiveIO.WriteUnsigned(stream, value, this.PointerSize, this.IsLittleEndian);
    }

    /// <summary>Writes one scalar or array element, using zero storage for unnamed custom-codec padding.</summary>
    /// <param name="compiledField">The compiled field or element view, including its fixed storage extent.</param>
    /// <param name="value">The caller value, or a synthesized zero for unnamed padding.</param>
    /// <param name="state">The destination, byte budget and current bitfield storage state.</param>
    /// <returns>The exact integer for an enum value, or null for other field kinds.</returns>
    /// <exception cref="CStructWriteException">The value cannot be encoded or an output limit is exceeded.</exception>
    /// <exception cref="InvalidOperationException">The field has no registered storage writer.</exception>
    private BigInteger? WriteSingleFieldValue(
        CompiledField compiledField,
        object value,
        CStructElementWriterState state)
    {
        // Resolve these once so each case below can choose the smallest correct writing path.
        string fieldTypeName = compiledField.TypeSpelling;
        bool isKnownFieldType = compiledField.HasCodec;

        if (compiledField.IsUnnamed && compiledField.Codec.IsCustom)
        {
            // Padding describes storage, not a caller value. Its validated fixed extent must stay zero even
            // when a custom codec maps numeric zero to another encoding or does not accept numeric values.
            state.WriteZeroes(compiledField.FixedElementSize!.Value);
            return null;
        }

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
            BigInteger enumValue = EnumFieldValueParser.GetEnumValue(compiledEnum, value);
            (this.codecs.WriterOf(compiledField) ??
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

            this.WriteStruct(strct, value, state, compiledField.IsPromotedComposite);
            state.QualifiedPrefix = outerPrefix;
            return null;
        }

        if (!isKnownFieldType)
        {
            throw new InvalidOperationException($"No handler for field type {fieldTypeName}");
        }

        // The remaining case is a normal primitive type registered when this layout was created.
        this.WritePrimitiveValue(compiledField, state.Stream, value);
        return null;
    }

    /// <summary>Encodes a caller value with a field's compiled primitive codec at the stream position.</summary>
    /// <remarks>An in-place update of a variable-length (LEB128 or unsized custom) value must keep its existing encoded length. Only expected caller-value conversion failures become <see cref="CStructWriteException"/>.</remarks>
    /// <param name="field">The field whose codec encodes the value.</param>
    /// <param name="stream">The destination, positioned at the value.</param>
    /// <param name="value">The caller value.</param>
    private void WritePrimitiveValue(
        CompiledField field,
        Stream stream,
        object value)
    {
        Action<Stream, object> writer = this.codecs.WriterOf(field) ??
                                        throw new InvalidOperationException(
                                            "Compiled field has no writer: " + field.CodecName);
        try
        {
            if (stream is WriteBudgetStream { IsSparseUpdate: true } && (field.Codec.IsLeb128 || (field.Codec.IsCustom && !field.FixedElementSize.HasValue)))
            {
                long start = stream.Position;
                _ = this.codecs.ReaderOf(field)!(stream);
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

    /// <summary>The shared unwritable-value text (<see cref="WriteFailures.UnwritableValue"/>) for one compiled field.</summary>
    private static string DescribeUnwritableValue(object? value, CompiledField field)
        => WriteFailures.UnwritableValue(value, field.TypeSpelling, WriteFailures.AcceptedRange(field.Codec.Kind));

    /// <summary>
    ///     Creates a new byte array while snapshotting expression variables from a read-only caller view.
    /// </summary>
    /// <param name="elementNameOrPath">The case-sensitive root name or nested field path to encode.</param>
    /// <param name="data">The value to encode.</param>
    /// <param name="variables">The caller's layout variables; they are copied and never mutated.</param>
    /// <param name="options">Write limits and pointer settings; <see langword="null"/> uses the defaults.</param>
    /// <returns>A new caller-owned array holding exactly the encoded bytes.</returns>
    internal byte[] SerializeCore(
        string elementNameOrPath,
        object data,
        LayoutVariableInput variables,
        WriteOptions? options = null)
    {
        // Serialize is the convenience entry point: write to a temporary stream, then hand its complete contents to the caller.
        using var stream = new OwnedMemoryStream();
        this.WriteStreamCore(stream, elementNameOrPath, data, variables, options);
        return stream.ToArray();
    }

    /// <summary>
    ///     Updates a selected value while snapshotting expression variables from a read-only caller view. All
    ///     library-detectable failures occur against bounded sparse staging before destination commit, and the
    ///     replacement cannot extend the existing stream.
    /// </summary>
    /// <param name="stream">
    ///     The caller-owned readable, writable, seekable stream whose current position is the operation origin.
    /// </param>
    /// <param name="elementNameOrPath">The case-sensitive path of the existing value to replace.</param>
    /// <param name="value">The replacement value.</param>
    /// <param name="variables">The caller's layout variables; they are copied and never mutated.</param>
    /// <param name="options">
    ///     Traversal limits, pointer rules, and union handling; <see langword="null"/> uses the documented defaults.
    /// </param>
    /// <exception cref="ArgumentException">The stream is not readable, writable, and seekable.</exception>
    /// <exception cref="CStructPathException">The path is empty or cannot be resolved.</exception>
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

        EngineSelector.Decide(effectiveOptions.EngineSelection, EngineOperation.Update);
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
            bool variableExtentTarget = false;
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
                if (originalLayout is null && (writableCompiledField.Codec.IsTerminatedText || writableCompiledField.HasTerminatedCodec || writableCompiledField.Array.Kind == CompiledArrayKind.Terminated))
                {
                    // A terminated value has no fixed extent: a replacement of another encoded length would move
                    // every later field, so the whole layout is captured and compared, as for a conditional root.
                    layoutVariables = new Dictionary<string, Expr>(effectiveVariables);
                    originalLayout = this.CaptureUpdateLayout(readState.Stream, originalPosition, rootElement, layoutVariables, readOptions);
                    variableExtentTarget = true;
                    readState.Stream.Position = target.Address;
                }

                state.PositionIsResolvedTarget = true;
                if (target.BitStorageSize > 0)
                {
                    // A later bitfield starts at the same byte address as its predecessors. Seed the shared writer
                    // state from the semantic target so only the selected bit range changes.
                    state.CurrentBitOffset = target.BitOffset;
                    state.BitfieldUnitOpen = true;
                    state.CurrentBitfieldSize = target.BitStorageSize;
                    state.BitfieldUnitSeeded = true;
                }

                this.WriteFieldValue(writableCompiledField, value, state, -1);
            }

            // The caller sees writes only after every library-detectable writer failure has been ruled out.
            if (originalLayout is not null)
            {
                const string ExtentChanged = "Update changes the extent of a terminated value and would move the fields that follow; the replacement must have the same encoded length, or serialize a new buffer instead.";
                (string Path, long Start, long End)[] changedLayout;
                try
                {
                    changedLayout = this.CaptureUpdateLayout(stagingStream, originalPosition, rootElement, layoutVariables!, readOptions);
                }
                catch (CStructException inner) when (variableExtentTarget)
                {
                    // The moved fields no longer read at all (a later field ran past the end, a pointer went astray).
                    throw new CStructWriteException(ExtentChanged, inner);
                }

                if (!originalLayout.SequenceEqual(changedLayout))
                {
                    throw new CStructWriteException(variableExtentTarget ? ExtentChanged : "Update changes the active conditional storage layout; serialize a new buffer instead.");
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
    /// <param name="stream">
    ///     The caller-owned writable, seekable destination; writing starts at its current position, and fields written
    ///     before a later failure remain in it.
    /// </param>
    /// <param name="elementNameOrPath">The case-sensitive root name or nested field path to write.</param>
    /// <param name="data">The value to encode.</param>
    /// <param name="variables">The caller's layout variables; they are copied and never mutated.</param>
    /// <param name="options">Write limits and pointer settings; <see langword="null"/> uses the defaults.</param>
    /// <exception cref="ArgumentException">The stream is not writable and seekable.</exception>
    /// <exception cref="CStructPathException">The path is empty or cannot be resolved.</exception>
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

        EngineSelector.Decide(effectiveOptions.EngineSelection, EngineOperation.Write);

        // Callers may pass either { root: ... } or the root object itself; accept both forms at the public boundary.
        object rootData = WriteDataBinding.NormalizeRootData(data, rootName);

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

            // A mapped-class root becomes a StructValue first so its members can be walked like any other root object.
            if (rootData is not null && !WriteDataBinding.IsMemberSource(rootData) && this.TryGetRootComposite(rootElement, out CompiledCompositeType? rootComposite))
            {
                rootData = WriteDataBinding.Materialize(rootData, rootComposite)!;
            }

            object subData = rootData!;
            if (WriteDataBinding.TryGetMemberValue(rootData!, childSegments[0].Name, out _))
            {
                // If the caller supplied a complete root object, walk down to the matching nested source value.
                subData = WriteDataBinding.ResolveDataPath(rootData!, childSegments);
            }

            // Separately resolve the layout shape so the writer knows whether the selected target is a field, struct, or typedef.
            CompiledField targetField = this.compilation.ResolveElementPath(rootElement, childSegments, effectiveVariables);
            this.WriteFieldValue(targetField, subData, state, -1);
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
    }
}
