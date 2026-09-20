namespace CStructSharp.Generated;

using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>
///     The primitive decode and encode functions generated code calls, and the same functions the runtime reader
///     and writer use: one implementation of every byte-level rule (byte order, the 24- and 48-bit integers, LEB128,
///     fixed point, identifiers, bitfield slices, primitive arrays, bounded text). Every function works on spans,
///     takes the byte order as an argument, and reports the runtime's own failure texts.
/// </summary>
/// <remarks>
///     This is an advanced surface: application code reads values through <see cref="CStruct"/> or a generated
///     layout class. It is public so the code the <c>[CStructLayout]</c> generator emits into your assembly can
///     call it.
/// </remarks>
public static class Codec
{
    private const int MaximumLeb128Bytes = 10;

    // ------------------------------------------------------------------------------------------ fixed-width integers

    /// <summary>Reads a signed 16-bit integer.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static short ReadInt16(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(source) : BinaryPrimitives.ReadInt16BigEndian(source);

    /// <summary>Reads an unsigned 16-bit integer.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static ushort ReadUInt16(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(source) : BinaryPrimitives.ReadUInt16BigEndian(source);

    /// <summary>Reads a three-byte integer and sign-extends bit 23.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static int ReadInt24(ReadOnlySpan<byte> source, bool littleEndian)
        => unchecked((int)(ReadUInt24(source, littleEndian) << 8)) >> 8;

    /// <summary>Reads an unsigned three-byte integer.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static uint ReadUInt24(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian
               ? (uint)(source[0] | (source[1] << 8) | (source[2] << 16))
               : (uint)(source[2] | (source[1] << 8) | (source[0] << 16));

    /// <summary>Reads a signed 32-bit integer.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static int ReadInt32(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(source) : BinaryPrimitives.ReadInt32BigEndian(source);

    /// <summary>Reads an unsigned 32-bit integer.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static uint ReadUInt32(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(source) : BinaryPrimitives.ReadUInt32BigEndian(source);

    /// <summary>Reads a six-byte integer and sign-extends bit 47.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static long ReadInt48(ReadOnlySpan<byte> source, bool littleEndian)
        => unchecked((long)(ReadUInt48(source, littleEndian) << 16)) >> 16;

    /// <summary>Reads an unsigned six-byte integer.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static ulong ReadUInt48(ReadOnlySpan<byte> source, bool littleEndian)
    {
        ulong value = 0;
        for (int index = 0; index < 6; index++)
        {
            int shift = littleEndian ? index * 8 : (5 - index) * 8;
            value |= (ulong)source[index] << shift;
        }

        return value;
    }

    /// <summary>Reads a signed 64-bit integer.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static long ReadInt64(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadInt64LittleEndian(source) : BinaryPrimitives.ReadInt64BigEndian(source);

    /// <summary>Reads an unsigned 64-bit integer.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static ulong ReadUInt64(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(source) : BinaryPrimitives.ReadUInt64BigEndian(source);

    /// <summary>Reads a signed 128-bit integer.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static Int128 ReadInt128(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadInt128LittleEndian(source) : BinaryPrimitives.ReadInt128BigEndian(source);

    /// <summary>Reads an unsigned 128-bit integer.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static UInt128 ReadUInt128(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadUInt128LittleEndian(source) : BinaryPrimitives.ReadUInt128BigEndian(source);

    /// <summary>Reads an IEEE 754 binary16 value bit for bit.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static Half ReadHalf(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadHalfLittleEndian(source) : BinaryPrimitives.ReadHalfBigEndian(source);

    /// <summary>Reads an IEEE 754 binary32 value.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static float ReadSingle(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadSingleLittleEndian(source) : BinaryPrimitives.ReadSingleBigEndian(source);

    /// <summary>Reads an IEEE 754 binary64 value.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static double ReadDouble(ReadOnlySpan<byte> source, bool littleEndian)
        => littleEndian ? BinaryPrimitives.ReadDoubleLittleEndian(source) : BinaryPrimitives.ReadDoubleBigEndian(source);

