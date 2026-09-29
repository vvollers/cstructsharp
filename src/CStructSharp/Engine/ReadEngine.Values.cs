namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Generated;
using CStructSharp.Reading;
using CStructSharp.Values;

/// <summary>The value reads of the compiled engine: scalars through their codecs, and every array shape stage 2 reads.</summary>
internal static partial class ReadEngine
{
    /// <summary>
    ///     Reads one value exactly as the codec's stream reader does (the reader the interpreter calls for every codec
    ///     that is not a fixed-width number, for enum storage and for array elements it does not read as a block): a
    ///     one-byte value through <see cref="IReadCursor.ReadByteExactly"/>, a wider fixed-size one through
    ///     <see cref="IReadCursor.ReadExactly"/>, LEB128 byte by byte, text up to its terminator, and a UUID through the
    ///     plain exact read whose short-read failure it words itself. The boxed CLR type is the reader's.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The value's codec.</param>
    /// <param name="scratch">A buffer of at least 16 bytes for fixed-size values.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="CStructReadException">The input ends early or holds an invalid value.</exception>
    /// <exception cref="CStructReadLimitException">The bytes exceed a budget.</exception>
    private static object ReadCodecValue<TCursor>(ref TCursor cursor, PrimitiveCodec codec, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        bool littleEndian = codec.LittleEndian;
        switch (codec.Kind)
        {
        case PrimitiveCodecKind.Char:
            return (char)cursor.ReadByteExactly();
        case PrimitiveCodecKind.Latin1:
        case PrimitiveCodecKind.Cp437:
        case PrimitiveCodecKind.Utf8Unit:
        case PrimitiveCodecKind.Utf16LeUnit:
        case PrimitiveCodecKind.Utf16BeUnit:
            // A text unit read alone is its byte; only an array of them is decoded as text.
            return cursor.ReadByteExactly();
        case PrimitiveCodecKind.WChar:
            return Codec.ReadChar(ReadExact(ref cursor, scratch[..2]), littleEndian);
        case PrimitiveCodecKind.Int48:
            return Codec.ReadInt48(ReadExact(ref cursor, scratch[..6]), littleEndian);
        case PrimitiveCodecKind.UInt48:
            return Codec.ReadUInt48(ReadExact(ref cursor, scratch[..6]), littleEndian);
        case PrimitiveCodecKind.Int128:
            return Codec.ReadInt128(ReadExact(ref cursor, scratch[..16]), littleEndian);
        case PrimitiveCodecKind.UInt128:
            return Codec.ReadUInt128(ReadExact(ref cursor, scratch[..16]), littleEndian);
        case PrimitiveCodecKind.Float16:
            return Codec.ReadHalf(ReadExact(ref cursor, scratch[..2]), littleEndian);
        case PrimitiveCodecKind.Fixed16_16:
            return Codec.DecodeFixedPoint(Codec.ReadInt32(ReadExact(ref cursor, scratch[..4]), littleEndian), 16);
        case PrimitiveCodecKind.UFixed16_16:
            return Codec.DecodeFixedPoint(Codec.ReadUInt32(ReadExact(ref cursor, scratch[..4]), littleEndian), 16);
        case PrimitiveCodecKind.Fixed2_30:
            return Codec.DecodeFixedPoint(Codec.ReadInt32(ReadExact(ref cursor, scratch[..4]), littleEndian), 30);
        case PrimitiveCodecKind.UFixed8_8:
            return Codec.DecodeFixedPoint(Codec.ReadUInt16(ReadExact(ref cursor, scratch[..2]), littleEndian), 8);
        case PrimitiveCodecKind.Uuid:
        case PrimitiveCodecKind.Guid:
            {
                Span<byte> bytes = scratch[..16];
                try
                {
                    cursor.ReadExactlyOrEndOfStream(bytes);
                }
                catch (EndOfStreamException exception)
                {
                    throw new CStructReadException(ReadFailures.IdentifierShortRead, exception);
                }

                return Codec.ReadGuid(bytes, codec.Kind == PrimitiveCodecKind.Uuid);
            }

        case PrimitiveCodecKind.ULeb128_32:
            return (uint)ReadLeb128(ref cursor, 32, false);
        case PrimitiveCodecKind.ULeb128_64:
            return ReadLeb128(ref cursor, 64, false);
        case PrimitiveCodecKind.SLeb128_32:
            return unchecked((int)ReadLeb128(ref cursor, 32, true));
        case PrimitiveCodecKind.SLeb128_64:
            return unchecked((long)ReadLeb128(ref cursor, 64, true));
        case PrimitiveCodecKind.TerminatedAscii:
            return cursor.ReadTerminatedString(PrimitiveCodecs.StrictAsciiEncoding, codec.Terminator);
        case PrimitiveCodecKind.TerminatedUtf8:
            return cursor.ReadTerminatedString(PrimitiveCodecs.StrictUtf8Encoding, codec.Terminator);
        case PrimitiveCodecKind.TerminatedUtf16:
            return cursor.ReadTerminatedString(littleEndian ? PrimitiveCodecs.StrictUtf16LittleEndianEncoding : PrimitiveCodecs.StrictUtf16BigEndianEncoding, codec.Terminator);
        default:
            if (!codec.IsFixedWidthNumeric)
            {
                throw new InvalidOperationException("The compiled engine has no reader for codec " + codec.Kind + ".");
            }

            // A fixed-width number read through its codec reader (enum storage): one byte through ReadByte, a wider
            // value through an exact read, never straight from memory.
            Span<byte> value = scratch[..codec.Size];
            if (value.Length == 1)
            {
                value[0] = cursor.ReadByteExactly();
            }
            else
            {
                cursor.ReadExactly(value);
            }

            return codec.ReadNumeric(value);
        }
    }

