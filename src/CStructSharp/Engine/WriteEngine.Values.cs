namespace CStructSharp.Engine;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Values;
using CStructSharp.Writing;

/// <summary>The value encodings of the compiled engine's writer: numbers, codec values, enums, text, arrays, pointers and bitfields.</summary>
internal static partial class WriteEngine
{
    /// <summary>
    ///     Encodes one fixed-width number and writes it: the value is converted first (a conversion failure names the value
    ///     and the field's type), then the budget and room are checked.
    ///     The conversion is the codec's own (<see cref="PrimitiveCodec.WriteNumeric"/>), the one the static write plan
    ///     also uses.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="codec">The number's codec.</param>
    /// <param name="field">The field the value is written as, named in a conversion failure.</param>
    /// <param name="value">The supplied value.</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    /// <exception cref="CStructWriteException">The value cannot be converted, or does not fit.</exception>
    private static void WriteNumber<TDestination>(ref TDestination destination, PrimitiveCodec codec, CompiledField field, object value, Span<byte> scratch)
        where TDestination : struct, IWriteDestination
    {
        Span<byte> bytes = scratch[..codec.Size];
        try
        {
            codec.WriteNumeric(bytes, value);
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or FormatException or InvalidCastException)
        {
            throw new CStructWriteException(CStruct.DescribeUnwritableValue(value, field), exception);
        }

        destination.Write(bytes);
    }

    /// <summary>
    ///     Writes one value through its codec's stream writer: conversion failures of the
    ///     codec become a write failure naming the value and the field's type; the codec's own write failures pass unchanged.
    ///     In an update's sparse staging a LEB128 or variable-size custom value must keep its existing encoded length.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="codec">The codec: its catalog id selects the writer, and terminated text is encoded here instead.</param>
    /// <param name="field">The field the value is written as.</param>
    /// <param name="value">The supplied value.</param>
    /// <exception cref="CStructWriteException">The value cannot be encoded.</exception>
    private static void WriteThroughCodec<TDestination>(ref TDestination destination, ref WriteEngineState state, ProgramCodec codec, CompiledField field, object value)
        where TDestination : struct, IWriteDestination
    {
        if (codec.Primitive.IsTerminatedText)
        {
            WriteTerminatedText(ref destination, codec.Primitive, field, value);
            return;
        }

        if (destination.Stream is WriteBudgetStream { IsSparseUpdate: true, } && (field.Codec.IsLeb128 || (field.Codec.IsCustom && !field.FixedElementSize.HasValue)))
        {
            // An update's staging keeps a variable-length value at its existing encoded length, which the layout's
            // primitive write checks (outside a union's staging, which is not the update's sparse stream).
            state.Layout.WritePrimitiveValue(field, destination.Stream, value);
            return;
        }

        Action<Stream, object> writer = state.Layout.Codecs.WriterOfCodec(codec.CodecId) ??
                                        throw new InvalidOperationException("Compiled field has no writer: " + field.CodecName);
        try
        {
            writer(destination.Stream, value);
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or FormatException or InvalidCastException)
        {
            throw new CStructWriteException(CStruct.DescribeUnwritableValue(value, field), exception);
        }
    }