    /// <summary>Reads a two-byte UTF-16 code unit.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static char ReadChar(ReadOnlySpan<byte> source, bool littleEndian)
        => (char)ReadUInt16(source, littleEndian);

    /// <summary>
    ///     Reads an unsigned integer of one to eight bytes - the whole of <paramref name="source"/> - in the given
    ///     byte order: a pointer of the layout's width, or a bitfield storage unit.
    /// </summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <returns>The decoded value.</returns>
    public static ulong ReadUnsigned(ReadOnlySpan<byte> source, bool littleEndian)
    {
        switch (source.Length)
        {
        case 1:
            return source[0];
        case 2:
            return ReadUInt16(source, littleEndian);
        case 4:
            return ReadUInt32(source, littleEndian);
        case 8:
            return ReadUInt64(source, littleEndian);
        case > 0 and <= 8:
            {
                // A packed SysV bitfield window can be any width up to eight bytes.
                ulong value = 0;
                for (int index = 0; index < source.Length; index++)
                {
                    int shift = littleEndian ? index * 8 : (source.Length - 1 - index) * 8;
                    value |= (ulong)source[index] << shift;
                }

                return value;
            }

        default:
            throw new InvalidOperationException("Unsupported integer size: " + source.Length);
        }
    }

    /// <summary>Writes a signed 16-bit integer.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteInt16(Span<byte> destination, short value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt16LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteInt16BigEndian(destination, value);
        }
    }

    /// <summary>Writes an unsigned 16-bit integer.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteUInt16(Span<byte> destination, ushort value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
        }
    }

    /// <summary>Writes a signed three-byte integer after checking its range.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <exception cref="CStructWriteException">The value does not fit 24 signed bits.</exception>
    public static void WriteInt24(Span<byte> destination, int value, bool littleEndian)
    {
        if (value is < -8388608 or > 8388607)
        {
            throw new CStructWriteException("Value is outside the int24 range.");
        }

        WriteUInt24Unchecked(destination, unchecked((uint)value) & 0xffffff, littleEndian);
    }

    /// <summary>Writes an unsigned three-byte integer after checking its range.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <exception cref="CStructWriteException">The value does not fit 24 bits.</exception>
    public static void WriteUInt24(Span<byte> destination, uint value, bool littleEndian)
    {
        if (value > 0xffffff)
        {
            throw new CStructWriteException("Value is outside the uint24 range.");
        }

        WriteUInt24Unchecked(destination, value, littleEndian);
    }

    /// <summary>Writes a signed 32-bit integer.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteInt32(Span<byte> destination, int value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(destination, value);
        }
    }

    /// <summary>Writes an unsigned 32-bit integer.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteUInt32(Span<byte> destination, uint value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(destination, value);
        }
    }

    /// <summary>Writes a signed six-byte integer after checking its range.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <exception cref="CStructWriteException">The value does not fit 48 signed bits.</exception>
    public static void WriteInt48(Span<byte> destination, long value, bool littleEndian)
    {
        if (value is < -140_737_488_355_328 or > 140_737_488_355_327)
        {
            throw new CStructWriteException("Value is outside the int48 range.");
        }

        WriteUInt48Unchecked(destination, unchecked((ulong)value) & 0xffff_ffff_ffff, littleEndian);
    }

    /// <summary>Writes an unsigned six-byte integer after checking its range.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <exception cref="CStructWriteException">The value does not fit 48 bits.</exception>
    public static void WriteUInt48(Span<byte> destination, ulong value, bool littleEndian)
    {
        if (value > 0xffff_ffff_ffff)
        {
            throw new CStructWriteException("Value is outside the uint48 range.");
        }

        WriteUInt48Unchecked(destination, value, littleEndian);
    }

    /// <summary>Writes a signed 64-bit integer.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteInt64(Span<byte> destination, long value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt64LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteInt64BigEndian(destination, value);
        }
    }

    /// <summary>Writes an unsigned 64-bit integer.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteUInt64(Span<byte> destination, ulong value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(destination, value);
        }
    }

    /// <summary>Writes a signed 128-bit integer.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteInt128(Span<byte> destination, Int128 value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteInt128LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteInt128BigEndian(destination, value);
        }
    }

    /// <summary>Writes an unsigned 128-bit integer.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteUInt128(Span<byte> destination, UInt128 value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt128LittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt128BigEndian(destination, value);
        }
    }

    /// <summary>Writes an IEEE 754 binary16 value bit for bit.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteHalf(Span<byte> destination, Half value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteHalfLittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteHalfBigEndian(destination, value);
        }
    }

    /// <summary>Writes an IEEE 754 binary32 value.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteSingle(Span<byte> destination, float value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteSingleLittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteSingleBigEndian(destination, value);
        }
    }

    /// <summary>Writes an IEEE 754 binary64 value.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteDouble(Span<byte> destination, double value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(destination, value);
        }
        else
        {
            BinaryPrimitives.WriteDoubleBigEndian(destination, value);
        }
    }

    /// <summary>Writes a two-byte UTF-16 code unit.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteChar(Span<byte> destination, char value, bool littleEndian)
        => WriteUInt16(destination, value, littleEndian);

    /// <summary>
    ///     Writes an unsigned integer into one to eight bytes - the whole of <paramref name="destination"/> - in the
    ///     given byte order: a pointer of the layout's width, or a bitfield storage unit.
    /// </summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void WriteUnsigned(Span<byte> destination, ulong value, bool littleEndian)
    {
        switch (destination.Length)
        {
        case 1:
            destination[0] = (byte)value;
            break;
        case 2:
            WriteUInt16(destination, (ushort)value, littleEndian);
            break;
        case 4:
            WriteUInt32(destination, (uint)value, littleEndian);
            break;
        case 8:
            WriteUInt64(destination, value, littleEndian);
            break;
        case > 0 and <= 8:
            for (int index = 0; index < destination.Length; index++)
            {
                int shift = littleEndian ? index * 8 : (destination.Length - 1 - index) * 8;
                destination[index] = (byte)(value >> shift);
            }

            break;
        default:
            throw new InvalidOperationException("Unsupported integer size: " + destination.Length);
        }
    }

    // ------------------------------------------------------------------------------------------ LEB128

    /// <summary>
    ///     Decodes one LEB128 integer of at most <paramref name="width"/> payload bits (32 or 64) from the start of
    ///     <paramref name="source"/>. A signed value is sign-extended into the returned bits; cast the result to
    ///     <see cref="int"/> or <see cref="long"/>.
    /// </summary>
    /// <param name="source">The bytes to decode from; the integer may end before the span does.</param>
    /// <param name="width">32 or 64.</param>
    /// <param name="signed">Whether the encoding is SLEB128.</param>
    /// <param name="bytesConsumed">How many bytes the integer occupied.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="CStructReadException">The integer is unterminated within the span or exceeds its width.</exception>
    public static ulong ReadLeb128(ReadOnlySpan<byte> source, int width, bool signed, out int bytesConsumed)
    {
        var decoder = new Leb128Decoder(width, signed);
        for (int index = 0; index < source.Length && index < MaximumLeb128Bytes; index++)
        {
            if (decoder.Push(source[index], out ulong value))
            {
                bytesConsumed = index + 1;
                return value;
            }
        }

        throw new CStructReadException(source.Length < MaximumLeb128Bytes ? Leb128Decoder.ShortRead : Leb128Decoder.Unterminated);
    }

    /// <summary>Encodes an unsigned LEB128 integer; the destination needs at most ten bytes.</summary>
    /// <returns>The number of bytes written.</returns>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    public static int WriteULeb128(Span<byte> destination, ulong value)
    {
        int written = 0;
        do
        {
            byte octet = (byte)(value & 127);
            value >>= 7;
            destination[written++] = value == 0 ? octet : (byte)(octet | 128);
        }
        while (value != 0);
        return written;
    }

    /// <summary>Encodes a signed LEB128 integer; the destination needs at most ten bytes.</summary>
    /// <returns>The number of bytes written.</returns>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    public static int WriteSLeb128(Span<byte> destination, long value)
    {
        int written = 0;
        while (true)
        {
            byte octet = (byte)(value & 127);
            value >>= 7;
            bool done = (value == 0 && (octet & 64) == 0) || (value == -1 && (octet & 64) != 0);
            destination[written++] = done ? octet : (byte)(octet | 128);
            if (done)
            {
                return written;
            }
        }
    }

    // ------------------------------------------------------------------------------------------ fixed point

    /// <summary>Converts the raw integer of a fixed-point field (<c>fixed16_16</c>, <c>ufixed8_8</c>, ...) to its value.</summary>
    /// <param name="raw">The stored integer, already sign-extended for a signed format.</param>
    /// <param name="fractionBits">The number of fraction bits (16, 30, or 8).</param>
    /// <returns>The decoded value.</returns>
    public static double DecodeFixedPoint(long raw, int fractionBits)
        => raw / Math.Pow(2, fractionBits);

    /// <summary>
    ///     Converts a value to the raw integer of a fixed-point field, rejecting anything that is not exactly on the
    ///     storage grid or is outside the range - the same rule for every writer.
    /// </summary>
    /// <param name="value">A number, or a <see cref="decimal"/> for an exact fraction.</param>
    /// <param name="widthBits">The storage width (16 or 32).</param>
    /// <param name="fractionBits">The number of fraction bits.</param>
    /// <param name="signed">Whether the format is signed.</param>
    /// <returns>The encoded integer.</returns>
    /// <exception cref="CStructWriteException">The value is off the grid or out of range.</exception>
    public static long EncodeFixedPoint(object value, int widthBits, int fractionBits, bool signed)
    {
        ArgumentNullException.ThrowIfNull(value);
        double scale = Math.Pow(2, fractionBits);
        double raw;
        if (value is decimal exact)
        {
            // Converting to Double first can erase a small off-grid decimal fraction.
            decimal scaled = checked(exact * (decimal)scale);
            if (scaled != decimal.Truncate(scaled))
            {
                throw new CStructWriteException("Fixed-point value is outside the exact storage grid or range.");
            }

            raw = (double)scaled;
        }
        else
        {
            raw = Convert.ToDouble(value, CultureInfo.InvariantCulture) * scale;
        }

        double minimum = signed ? -Math.Pow(2, widthBits - 1) : 0;
        double maximum = Math.Pow(2, signed ? widthBits - 1 : widthBits) - 1;
        if (!double.IsFinite(raw) || raw != Math.Truncate(raw) || raw < minimum || raw > maximum)
        {
            throw new CStructWriteException("Fixed-point value is outside the exact storage grid or range.");
        }

        return (long)raw;
    }

    // ------------------------------------------------------------------------------------------ identifiers

    /// <summary>Reads a 16-byte identifier: <c>uuid</c> stores it in network (big-endian) order, <c>guid</c> in Windows order.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="networkOrder">Whether the identifier is stored in network (big-endian) order.</param>
    /// <returns>The decoded value.</returns>
    public static Guid ReadGuid(ReadOnlySpan<byte> source, bool networkOrder)
        => new(source[..16], bigEndian: networkOrder);

    /// <summary>Writes a 16-byte identifier in network or Windows order.</summary>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="networkOrder">Whether the identifier is stored in network (big-endian) order.</param>
    public static void WriteGuid(Span<byte> destination, Guid value, bool networkOrder)
        => value.TryWriteBytes(destination, bigEndian: networkOrder, out _);

    /// <summary>Accepts a <see cref="Guid"/> or its canonical <c>D</c> text as an identifier to write.</summary>
    /// <param name="value">The value to write.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="CStructWriteException">The value is neither.</exception>
    public static Guid ToGuid(object value)
    {
        if (value is Guid typed)
        {
            return typed;
        }

        if (value is string text && Guid.TryParseExact(text, "D", out Guid parsed))
        {
            return parsed;
        }

        throw new CStructWriteException("Identifier requires a Guid or canonical xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx text.");
    }

    // ------------------------------------------------------------------------------------------ bitfields

    /// <summary>
    ///     The shift of a bitfield slice inside its storage unit: the declaration-order bit offset for low-bit-first
    ///     allocation, or counted down from the unit's top bit for high-bit-first allocation.
    /// </summary>
    /// <param name="bitOffset">The declaration-order bit offset.</param>
    /// <param name="bitSize">The width of the slice in bits.</param>
    /// <param name="unitBits">The storage unit width in bits.</param>
    /// <param name="highBitFirst">Whether bits are allocated from the top of the unit.</param>
    /// <returns>The shift.</returns>
    public static int BitfieldShift(int bitOffset, int bitSize, int unitBits, bool highBitFirst)
        => BitfieldCodecTable.EffectiveShift(bitOffset, bitSize, unitBits, highBitFirst);

    /// <summary>Extracts <paramref name="bitSize"/> bits starting at <paramref name="shift"/> from a storage unit.</summary>
    /// <param name="unit">The storage unit.</param>
    /// <param name="shift">The bit offset of the slice inside the unit.</param>
    /// <param name="bitSize">The width of the slice in bits.</param>
    /// <returns>The extracted bits.</returns>
    public static ulong ExtractBits(ulong unit, int shift, int bitSize)
        => (unit >> shift) & BitfieldCodecTable.GetBitfieldMask(bitSize);

    /// <summary>Replaces <paramref name="bitSize"/> bits at <paramref name="shift"/> in a storage unit, keeping its other bits.</summary>
    /// <param name="unit">The storage unit.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="shift">The bit offset of the slice inside the unit.</param>
    /// <param name="bitSize">The width of the slice in bits.</param>
    /// <returns>The updated storage unit.</returns>
    public static ulong MergeBits(ulong unit, ulong value, int shift, int bitSize)
        => BitfieldCodecTable.MergeBitfieldValue(unit, value, shift, bitSize);

    /// <summary>Converts and validates a value against one bitfield's unsigned domain, with the runtime's messages.</summary>
    /// <param name="name">The member name, for the message.</param>
    /// <param name="bitSize">The width of the slice in bits.</param>
    /// <param name="value">The value to write.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="CStructWriteException">The value is null, not an integer, or does not fit.</exception>
    public static ulong ToBitfieldValue(string name, int bitSize, object? value)
        => BitfieldCodecTable.ValidateBitfieldWriteValue(name, bitSize, value);

    // ------------------------------------------------------------------------------------------ primitive arrays

    /// <summary>Decodes packed two-, four-, or eight-byte integers (or floats through their integer view) into typed elements.</summary>
    /// <typeparam name="T">An integer or floating type of the element width.</typeparam>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void DecodeIntegers<T>(ReadOnlySpan<byte> source, Span<T> destination, bool littleEndian)
        where T : unmanaged
    {
        source[..(destination.Length * Unsafe.SizeOf<T>())].CopyTo(MemoryMarshal.AsBytes(destination));
        if (littleEndian == BitConverter.IsLittleEndian || Unsafe.SizeOf<T>() == 1)
        {
            return;
        }

        switch (Unsafe.SizeOf<T>())
        {
        case 2:
            {
                Span<ushort> view = MemoryMarshal.Cast<T, ushort>(destination);
                BinaryPrimitives.ReverseEndianness(view, view);
                break;
            }

        case 4:
            {
                Span<uint> view = MemoryMarshal.Cast<T, uint>(destination);
                BinaryPrimitives.ReverseEndianness(view, view);
                break;
            }

        case 8:
            {
                Span<ulong> view = MemoryMarshal.Cast<T, ulong>(destination);
                BinaryPrimitives.ReverseEndianness(view, view);
                break;
            }

        default:
            throw new InvalidOperationException("Unsupported integer element size: " + Unsafe.SizeOf<T>());
        }
    }

    /// <summary>Encodes typed two-, four-, or eight-byte elements into packed bytes in the given byte order.</summary>
    /// <typeparam name="T">An integer or floating type of the element width.</typeparam>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void EncodeIntegers<T>(ReadOnlySpan<T> source, Span<byte> destination, bool littleEndian)
        where T : unmanaged
    {
        Span<byte> target = destination[..(source.Length * Unsafe.SizeOf<T>())];
        MemoryMarshal.AsBytes(source).CopyTo(target);
        if (littleEndian == BitConverter.IsLittleEndian || Unsafe.SizeOf<T>() == 1)
        {
            return;
        }

        switch (Unsafe.SizeOf<T>())
        {
        case 2:
            {
                Span<ushort> view = MemoryMarshal.Cast<byte, ushort>(target);
                BinaryPrimitives.ReverseEndianness(view, view);
                break;
            }

        case 4:
            {
                Span<uint> view = MemoryMarshal.Cast<byte, uint>(target);
                BinaryPrimitives.ReverseEndianness(view, view);
                break;
            }

        case 8:
            {
                Span<ulong> view = MemoryMarshal.Cast<byte, ulong>(target);
                BinaryPrimitives.ReverseEndianness(view, view);
                break;
            }

        default:
            throw new InvalidOperationException("Unsupported integer element size: " + Unsafe.SizeOf<T>());
        }
    }

    /// <summary>Decodes one-byte booleans (any non-zero byte is <see langword="true"/>).</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="destination">The bytes to write into.</param>
    public static void DecodeBooleans(ReadOnlySpan<byte> source, Span<bool> destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] = source[index] != 0;
        }
    }

    /// <summary>Decodes packed signed three-byte integers.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void DecodeInt24(ReadOnlySpan<byte> source, Span<int> destination, bool littleEndian)
    {
        for (int index = 0, offset = 0; index < destination.Length; index++, offset += 3)
        {
            destination[index] = ReadInt24(source.Slice(offset, 3), littleEndian);
        }
    }

    /// <summary>Decodes packed unsigned three-byte integers.</summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="destination">The bytes to write into.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    public static void DecodeUInt24(ReadOnlySpan<byte> source, Span<uint> destination, bool littleEndian)
    {
        for (int index = 0, offset = 0; index < destination.Length; index++, offset += 3)
        {
            destination[index] = ReadUInt24(source.Slice(offset, 3), littleEndian);
        }
    }

    // ------------------------------------------------------------------------------------------ text

    /// <summary>
    ///     Decodes a fixed-capacity buffer of one-byte characters (<c>char[N]</c>) as Latin-1 code points, as the
    ///     runtime does; <paramref name="trimTrailingNuls"/> applies <c>ReadOptions.TrimFixedText</c>.
    /// </summary>
    /// <param name="source">The bytes to read from.</param>
    /// <param name="trimTrailingNuls">Whether trailing NUL characters are dropped.</param>
    /// <returns>The decoded value.</returns>
    public static string DecodeFixedText(ReadOnlySpan<byte> source, bool trimTrailingNuls)
    {
        // Latin-1 maps every byte to the code point of the same value, which is what the runtime's char[N] read produces.
        string text = System.Text.Encoding.Latin1.GetString(source);
        return trimTrailingNuls ? text.TrimEnd('\0') : text;
    }

    /// <summary>Decodes an encoded text buffer (<c>utf8[N]</c>, <c>latin1[N]</c>, <c>cp437[N]</c>, <c>utf16le[N]</c>, <c>utf16be[N]</c>).</summary>
    /// <param name="source">The buffer's bytes.</param>
    /// <param name="encoding">The layout's encoding name.</param>
    /// <param name="trimTrailingNuls">Whether to apply <c>ReadOptions.TrimFixedText</c>.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="CStructReadException">The bytes are not valid in the encoding.</exception>
    public static string DecodeBoundedText(ReadOnlySpan<byte> source, string encoding, bool trimTrailingNuls)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        string text;
        try
        {
            text = BoundedTextCodec.Decode(encoding, source.ToArray());
        }
        catch (System.Text.DecoderFallbackException exception)
        {
            throw new CStructReadException(ReadFailures.BoundedTextInvalid, exception);
        }

        return trimTrailingNuls ? text.TrimEnd('\0') : text;
    }

    /// <summary>
    ///     Finds a terminator in encoded text: the index of the first occurrence that starts on an encoding-unit
    ///     boundary (<paramref name="unitSize"/> 1 or 2, <paramref name="alignmentOffset"/> the offset of the first
    ///     boundary), or -1.
    /// </summary>
    /// <param name="data">The encoded text to search.</param>
    /// <param name="terminator">The encoded terminator bytes.</param>
    /// <param name="unitSize">The encoding unit size (1 or 2 bytes).</param>
    /// <param name="alignmentOffset">The offset of the first encoding-unit boundary.</param>
    /// <returns>The index, or -1.</returns>
    public static int FindTerminator(ReadOnlySpan<byte> data, ReadOnlySpan<byte> terminator, int unitSize, int alignmentOffset)
    {
        if (unitSize == 1)
        {
            return data.IndexOf(terminator[0]);
        }

        for (int index = alignmentOffset; index + terminator.Length <= data.Length; index += unitSize)
        {
            if (data.Slice(index, terminator.Length).SequenceEqual(terminator))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Converts a value to the one-byte character a <c>char</c> field stores.</summary>
    /// <param name="value">The value to write.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="CStructWriteException">The character is above U+00FF.</exception>
    public static byte ToNarrowCharacter(object value)
        => PrimitiveCodecs.ConvertToNarrowCharacter(value);

    /// <summary>Whether the options trim trailing NULs from fixed text (<c>ReadOptions.TrimFixedText</c>), for a view's text accessor.</summary>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>Whether trailing NULs are trimmed.</returns>
    public static bool TrimsFixedText(ReadOptions? options) => options?.TrimFixedText ?? false;

    /// <summary>Decodes a fixed wide-character buffer (<c>wchar[N]</c>) as a view does: validated UTF-16 code units, <c>TrimFixedText</c> applied.</summary>
    /// <param name="source">The buffer's bytes (two per character).</param>
    /// <param name="littleEndian">The code units' byte order.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The text.</returns>
    /// <exception cref="CStructReadException">The code units are not valid UTF-16.</exception>
    public static string DecodeWideText(ReadOnlySpan<byte> source, bool littleEndian, ReadOptions? options)
    {
        var characters = new char[source.Length / 2];
        for (int index = 0; index < characters.Length; index++)
        {
            characters[index] = ReadChar(source.Slice(index * 2, 2), littleEndian);
        }

        string text = new(characters);
        try
        {
            _ = (littleEndian ? PrimitiveCodecs.StrictUtf16LittleEndianEncoding : PrimitiveCodecs.StrictUtf16BigEndianEncoding).GetByteCount(text);
        }
        catch (System.Text.EncoderFallbackException exception)
        {
            throw new CStructReadException(ReadFailures.WideTextInvalid, exception);
        }

        return TrimsFixedText(options) ? text.TrimEnd('\0') : text;
    }

    /// <summary>Converts one character to the one-byte domain of the layout's <c>char</c> type.</summary>
    /// <param name="value">The character.</param>
    /// <returns>The byte.</returns>
    /// <exception cref="CStructWriteException">The character is above U+00FF.</exception>
    public static byte ToNarrowCharacter(char value)
        => value > byte.MaxValue ? throw new CStructWriteException(WriteFailures.NarrowCharacter(value)) : (byte)value;

    private static void WriteUInt24Unchecked(Span<byte> destination, uint value, bool littleEndian)
    {
        destination[1] = (byte)(value >> 8);
        destination[littleEndian ? 0 : 2] = (byte)value;
        destination[littleEndian ? 2 : 0] = (byte)(value >> 16);
    }

    private static void WriteUInt48Unchecked(Span<byte> destination, ulong value, bool littleEndian)
    {
        for (int index = 0; index < 6; index++)
        {
            int shift = littleEndian ? index * 8 : (5 - index) * 8;
            destination[index] = (byte)(value >> shift);
        }
    }
}