    /// <summary>Fills <paramref name="bytes"/> through <see cref="IReadCursor.ReadExactly"/> and returns it.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="bytes">The span to fill.</param>
    /// <returns><paramref name="bytes"/>, filled.</returns>
    private static ReadOnlySpan<byte> ReadExact<TCursor>(ref TCursor cursor, Span<byte> bytes)
        where TCursor : struct, IReadCursor
    {
        cursor.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>Reads a LEB128 integer one byte at a time, as <c>Leb128Codec.Read</c> does, failing where it fails.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="width">The declared width in bits.</param>
    /// <param name="signed">Whether the encoding is signed.</param>
    /// <returns>The raw 64-bit value.</returns>
    private static ulong ReadLeb128<TCursor>(ref TCursor cursor, int width, bool signed)
        where TCursor : struct, IReadCursor
    {
        var decoder = new Leb128Decoder(width, signed);
        while (true)
        {
            if (decoder.Push(cursor.ReadByteExactly(), out ulong value))
            {
                return value;
            }
        }
    }

    /// <summary>
    ///     Reads a numeric array of a member its composite placed: a non-empty one as one typed block (the interpreter's
    ///     bulk path, failing at its block granularity), an empty one as the empty typed array its element loop leaves.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="count">The element count.</param>
    /// <returns>The typed array.</returns>
    private static IList<object?> ReadNumericArray<TCursor>(ref TCursor cursor, PrimitiveCodec codec, int count)
        where TCursor : struct, IReadCursor
        => count > 0 ? cursor.ReadPrimitiveArray(codec, count) : PrimitiveArrayReader.FromBoxed(PrimitiveArrayReader.GetElementType(codec), Array.Empty<object?>())!;

    /// <summary>
    ///     Reads a numeric array no composite placed (a root) one element at a time, as the interpreter's element loop
    ///     does - each element straight from memory when it is there, else through the codec's reads - then gives the
    ///     elements the typed shape.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="count">The element count.</param>
    /// <param name="scratch">A buffer of at least one element.</param>
    /// <returns>The typed array.</returns>
    private static IList<object?> ReadNumericElements<TCursor>(ref TCursor cursor, PrimitiveCodec codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        Span<byte> element = scratch[..codec.Size];
        for (int index = 0; index < count; index++)
        {
            elements.Add(codec.ReadNumeric(cursor.ReadFixed(element)));
        }

        return PrimitiveArrayReader.FromBoxed(PrimitiveArrayReader.GetElementType(codec), elements)!;
    }

    /// <summary>Reads an array of a codec that is not a fixed-width number (<c>int48</c>, <c>float16</c>, UUID, LEB128, terminated text, ...) into a list.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="count">The element count.</param>
    /// <param name="scratch">A buffer for fixed-size elements.</param>
    /// <returns>The elements.</returns>
    private static List<object?> ReadCodecArray<TCursor>(ref TCursor cursor, PrimitiveCodec codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        for (int index = 0; index < count; index++)
        {
            elements.Add(ReadCodecValue(ref cursor, codec, scratch));
        }

        return elements;
    }

    /// <summary>
    ///     Reads a <c>char[N]</c> as one Latin-1 string: as one block when the interpreter would (a member its composite
    ///     placed, with characters, not captured, not restricted to the general path, and the whole extent present
    ///     within the byte budget), otherwise character by character; then trimmed as the options say.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The program the member belongs to.</param>
    /// <param name="field">The member's index.</param>
    /// <param name="codec">The element codec's index.</param>
    /// <param name="count">The character count.</param>
    /// <param name="scratch">A scratch buffer.</param>
    /// <returns>The text.</returns>
    private static string ReadCharArray<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, int field, int codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        if (program.Kind != ReadProgramKind.Root && count > 0 && !program.Fields[field].CapturesLayoutVariable && !state.GeneralPathOnly)
        {
            byte[]? rented = null;
            try
            {
                if (TryStage(ref cursor, count, out ReadOnlySpan<byte> bytes, out rented))
                {
                    return state.FixedText(CStruct.ReadLatin1Characters(bytes));
                }
            }
            finally
            {
                if (rented is not null)
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

        return state.FixedText(ReadCharacters(ref cursor, program.Codecs[codec].Primitive, count, scratch));
    }

    /// <summary>Reads a <c>wchar[N]</c> character by character as one string, trimmed as the options say, which must be valid UTF-16.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="member">The array member.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="count">The character count.</param>
    /// <param name="scratch">A scratch buffer.</param>
    /// <returns>The text.</returns>
    /// <exception cref="CStructReadException">The characters are not valid UTF-16.</exception>
    private static string ReadWideCharArray<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField member, PrimitiveCodec codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        string text = state.FixedText(ReadCharacters(ref cursor, codec, count, scratch));
        PrimitiveCodecs.ValidateWideText(text, state.Layout.GetWideCharacterEncoding(member));
        return text;
    }

    /// <summary>Reads <paramref name="count"/> characters one at a time through their codec reader into a string.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The character codec.</param>
    /// <param name="count">The character count.</param>
    /// <param name="scratch">A scratch buffer.</param>
    /// <returns>The characters as read, untrimmed.</returns>
    private static string ReadCharacters<TCursor>(ref TCursor cursor, PrimitiveCodec codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        char[] characters = new char[count];
        for (int index = 0; index < count; index++)
        {
            characters[index] = (char)ReadCodecValue(ref cursor, codec, scratch);
        }

        return new string(characters);
    }

    /// <summary>Reads an array of enum or flag values through their storage codec into a list.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The storage codec.</param>
    /// <param name="enumType">The enum type.</param>
    /// <param name="count">The element count.</param>
    /// <param name="scratch">A scratch buffer.</param>
    /// <returns>The enum results.</returns>
    private static List<object?> ReadEnumArray<TCursor>(ref TCursor cursor, PrimitiveCodec codec, CompiledEnumType enumType, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        for (int index = 0; index < count; index++)
        {
            elements.Add(CStruct.CreateEnumValue(enumType, ReadCodecValue(ref cursor, codec, scratch)));
        }

        return elements;
    }

    /// <summary>
    ///     Reads and discards the elements of an unnamed padding array one at a time, as the interpreter reads a field
    ///     without a name: each element is read (and can fail) exactly as a named one would be.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="count">The element count.</param>
    /// <param name="scratch">A scratch buffer.</param>
    private static void SkipElements<TCursor>(ref TCursor cursor, PrimitiveCodec codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        for (int index = 0; index < count; index++)
        {
            if (codec.IsFixedWidthNumeric)
            {
                _ = cursor.ReadFixed(scratch[..codec.Size]);
            }
            else
            {
                _ = ReadCodecValue(ref cursor, codec, scratch);
            }
        }
    }

    /// <summary>
    ///     Reads an array of structs into a list. A fully fixed element struct of a member its composite placed is read
    ///     through its static plan over one in-memory span when the interpreter would (cancellation observed before each
    ///     element); otherwise each element is read as a struct of its own, with a fresh conditional selection.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The program the member belongs to.</param>
    /// <param name="field">The member's index.</param>
    /// <param name="element">The element struct's program.</param>
    /// <param name="count">The element count.</param>
    /// <returns>The elements.</returns>
    private static List<object?> ReadStructArray<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, int field, ReadProgram element, int count)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        CompiledCompositeType composite = element.Composite!;
        if (program.Kind != ReadProgramKind.Root && count > 0 && !state.GeneralPathOnly && composite.StaticPlan is { Size: > 0, } plan &&
            program.Fields[field].FixedElementSize == plan.Size && state.CoversPlan(plan) && (long)count * plan.Size <= int.MaxValue &&
            cursor.TryReadSpanWithinBudget(count * plan.Size, out ReadOnlySpan<byte> bytes))
        {
            for (int index = 0; index < count; index++)
            {
                cursor.ThrowIfCancellationRequested();
                var value = new StructValue(composite.Shape);
                RunStaticPlan(ref cursor, ref state, plan, bytes.Slice(index * plan.Size, plan.Size), value);
                elements.Add(value);
            }

            return elements;
        }

