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

/// <summary>The value reads of the compiled engine: scalars through their codecs, caller codecs through their adapter, and every array shape.</summary>
internal static partial class ReadEngine
{
    /// <summary>
    ///     Reads one value of a codec through the cursor, element by element (used for every codec that is not a
    ///     fixed-width number, for enum storage and for array elements not read as a block): a one-byte value through
    ///     <see cref="IReadCursor.ReadByteExactly"/>, a wider fixed-size one through
    ///     <see cref="IReadCursor.ReadExactly"/>, LEB128 byte by byte, text up to its terminator, and a UUID through
    ///     the plain exact read whose short-read failure it words itself. The boxed CLR type is the codec's value type
    ///     (such as <see cref="char"/> for <c>char</c> and <see cref="Guid"/> for a UUID).
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The value's codec.</param>
    /// <param name="scratch">A buffer of at least 16 bytes for fixed-size values.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="CStructReadException">The input ends early or holds an invalid value.</exception>
    /// <exception cref="CStructReadLimitException">The bytes exceed a budget.</exception>
    internal static object ReadCodecValue<TCursor>(ref TCursor cursor, PrimitiveCodec codec, Span<byte> scratch)
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

            // A fixed-width number read element by element (enum storage): one byte through ReadByteExactly, a wider
            // value through an exact read, never straight from memory, so it fails with the element-read texts.
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

    /// <summary>
    ///     Reads a LEB128 integer one byte at a time through the shared <see cref="Leb128Decoder"/>, stopping at the
    ///     terminating byte; input that ends first fails with the one-byte short-read text, and an encoding that runs
    ///     past the declared width fails where the decoder rejects it.
    /// </summary>
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
    ///     Reads a numeric array of a member its composite placed: a non-empty one as one typed block (the bulk path,
    ///     failing at its block granularity), an empty one as the empty typed array of the same element type.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="count">The element count.</param>
    /// <returns>The typed array.</returns>
    private static IList<object?> ReadNumericArray<TCursor>(ref TCursor cursor, PrimitiveCodec codec, int count)
        where TCursor : struct, IReadCursor
        => count > 0 ? cursor.ReadPrimitiveArray(codec, count) : PrimitiveArrayReader.Empty(codec);

    /// <summary>
    ///     Reads a numeric array no composite placed (a root) one element at a time - each element straight from memory
    ///     when it is there, else through the codec's reads - then gives the elements the typed shape.
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
    ///     Reads a <c>char[N]</c> as one Latin-1 string: as one block when the block path applies (a member its
    ///     composite placed, with characters, not captured, not restricted to member-by-member reads, and the whole extent
    ///     present within the byte budget), otherwise character by character; then trimmed as the options say.
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
        if (program.PlacesMembers && count > 0 && !program.Fields[field].CapturesLayoutVariable && !state.NoFastPaths)
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

    /// <summary>
    ///     Reads the characters of a multidimensional <c>char</c> or <c>wchar</c> array one at a time, then makes each
    ///     innermost row a string - trimmed as the options say, and valid UTF-16 for <c>wchar</c>, checked row by row - and
    ///     nests the rows by the outer dimensions, once the whole array is read.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="member">The array member.</param>
    /// <param name="codec">The character codec.</param>
    /// <param name="count">The number of characters in every row together.</param>
    /// <param name="scratch">A scratch buffer.</param>
    /// <returns>The rows, nested in lists by the outer dimensions.</returns>
    /// <exception cref="CStructReadException">The input ends early, or a <c>wchar</c> row is not valid UTF-16.</exception>
    private static List<object?> ReadCharTable<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField member, PrimitiveCodec codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        char[] characters = new char[count];
        for (int index = 0; index < count; index++)
        {
            characters[index] = (char)ReadCodecValue(ref cursor, codec, scratch);
        }

