namespace CStructSharp.Engine;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;
using CStructSharp.Writing;

/// <summary>The value encodings of the compiled engine's writer: numbers, codec values, enums, text and arrays.</summary>
internal static partial class WriteEngine
{
    /// <summary>
    ///     Encodes one fixed-width number and writes it: the value is converted first (a conversion failure names the value
    ///     and the field's type, as the interpreter's primitive writer reports it), then the budget and room are checked.
    ///     The conversion is the codec's own (<see cref="PrimitiveCodec.WriteNumeric"/>), the one the stream writers and the
    ///     static write plan share.
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
    ///     Writes one value through its codec's stream writer, the interpreter's primitive write: conversion failures of the
    ///     codec become a write failure naming the value and the field's type; the codec's own write failures pass unchanged.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="codecId">The catalog codec id whose writer encodes the value.</param>
    /// <param name="field">The field the value is written as.</param>
    /// <param name="value">The supplied value.</param>
    /// <exception cref="CStructWriteException">The value cannot be encoded.</exception>
    private static void WriteThroughCodec<TDestination>(ref TDestination destination, ref WriteEngineState state, int codecId, CompiledField field, object value)
        where TDestination : struct, IWriteDestination
    {
        Action<Stream, object> writer = state.Layout.Codecs.WriterOfCodec(codecId) ??
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
    ///     Writes one enum value as the interpreter does: the value is resolved to a member's exact number
    ///     (<see cref="EnumFieldValueParser"/>, which reports its own failures), and its storage value is written without the
    ///     primitive writer's failure translation. A fixed-width storage encodes through the codec's own conversion, which
    ///     the stream writer and the static write plan share.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="codec">The enum's storage codec.</param>
    /// <param name="enm">The enum type.</param>
    /// <param name="value">The supplied value.</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    /// <returns>The exact number written, which a capture stores.</returns>
    private static BigInteger WriteEnum<TDestination>(ref TDestination destination, ref WriteEngineState state, ReadProgram.Codec codec, CompiledEnumType enm, object value, Span<byte> scratch)
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
    ///     zeroes, in the interpreter's check order: the per-string limit, the text's length against the capacity, then the
    ///     encoding. A narrow <c>char[N]</c> is written as one block when every character fits a byte and the budget and
    ///     room hold the block; otherwise character by character, so a failure leaves the earlier characters written.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The text member.</param>
    /// <param name="codecId">The character codec's id, whose writer the character-by-character path uses.</param>
    /// <param name="value">A string, or a sequence of characters or bytes.</param>
    /// <param name="count">The capacity in characters (bytes for byte-counted text).</param>
    private static void WriteText<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledField field, int codecId, object value, int count)
        where TDestination : struct, IWriteDestination
    {
        string text = value as string ?? WriteValueMaterialization.ConvertToBoundedCharString(value, count, field.Name);
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
    ///     the block path is allowed (not <see cref="ExecutionPath.GeneralOnly"/>), and the budget and the destination's
    ///     room hold them all.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="length">The block's length in bytes.</param>
    /// <returns>Whether the block path applies.</returns>
    private static bool CanWriteBlock<TDestination>(ref TDestination destination, ref WriteEngineState state, int length)
        where TDestination : struct, IWriteDestination
        => !state.GeneralPathOnly && destination.CanAffordBlock(length, length);

    /// <summary>
    ///     Writes <paramref name="count"/> fixed-width numbers: as one block from typed storage (a parsed
    ///     <see cref="PrimitiveArray{T}"/> or an exact <c>T[]</c> of that length) when the interpreter takes that path - a
    ///     named member a struct places, within one 64 KiB block, the budget and room - and otherwise element by element.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array member.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="value">The supplied collection.</param>
    /// <param name="count">The declared element count.</param>
    /// <param name="placed">Whether a struct places the member (the typed block path is taken only then).</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    private static void WriteNumericArray<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledField field, PrimitiveCodec codec, object value, int count, bool placed, Span<byte> scratch)
        where TDestination : struct, IWriteDestination
    {
        if (placed && count > 0 && !field.IsUnnamed && TryWriteTypedArrayBlock(ref destination, ref state, field, value, count))
        {
            return;
        }

        IList<object> items = Elements(field, value, count);
        for (int index = 0; index < count; index++)
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
        if (length > ReadBlock.Size || !CanWriteBlock(ref destination, ref state, (int)length))
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

    /// <summary>Writes <paramref name="count"/> elements one by one through the element codec's stream writer.</summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array member.</param>
    /// <param name="codecId">The element codec's id.</param>
    /// <param name="value">The supplied collection.</param>
    /// <param name="count">The declared element count.</param>
    private static void WriteCodecArray<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledField field, int codecId, object value, int count)
        where TDestination : struct, IWriteDestination
    {
        IList<object> items = Elements(field, value, count);
        for (int index = 0; index < count; index++)
        {
            WriteThroughCodec(ref destination, ref state, codecId, field, items[index]);
        }
    }

    /// <summary>Writes <paramref name="count"/> elements one by one as members of an enum.</summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array member.</param>
    /// <param name="codec">The storage codec.</param>
    /// <param name="enm">The enum type.</param>
    /// <param name="value">The supplied collection.</param>
    /// <param name="count">The declared element count.</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    private static void WriteEnumArray<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledField field, ReadProgram.Codec codec, CompiledEnumType enm, object value, int count, Span<byte> scratch)
        where TDestination : struct, IWriteDestination
    {
        IList<object> items = Elements(field, value, count);
        for (int index = 0; index < count; index++)
        {
            _ = WriteEnum(ref destination, ref state, codec, enm, items[index], scratch);
        }
    }

    /// <summary>
    ///     Writes <paramref name="count"/> structs one by one, observing the token before each element as the interpreter
    ///     does (and again at each struct's entry); a null element fails as a null struct.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array member.</param>
    /// <param name="element">The element struct's program.</param>
    /// <param name="value">The supplied collection.</param>
    /// <param name="count">The declared element count.</param>
    private static void WriteStructArray<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledField field, WriteProgram element, object value, int count)
        where TDestination : struct, IWriteDestination
    {
        IList<object> items = Elements(field, value, count);
        for (int index = 0; index < count; index++)
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            WriteComposite(ref destination, ref state, element, items[index], promoted: false);
        }
    }

    /// <summary>
    ///     Materializes an array value as the interpreter does before its element loop, consuming at most one element past
    ///     the count, and rejects a different number of elements.
    /// </summary>
    /// <param name="field">The array member.</param>
    /// <param name="value">The supplied collection.</param>
    /// <param name="count">The declared element count.</param>
    /// <returns>The elements.</returns>
    /// <exception cref="CStructWriteException">The value is not a collection or has a different number of elements.</exception>
    private static IList<object> Elements(CompiledField field, object value, int count)
    {
        IList<object> items = WriteValueMaterialization.ConvertToObjectList(value, count, field.Name);
        if (items.Count != count)
        {
            throw new CStructWriteException(WriteFailures.ArrayLengthMismatch(field.Name, count, items.Count));
        }

        return items;
    }
}