        for (int index = 0; index < count; index++)
        {
            var value = new StructValue(element.Shape);
            ReadComposite(ref cursor, ref state, element, value);
            elements.Add(value);
        }

        return elements;
    }

    /// <summary>
    ///     Runs a fixed composite's static read plan over its bytes with the interpreter's side effects: one nesting level
    ///     per struct (cancellation observed on entry), values stored straight into their slots, and the captures and
    ///     qualified publications a later expression may read.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type, which carries the cancellation token.</typeparam>
    /// <param name="cursor">The operation's cursor; the plan's bytes are already consumed.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="plan">The composite's plan.</param>
    /// <param name="bytes">Exactly the composite's bytes.</param>
    /// <param name="destination">The composite's new value.</param>
    private static void RunStaticPlan<TCursor>(ref TCursor cursor, ref ReadEngineState state, StaticReadPlan plan, ReadOnlySpan<byte> bytes, StructValue destination)
        where TCursor : struct, IReadCursor
    {
        state.EnterStructure(ref cursor);
        try
        {
            foreach (StaticReadOperation operation in plan.Operations)
            {
                CompiledField field = operation.Field;
                switch (operation.Kind)
                {
                case StaticReadKind.Numeric:
                    {
                        object value = field.Codec.ReadNumeric(bytes.Slice(operation.Offset, field.Codec.Size));
                        destination.SetFreshSlot(operation.Slot, value);
                        CaptureStatic(ref state, field, value);
                        break;
                    }

                case StaticReadKind.Enum:
                    {
                        EnumValueResult value = CStruct.CreateEnumValue(field.Enum!, field.Codec.ReadNumeric(bytes.Slice(operation.Offset, field.Codec.Size)));
                        destination.SetFreshSlot(operation.Slot, value);
                        CaptureStatic(ref state, field, value);
                        break;
                    }

                case StaticReadKind.CharArray:
                    {
                        CheckPlanCount(operation.Count, state.MaxArrayElements);
                        string text = state.FixedText(CStruct.ReadLatin1Characters(bytes.Slice(operation.Offset, operation.Count)));
                        destination.SetFreshSlot(operation.Slot, text);
                        CaptureStatic(ref state, field, text);
                        break;
                    }

                case StaticReadKind.NumericArray:
                    {
                        CheckPlanCount(operation.Count, state.MaxArrayElements);
                        if (operation.Count == 0)
                        {
                            destination.SetFreshSlot(operation.Slot, PrimitiveArrayReader.Empty(field.Codec));
                            break;
                        }

                        IList<object?> values = PrimitiveArrayReader.Decode(bytes.Slice(operation.Offset, operation.Count * field.Codec.Size), field.Codec, operation.Count);
                        destination.SetFreshSlot(operation.Slot, values);
                        CaptureStatic(ref state, field, values);
                        break;
                    }

                case StaticReadKind.Nested:
                    {
                        var nested = new StructValue(operation.NestedComposite!.Shape);
                        destination.SetFreshSlot(operation.Slot, nested);
                        string? outer = state.QualifiedPrefix;
                        if (field.HasQualifiedPrefix)
                        {
                            state.QualifiedPrefix = outer is null ? field.QualifiedPrefix : outer + field.QualifiedPrefix;
                        }

                        RunStaticPlan(ref cursor, ref state, operation.NestedPlan!, bytes.Slice(operation.Offset, operation.NestedPlan!.Size), nested);
                        state.QualifiedPrefix = outer;
                        break;
                    }

                case StaticReadKind.NestedArray:
                    {
                        CheckPlanCount(operation.Count, state.MaxArrayElements);
                        var elements = new List<object?>(operation.Count);
                        destination.SetFreshSlot(operation.Slot, elements);
                        StaticReadPlan nestedPlan = operation.NestedPlan!;
                        int offset = operation.Offset;
                        for (int element = 0; element < operation.Count; element++, offset += nestedPlan.Size)
                        {
                            var nested = new StructValue(operation.NestedComposite!.Shape);
                            elements.Add(nested);
                            RunStaticPlan(ref cursor, ref state, nestedPlan, bytes.Slice(offset, nestedPlan.Size), nested);
                        }

                        break;
                    }
                }
            }
        }
        finally
        {
            state.StructureDepth--;
        }
    }

    /// <summary>Rejects a static-plan array past the element limit, as the plan executor does (the plan is only taken when it cannot).</summary>
    /// <param name="count">The array's element count.</param>
    /// <param name="maximum">The operation's element limit.</param>
    /// <exception cref="CStructReadLimitException">The count exceeds <paramref name="maximum"/>.</exception>
    private static void CheckPlanCount(int count, int maximum)
    {
        if (count > maximum)
        {
            throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(count, maximum));
        }
    }

    /// <summary>
    ///     Captures a value the static plan read when an expression of the layout names its field, by the shared capture
    ///     rule, and publishes it under the active qualified prefix.
    /// </summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The field read.</param>
    /// <param name="value">The value read.</param>
    private static void CaptureStatic(ref ReadEngineState state, CompiledField field, object? value)
    {
        if (!field.CapturesLayoutVariable)
        {
            return;
        }

        string name = field.Declaration.Name.Name;
        SlotValue captured = field.NotANumberReason is { } reason ? SlotValue.FromUnusable(new NotANumberVariable(reason)) : CaptureValue(value);
        SlotTable table = state.Slots.Table;
        if (table.TryGetSlot(name, out int slot))
        {
            state.Slots.Set(slot, captured);
        }

        if (state.QualifiedPrefix is not null)
        {
            state.PublishQualified(table.ReadPrograms.GetQualifiedTargets(name), captured);
        }
    }
}