        return CharacterRows(ref state, member, characters);
    }

    /// <summary>
    ///     Nests a multidimensional member's flat element list, already stored in the destination, by the member's
    ///     dimensions, replacing it in place; a member without a slot (unnamed padding) is left alone.
    /// </summary>
    /// <param name="destination">The value holding the member.</param>
    /// <param name="program">The program the member belongs to.</param>
    /// <param name="field">The member's index.</param>
    private static void ReshapeTable(StructValue destination, ReadProgram program, int field)
    {
        int slot = program.GetShapeSlot(field);
        if (slot >= 0 && destination.TryGetSlot(slot, out object? flat))
        {
            destination.StoreSlot(slot, CStruct.ReshapeFlatArrayValues((List<object?>)flat!, CStruct.FixedDimensionSizes(program.Fields[field])));
        }
    }

    /// <summary>
    ///     Reads one value of the member's caller-supplied codec through the cursor's custom-codec adapter. A codec that
    ///     reports success without a value fails with an <see cref="InvalidOperationException"/>.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state, whose layout holds the codec instances.</param>
    /// <param name="program">The program the member belongs to.</param>
    /// <param name="field">The member's index.</param>
    /// <param name="codec">The codec's index in the program.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="CStructReadException">The input ends before the value, or the codec rejects it.</exception>
    /// <exception cref="CStructReadLimitException">The value exceeds a budget.</exception>
    /// <exception cref="InvalidOperationException">The codec decoded no value.</exception>
    private static object ReadCustomValue<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, int field, int codec)
        where TCursor : struct, IReadCursor
    {
        ICustomCodec custom = state.Layout.Codecs.CustomCodecOf(program.Codecs[codec].CodecId);
        return cursor.ReadCustom(custom) ??
               throw new InvalidOperationException("Compiled field has no reader: " + program.Fields[field].DisplayTypeSpelling);
    }

    /// <summary>Reads count-register values of the member's caller-supplied codec into a list, one after another.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The program the member belongs to.</param>
    /// <param name="field">The member's index.</param>
    /// <param name="codec">The codec's index in the program.</param>
    /// <param name="count">The element count.</param>
    /// <returns>The elements.</returns>
    private static List<object?> ReadCustomArray<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, int field, int codec, int count)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        for (int index = 0; index < count; index++)
        {
            elements.Add(ReadCustomValue(ref cursor, ref state, program, field, codec));
        }

        return elements;
    }

    /// <summary>
    ///     Reads the flat elements of a multidimensional numeric array its composite placed into a list, through the
    ///     boxed block reader (64 KiB blocks, each one exact read); no elements read nothing.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="count">The number of elements in every dimension together.</param>
    /// <returns>The flat elements.</returns>
    private static List<object?> ReadNumericList<TCursor>(ref TCursor cursor, PrimitiveCodec codec, int count)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        if (count > 0)
        {
            _ = PrimitiveArrayReader.ReadInto(ref cursor, codec, count, elements);
        }

        return elements;
    }

    /// <summary>
    ///     Reads the flat elements of a multidimensional numeric root one at a time, as a field no composite placed is
    ///     read, into a list.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="count">The number of elements in every dimension together.</param>
    /// <param name="scratch">A buffer of at least one element.</param>
    /// <returns>The flat elements.</returns>
    private static List<object?> ReadNumericElementList<TCursor>(ref TCursor cursor, PrimitiveCodec codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        Span<byte> element = scratch[..codec.Size];
        for (int index = 0; index < count; index++)
        {
            elements.Add(codec.ReadNumeric(cursor.ReadFixed(element)));
        }

        return elements;
    }

    /// <summary>
    ///     Reads the flat elements of a multidimensional struct array, each as a struct of its own with a fresh conditional
    ///     selection (and its own static plan when it has one), into a list.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="element">The element struct's program.</param>
    /// <param name="count">The number of elements in every dimension together.</param>
    /// <returns>The flat elements.</returns>
    private static List<object?> ReadStructElements<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram element, int count)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        for (int index = 0; index < count; index++)
        {
            var value = new StructValue(element.Shape);
            ReadComposite(ref cursor, ref state, element, value);
            elements.Add(value);
        }

        return elements;
    }

    /// <summary>Reads <paramref name="count"/> characters one at a time through <see cref="ReadCodecValue{TCursor}"/> into a string.</summary>
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
    ///     Reads and discards the elements of an unnamed padding array one at a time: each element is read (and can
    ///     fail) exactly as a named one would be.
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
    ///     through its static plan over one in-memory span when the plan applies (cancellation observed before each
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
        CompiledCompositeType composite = element.Composite!;
        if (program.PlacesMembers && count > 0 && !state.NoFastPaths && composite.StaticPlan is { Size: > 0, } plan &&
            program.Fields[field].FixedElementSize == plan.Size && state.CoversPlan(plan) && (long)count * plan.Size <= int.MaxValue &&
            cursor.TryReadSpanWithinBudget(count * plan.Size, out ReadOnlySpan<byte> bytes))
        {
            var elements = new List<object?>(count);
            for (int index = 0; index < count; index++)
            {
                cursor.ThrowIfCancellationRequested();
                var value = new StructValue(composite.Shape);
                RunStaticPlan(ref cursor, ref state, plan, bytes.Slice(index * plan.Size, plan.Size), value);
                elements.Add(value);
            }

            return elements;
        }

        return ReadStructElements(ref cursor, ref state, element, count);
    }

    /// <summary>
    ///     Reads one bitfield: the storage unit the bit registers describe is read whole (a packed window whose placed
    ///     unit differs from the declared type as a raw unsigned unit, any other unit through its storage codec), a
    ///     field that overruns the unit fails, and the field's bits are decoded by the shared rule. While bits of the
    ///     unit remain after the field, the position returns to the unit's start, so the next bitfield of the unit
    ///     reads, and is charged for, the whole unit again.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the unit's start.</param>
    /// <param name="state">The operation's state, whose layout decodes the bits.</param>
    /// <param name="member">The bitfield.</param>
    /// <param name="codec">The bitfield's storage codec.</param>
    /// <param name="bitOffset">The field's bit offset in the unit; advanced past the field.</param>
    /// <param name="unitSize">The placed unit's size in bytes, 1 to 8.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    /// <returns>The field's value: an <see cref="int"/>, a <see cref="ulong"/>, or an enum result.</returns>
    /// <exception cref="CStructReadException">The input ends inside the unit, or the field overruns it.</exception>
    /// <exception cref="CStructReadLimitException">The unit exceeds the total read budget.</exception>
    private static object ReadBitfield<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField member, PrimitiveCodec codec, ref int bitOffset, int unitSize, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        long start = cursor.Position;
        object unit;
        if (unitSize != codec.Size)
        {
            ReadOnlySpan<byte> bytes = cursor.TryReadSpan(unitSize, out ReadOnlySpan<byte> direct) ? direct : ReadExact(ref cursor, scratch[..unitSize]);
            unit = BinaryPrimitiveIO.ReadUnsigned(bytes, member.BitStorageIsLittleEndian ?? true);
        }
        else
        {
            unit = codec.IsFixedWidthNumeric ? codec.ReadNumeric(cursor.ReadFixed(scratch[..codec.Size])) : ReadCodecValue(ref cursor, codec, scratch);
        }

        long end = cursor.Position;
        int unitBits = checked(unitSize * 8);
        if (bitOffset + member.BitSize > unitBits)
        {
            throw new CStructReadException(LayoutFailures.BitfieldExceedsUnit(member.Name));
        }

        object value = state.Layout.DecodeBitfield(member, unit, bitOffset, unitBits);
        bitOffset += member.BitSize;
        if (1 + (bitOffset / 8) <= end - start)
        {
            cursor.Position = start;
        }

        return value;
    }

    /// <summary>
    ///     Reads a union: its size (fixed, or measured from the variables at entry), its raw storage read and charged
    ///     whole, then - from its start, inside one nesting level (a promoted union only observes cancellation) and
    ///     with every variable restored to its entry value before each member - its member views, which are charged
    ///     again. Whatever happens in a member, the variables are restored and the position ends at the union's end, so
    ///     a failure inside a member reports that position; nothing a member captures is visible after the union.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the union's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The union's program (<see cref="ReadProgramKind.Union"/>).</param>
    /// <param name="promoted">Whether the union is an anonymous member whose views belong to its parent.</param>
    /// <returns>The union value with its raw storage and member views.</returns>
    /// <exception cref="CStructException">The size cannot be measured, the storage is short, a limit is exceeded, or a member fails.</exception>
    private static UnionValue ReadUnion<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, bool promoted)
        where TCursor : struct, IReadCursor
    {
        CompiledCompositeType union = program.Composite!;
        long start = cursor.Position;
        int size = union.Symbol.FixedSize ?? state.Layout.Compilation.SizeQueries.GetCompiledStructSizeInBytes(union, state.Slots.AsDictionary(), false);
        long end = checked(start + size);
        byte[] raw = new byte[size];
        cursor.ReadExactly(raw);
        cursor.Position = start;
        var members = new StructValue(union.Shape);
        if (promoted)
        {
            cursor.ThrowIfCancellationRequested();
        }
        else
        {
            state.EnterStructure(ref cursor);
        }

        int outer = state.SaveUnionSlots();
        bool suppressed = state.SuppressPointers;
        try
        {
            // An untagged union has no active member: a view never follows a pointer its bytes happen to hold.
            state.SuppressPointers = true;
            RunFrame(ref cursor, ref state, program, members);
        }
        finally
        {
            state.RestoreUnionSlots();
            state.ReleaseUnionSlots(outer);
            state.SuppressPointers = suppressed;
            if (!promoted)
            {
                state.StructureDepth--;
            }

            cursor.Position = end;
        }

        return UnionValue.FromParsed(union.Name, raw, members);
    }

    /// <summary>Reads count-register unions, each a union value of its own, into a list.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="element">The element union's program.</param>
    /// <param name="count">The element count.</param>
    /// <returns>The elements.</returns>
    private static List<object?> ReadUnionArray<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram element, int count)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        for (int index = 0; index < count; index++)
        {
            elements.Add(ReadUnion(ref cursor, ref state, element, promoted: false));
        }

        return elements;
    }

    /// <summary>
    ///     Runs a fixed composite's static read plan over its bytes with the member-by-member read's side effects: one
    ///     nesting level per struct (cancellation observed on entry), values stored straight into their slots, and the
    ///     captures and qualified publications a later expression may read.
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
        SlotValue captured = LayoutVariableCapture.ToSlotValue(field, value);
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