    /// <summary>
    ///     Writes one value as terminated text exactly as the catalog's terminated-string writer does
    ///     (<c>PrimitiveCodecs.WriteTerminatedString</c>), without its pooled payload and stream calls: the value is converted
    ///     to invariant text (a failure there names the value), a terminator inside it is rejected, the encoded size with the
    ///     terminator is checked against the per-string limit, and text the strict encoding cannot represent fails.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="codec">The terminated-text codec: its kind and byte order select the encoding, and its terminator.</param>
    /// <param name="field">The field the value is written as, named in a conversion failure.</param>
    /// <param name="value">The supplied value.</param>
    /// <exception cref="CStructWriteException">The value cannot be encoded.</exception>
    private static void WriteTerminatedText<TDestination>(ref TDestination destination, PrimitiveCodec codec, CompiledField field, object value)
        where TDestination : struct, IWriteDestination
    {
        string text;
        try
        {
            text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or FormatException or InvalidCastException)
        {
            throw new CStructWriteException(CStruct.DescribeUnwritableValue(value, field), exception);
        }

        char terminator = codec.Terminator;
        if (text.Contains(terminator, StringComparison.Ordinal))
        {
            throw new CStructWriteException(WriteFailures.TerminatorInValue);
        }

        Encoding encoding = codec.Kind switch
        {
            PrimitiveCodecKind.TerminatedAscii => PrimitiveCodecs.StrictAsciiEncoding,
            PrimitiveCodecKind.TerminatedUtf8 => PrimitiveCodecs.StrictUtf8Encoding,
            _ => codec.LittleEndian ? PrimitiveCodecs.StrictUtf16LittleEndianEncoding : PrimitiveCodecs.StrictUtf16BigEndianEncoding,
        };
        byte[]? rented = null;
        try
        {
            int valueBytes = encoding.GetByteCount(text);
            int terminatorBytes = encoding.GetByteCount(new ReadOnlySpan<char>(in terminator));
            destination.EnsureStringBytes(checked((long)valueBytes + terminatorBytes));
            int length = valueBytes + terminatorBytes;
            Span<byte> payload = length <= StackStagingLimit ? stackalloc byte[StackStagingLimit] : (rented = ArrayPool<byte>.Shared.Rent(length));
            int written = encoding.GetBytes(text, payload[..valueBytes]);
            written += encoding.GetBytes(new ReadOnlySpan<char>(in terminator), payload.Slice(written, terminatorBytes));
            destination.Write(payload[..written]);
        }
        catch (EncoderFallbackException exception)
        {
            throw new CStructWriteException(WriteFailures.InvalidForEncoding, exception);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    ///     Writes one enum value: the value is resolved to a member's exact number
    ///     (<see cref="EnumFieldValueParser"/>, which reports its own failures), and its storage value is written without the
    ///     primitive writer's failure translation. A fixed-width storage encodes through the codec's own conversion
    ///     (<see cref="PrimitiveCodec.WriteNumeric"/>), which the static write plan also uses.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="codec">The enum's storage codec.</param>
    /// <param name="enm">The enum type.</param>
    /// <param name="value">The supplied value.</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    /// <returns>The exact number written, which a capture stores.</returns>
    private static BigInteger WriteEnum<TDestination>(ref TDestination destination, ref WriteEngineState state, ProgramCodec codec, CompiledEnumType enm, object value, Span<byte> scratch)
        where TDestination : struct, IWriteDestination
    {
        BigInteger number = EnumFieldValueParser.GetEnumValue(enm, value);
        object storage = enm.Integer.ToStorageValue(number);
        if (codec.Primitive.IsFixedWidthNumeric)
        {
            Span<byte> bytes = scratch[..codec.Primitive.Size];
            codec.Primitive.WriteNumeric(bytes, storage);
            destination.Write(bytes);
        }
        else
        {
            Action<Stream, object> writer = state.Layout.Codecs.WriterOfCodec(codec.CodecId) ??
                                            throw new InvalidOperationException("Compiled enum has no storage writer: " + enm.Name);
            writer(destination.Stream, storage);
        }

        return number;
    }

    /// <summary>
    ///     Writes fixed-capacity text of <paramref name="count"/> characters (or bytes, for byte-counted text), padded with
    ///     zeroes, checking in this order: the per-string limit, the text's length against the capacity, then the
    ///     encoding. A narrow <c>char[N]</c> is written as one block when every character fits a byte and the budget and
    ///     room hold the block; otherwise character by character, so a failure leaves the earlier characters written.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The text member.</param>
    /// <param name="codecId">The character codec's id, whose writer the character-by-character path uses.</param>
    /// <param name="text">The text, already converted from characters or bytes when the caller supplied those.</param>
    /// <param name="count">The capacity in characters (bytes for byte-counted text).</param>
    private static void WriteText<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledField field, int codecId, string text, int count)
        where TDestination : struct, IWriteDestination
    {
        if (BoundedTextCodec.IsType(field.TypeSpelling))
        {
            destination.EnsureStringBytes(count);
            if (BoundedTextCodec.IsUtf16(field.TypeSpelling) && (count & 1) != 0)
            {
                throw new CStructWriteException(WriteFailures.Utf16CapacityOdd);
            }

            byte[] encoded;
            try
            {
                int length = BoundedTextCodec.GetByteCount(field.TypeSpelling, text);
                if (length > count)
                {
                    throw new CStructWriteException(WriteFailures.BoundedTextTooLong(field.Name, length, count));
                }

                encoded = BoundedTextCodec.Encode(field.TypeSpelling, text);
            }
            catch (EncoderFallbackException exception)
            {
                throw new CStructWriteException(WriteFailures.EncodingUnrepresentable, exception);
            }

            destination.Write(encoded);
            destination.WriteZeroes(count - encoded.Length);
            return;
        }

        // A fixed buffer takes its declared size; longer input is rejected rather than truncated.
        if (text.Length > count)
        {
            throw new CStructWriteException(WriteFailures.FixedTextTooLong(field.Name, text.Length, count));
        }

        destination.EnsureStringBytes(checked((long)count * (field.IsWideCharElement ? 2 : 1)));
        if (field.IsWideCharElement)
        {
            byte[] encoded;
            try
            {
                encoded = state.Layout.GetWideCharacterEncoding(field).GetBytes(text.PadRight(count, '\0'));
            }
            catch (EncoderFallbackException exception)
            {
                throw new CStructWriteException(WriteFailures.InvalidWideText, exception);
            }

            destination.Write(encoded);
            return;
        }

        if (TryWriteNarrowTextBlock(ref destination, ref state, text, count))
        {
            return;
        }

        // The character-by-character path: each NUL-padded character through the character codec's writer.
        Action<Stream, object> writer = state.Layout.Codecs.WriterOfCodec(codecId) ??
                                        throw new InvalidOperationException("Compiled field has no writer: " + field.CodecName);
        for (int index = 0; index < count; index++)
        {
            object character = index < text.Length ? text[index] : '\0';
            try
            {
                writer(destination.Stream, character);
            }
            catch (Exception exception) when (exception is ArgumentException or ArithmeticException or FormatException or InvalidCastException)
            {
                throw new CStructWriteException(CStruct.DescribeUnwritableValue(character, field), exception);
            }
        }
    }

    /// <summary>
    ///     Writes a narrow <c>char[N]</c> as one block when the character-by-character writes could not fail part-way: the
    ///     block path is allowed, every character fits one byte, and the budget and room hold the whole block (the text
    ///     padded to <paramref name="count"/> with NUL). Returns <see langword="false"/>, having written nothing, otherwise.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="text">The text, at most <paramref name="count"/> characters.</param>
    /// <param name="count">The capacity in characters.</param>
    /// <returns>Whether the text was written.</returns>
    private static bool TryWriteNarrowTextBlock<TDestination>(ref TDestination destination, ref WriteEngineState state, string text, int count)
        where TDestination : struct, IWriteDestination
    {
        if (!CanWriteBlock(ref destination, ref state, count))
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
        Span<byte> block = count <= StackStagingLimit ? stackalloc byte[count] : (rented = ArrayPool<byte>.Shared.Rent(count)).AsSpan(0, count);
        try
        {
            for (int index = 0; index < text.Length; index++)
            {
                block[index] = (byte)text[index];
            }

            block[text.Length..].Clear();
            destination.WriteBlock(block, block.Length);
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
    ///     Whether <paramref name="length"/> bytes can be written as one block with the outcome of writing them one by one:
    ///     the block path is allowed (not <see cref="ExecutionPath.NoFastPaths"/>, no update semantics, and neither a stream
    ///     nor a union's staging, which are written element by element), and the budget and the
    ///     destination's room hold them all.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="length">The block's length in bytes.</param>
    /// <returns>Whether the block path applies.</returns>
    private static bool CanWriteBlock<TDestination>(ref TDestination destination, ref WriteEngineState state, int length)
        where TDestination : struct, IWriteDestination
        => !state.NoFastPaths && !state.UpdateSemantics && destination.AllowsBlocks && destination.CanAffordBlock(length, length);

    /// <summary>
    ///     Writes fixed-width numbers: as one block from typed storage (a parsed <see cref="PrimitiveArray{T}"/> or an exact
    ///     <c>T[]</c> of the declared length) when the block path applies - a named member a struct places, with a
    ///     declared count, within one 64 KiB block, the budget and room - and otherwise element by element.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array member.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="value">The supplied collection.</param>
    /// <param name="count">The declared element count, or -1 when the value decides it.</param>
    /// <param name="placed">Whether a struct places the member (the typed block path is taken only then).</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    private static void WriteNumericArray<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledField field, PrimitiveCodec codec, object value, int count, bool placed, Span<byte> scratch)
        where TDestination : struct, IWriteDestination
    {
        if (placed && count > 0 && !field.IsUnnamed && TryWriteTypedArrayBlock(ref destination, ref state, field, value, count))
        {
            return;
        }

        IList<object> items = Elements(field, value, count, state.MaxArrayElements);
        int written = count < 0 ? items.Count : count;
        for (int index = 0; index < written; index++)
        {
            WriteNumber(ref destination, codec, field, items[index], scratch);
        }
    }

    /// <summary>
    ///     Writes a numeric array held in the field's own CLR type as one block, with the vectorized byte-order conversion;
    ///     anything else returns <see langword="false"/> having written nothing, and the element loop runs.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array member.</param>
    /// <param name="value">The supplied collection.</param>
    /// <param name="count">The declared element count, positive.</param>
    /// <returns>Whether the array was written.</returns>
    private static bool TryWriteTypedArrayBlock<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledField field, object value, int count)
        where TDestination : struct, IWriteDestination
    {
        long length = (long)count * field.Codec.Size;
        if (length > BlockSize.Bytes || !CanWriteBlock(ref destination, ref state, (int)length))
        {
            return false;
        }

        byte[]? rented = null;
        Span<byte> block = length <= StackStagingLimit ? stackalloc byte[(int)length] : (rented = ArrayPool<byte>.Shared.Rent((int)length)).AsSpan(0, (int)length);
        try
        {
            if (!CStruct.TryWriteTypedArray(field, block, value, count))
            {
                return false;
            }

            destination.WriteBlock(block, block.Length);
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
    ///     Writes an array element by element, each by its kind, observing the token before each element whose type is a
    ///     struct or union (a struct array, or a pointer array to structs).
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The program the member belongs to.</param>
    /// <param name="member">The member's index.</param>
    /// <param name="kind">How each element is written.</param>
    /// <param name="operand">The codec index, nested program index, or zero-fill size.</param>
    /// <param name="value">The supplied collection.</param>
    /// <param name="count">The declared element count, or -1 when the value decides it.</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    private static void WriteElements<TDestination>(ref TDestination destination, ref WriteEngineState state, WriteProgram program, int member, WriteElementKind kind, int operand, object value, int count, Span<byte> scratch)
        where TDestination : struct, IWriteDestination
    {
        CompiledField field = program.Fields[member];
        IList<object> items = Elements(field, value, count, state.MaxArrayElements);
        int written = count < 0 ? items.Count : count;
        bool cancellable = field.TargetComposite is not null;
        for (int index = 0; index < written; index++)
        {
            if (cancellable)
            {
                state.CancellationToken.ThrowIfCancellationRequested();
            }

            WriteElement(ref destination, ref state, program, field, kind, operand, items[index], scratch);
        }
    }

    /// <summary>
    ///     Writes a multidimensional array's leaves: the nested collections are flattened row-major (each level checked
    ///     against its dimension), then every leaf is written by its kind, with no cancellation check between them.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The program the member belongs to.</param>
    /// <param name="member">The member's index.</param>
    /// <param name="kind">How each leaf is written.</param>
    /// <param name="operand">The codec index, nested program index, or zero-fill size.</param>
    /// <param name="value">The supplied nested collection.</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    private static void WriteLeaves<TDestination>(ref TDestination destination, ref WriteEngineState state, WriteProgram program, int member, WriteElementKind kind, int operand, object value, Span<byte> scratch)
        where TDestination : struct, IWriteDestination
    {
        CompiledField field = program.Fields[member];
        List<object> leaves = CStruct.FlattenNestedArrayValues(value, CStruct.FixedDimensionSizes(field), field.Name);
        for (int index = 0; index < leaves.Count; index++)
        {
            WriteElement(ref destination, ref state, program, field, kind, operand, leaves[index], scratch);
        }
    }

    /// <summary>
    ///     Writes a multidimensional character array row by row: the outer dimensions are flattened, and each row - a
    ///     string, or characters - is fixed-capacity text of the innermost dimension.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array member.</param>
    /// <param name="codecId">The character codec's id.</param>
    /// <param name="value">The supplied nested collection of rows.</param>
    private static void WriteTextTable<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledField field, int codecId, object value)
        where TDestination : struct, IWriteDestination
    {
        int[] dimensions = CStruct.FixedDimensionSizes(field);
        int rowSize = dimensions[^1];
        foreach (object row in CStruct.FlattenNestedArrayValues(value, dimensions[..^1], field.Name))
        {
            string text = row as string ?? WriteValueMaterialization.ConvertToBoundedCharString(row, rowSize, field.Name);
            WriteText(ref destination, ref state, field, codecId, text, rowSize);
        }
    }

    /// <summary>Writes one element of an array by its kind.</summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The program the member belongs to.</param>
    /// <param name="field">The array member.</param>
    /// <param name="kind">How the element is written.</param>
    /// <param name="operand">The codec index, nested program index, or zero-fill size.</param>
    /// <param name="item">The element's supplied value.</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    private static void WriteElement<TDestination>(ref TDestination destination, ref WriteEngineState state, WriteProgram program, CompiledField field, WriteElementKind kind, int operand, object item, Span<byte> scratch)
        where TDestination : struct, IWriteDestination
    {
        switch (kind)
        {
        case WriteElementKind.Zeroes:
            destination.WriteZeroes(operand);
            break;
        case WriteElementKind.Pointer:
            WritePointer(ref destination, ref state, item, scratch);
            break;
        case WriteElementKind.Enum:
            _ = WriteEnum(ref destination, ref state, program.Codecs[operand], field.Enum!, item, scratch);
            break;
        case WriteElementKind.Composite:
            WriteComposite(ref destination, ref state, program.Nested[operand], item, promoted: false);
            break;
        case WriteElementKind.Numeric:
            WriteNumber(ref destination, program.Codecs[operand].Primitive, field, item, scratch);
            break;
        default:
            WriteThroughCodec(ref destination, ref state, program.Codecs[operand], field, item);
            break;
        }
    }

    /// <summary>
    ///     Writes a pointer's stored address: the supplied value converted to a position (null is
    ///     the null address), encoded by the operation's addressing mode and origin at the layout's pointer width and byte
    ///     order; failures are the address rules' own.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="value">A <see cref="Pointer"/>, an integer position, or <see langword="null"/>.</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    private static void WritePointer<TDestination>(ref TDestination destination, ref WriteEngineState state, object? value, Span<byte> scratch)
        where TDestination : struct, IWriteDestination
    {
        long address = CStructPointerArithmetic.ConvertTargetAddress(value);
        ulong stored = CStructPointerArithmetic.EncodeTargetAddress(address, state.Options.AddressingMode, state.Options.Origin, state.Layout.PointerSize);
        Span<byte> unit = BinaryPrimitiveIO.UnitOf(scratch, state.Layout.PointerSize);
        Codec.WriteUnsigned(unit, stored, state.Layout.IsLittleEndian);
        destination.Write(unit);
    }

    /// <summary>
    ///     Replaces one bitfield inside its storage unit without changing the neighbouring bits: an
    ///     enum is resolved to its raw bits, the slice is validated, the unit's existing bytes are read back (zero past
    ///     the destination's end: a new output's high-water mark, or a caller's stream's own end), the bits merged, and the
    ///     whole unit written - and charged - again. While later bitfields share the unit the position returns to its start.
    ///     Under update semantics a unit the destination does not wholly hold fails before anything is written.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, at the unit's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The bitfield.</param>
    /// <param name="value">The supplied value.</param>
    /// <param name="bitOffset">The bits of the unit already used; advanced past the field (and past the unit when it is full).</param>
    /// <param name="unitSize">The placed unit's size in bytes, or 0 for the field's declared storage size.</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    /// <exception cref="CStructWriteException">The value does not fit the field, or the field does not fit the unit.</exception>
    /// <exception cref="CStructReadException">Under update semantics, the destination does not hold the whole unit.</exception>
    private static void WriteBitfield<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledField field, object value, ref int bitOffset, int unitSize, Span<byte> scratch)
        where TDestination : struct, IWriteDestination
    {
        if (field.Enum is { } enm)
        {
            value = enm.Integer.ToRawBits(EnumFieldValueParser.GetEnumValue(enm, value));
        }

        int byteSize = unitSize > 0 ? unitSize : field.BitStorageSize!.Value;
        bool littleEndian = field.BitStorageIsLittleEndian!.Value;
        int unitBits = checked(byteSize * 8);
        if (bitOffset + field.BitSize > unitBits)
        {
            throw new CStructWriteException(LayoutFailures.BitfieldExceedsUnit(field.Name));
        }

        ulong bits = BitfieldCodecTable.ValidateBitfieldWriteValue(field.Name, field.BitSize, value);

        // Read the unit back so the bits of the other fields survive; a destination that does not hold all of it yet
        // reads the rest as zero, except under update semantics, which only change bytes that exist.
        long unitStart = destination.Position;
        Span<byte> unit = BinaryPrimitiveIO.UnitOf(scratch, byteSize);
        int offset = 0;
        while (offset < unit.Length)
        {
            int read = destination.Read(unit[offset..]);
            if (read == 0)
            {
                if (state.UpdateSemantics)
                {
                    throw new CStructReadException(WriteFailures.IncompleteBitfieldUnit);
                }

                unit[offset..].Clear();
                break;
            }

            offset += read;
        }

        ulong merged = BitfieldCodecTable.MergeBitfieldValue(
            BinaryPrimitiveIO.ReadUnsigned(unit, littleEndian),
            bits,
            BitfieldCodecTable.EffectiveShift(bitOffset, field.BitSize, unitBits, state.Layout.Compilation.HighBitFirst),
            field.BitSize);
        destination.Position = unitStart;
        Codec.WriteUnsigned(unit, merged, littleEndian);
        destination.Write(unit);

        bitOffset += field.BitSize;
        if (1 + (bitOffset / 8) > byteSize)
        {
            // This field finished the unit; the next bitfield starts a fresh one.
            bitOffset -= unitBits;
        }
        else
        {
            destination.Position = unitStart;
        }
    }

    /// <summary>
    ///     Materializes an array value before its element loop: a declared count consumes at most
    ///     one element past it and rejects a different number of elements; a count the value decides (-1) takes every
    ///     element up to the element limit.
    /// </summary>
    /// <param name="field">The array member.</param>
    /// <param name="value">The supplied collection.</param>
    /// <param name="count">The declared element count, or -1.</param>
    /// <param name="maximum">The operation's element limit.</param>
    /// <returns>The elements.</returns>
    /// <exception cref="CStructWriteException">The value is not a collection or has a different or larger number of elements.</exception>
    /// <exception cref="CStructWriteLimitException">The value holds more elements than the limit.</exception>
    private static IList<object> Elements(CompiledField field, object value, int count, int maximum)
    {
        if (count < 0)
        {
            IList<object> all = WriteValueMaterialization.ConvertToObjectList(value, maximum, field.Name);
            if (all.Count > maximum)
            {
                throw new CStructWriteLimitException(WriteFailures.ArrayLengthLimit(field.Name));
            }

            return all;
        }

        IList<object> items = WriteValueMaterialization.ConvertToObjectList(value, count, field.Name);
        if (items.Count != count)
        {
            throw new CStructWriteException(WriteFailures.ArrayLengthMismatch(field.Name, count, items.Count));
        }

        return items;
    }
}
