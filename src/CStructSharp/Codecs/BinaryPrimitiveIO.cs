namespace CStructSharp.Codecs;

using System;
using System.Buffers.Binary;
using System.IO;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>Reads and writes fixed-width unsigned integers in a caller-chosen byte order.</summary>
internal static class BinaryPrimitiveIO
{
    /// <summary>Reads an unsigned three-byte integer.</summary>
    public static uint ReadUInt24(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[3];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian
                   ? (uint)(buffer[0] | (buffer[1] << 8) | (buffer[2] << 16))
                   : (uint)(buffer[2] | (buffer[1] << 8) | (buffer[0] << 16));
    }

    /// <summary>Reads a three-byte integer and sign-extends bit 23.</summary>
    public static int ReadInt24(Stream stream, bool isLittleEndian)
        => unchecked((int)(ReadUInt24(stream, isLittleEndian) << 8)) >> 8;

    /// <summary>Writes an unsigned three-byte integer after checking its range.</summary>
    public static void WriteUInt24(Stream stream, uint value, bool isLittleEndian)
    {
        if (value > 0xffffff)
        {
            throw new CStructWriteException("Value is outside the uint24 range.");
        }

        Span<byte> buffer = stackalloc byte[3];
        buffer[1] = (byte)(value >> 8);
        buffer[isLittleEndian ? 0 : 2] = (byte)value;
        buffer[isLittleEndian ? 2 : 0] = (byte)(value >> 16);
        stream.Write(buffer);
    }

    /// <summary>Writes a signed three-byte integer after checking its range.</summary>
    public static void WriteInt24(Stream stream, int value, bool isLittleEndian)
    {
        if (value is < -8388608 or > 8388607)
        {
            throw new CStructWriteException("Value is outside the int24 range.");
        }

        WriteUInt24(stream, unchecked((uint)value) & 0xffffff, isLittleEndian);
    }

    /// <summary>Reads an unsigned six-byte integer in the requested byte order.</summary>
    public static ulong ReadUInt48(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[6];
        ReadExactlyOrThrow(stream, buffer);
        ulong value = 0;
        for (int index = 0; index < 6; index++)
        {
            int shift = isLittleEndian ? index * 8 : (5 - index) * 8;
            value |= (ulong)buffer[index] << shift;
        }

        return value;
    }

    /// <summary>Reads a six-byte integer and sign-extends bit 47.</summary>
    public static long ReadInt48(Stream stream, bool isLittleEndian)
        => unchecked((long)(ReadUInt48(stream, isLittleEndian) << 16)) >> 16;

    /// <summary>Writes an unsigned six-byte integer after checking its range.</summary>
    public static void WriteUInt48(Stream stream, ulong value, bool isLittleEndian)
    {
        if (value > 0xffff_ffff_ffff)
        {
            throw new CStructWriteException("Value is outside the uint48 range.");
        }

        Span<byte> buffer = stackalloc byte[6];
        for (int index = 0; index < 6; index++)
        {
            int shift = isLittleEndian ? index * 8 : (5 - index) * 8;
            buffer[index] = (byte)(value >> shift);
        }

        stream.Write(buffer);
    }

    /// <summary>Writes a signed six-byte integer after checking its range.</summary>
    public static void WriteInt48(Stream stream, long value, bool isLittleEndian)
    {
        if (value is < -140_737_488_355_328 or > 140_737_488_355_327)
        {
            throw new CStructWriteException("Value is outside the int48 range.");
        }

        WriteUInt48(stream, unchecked((ulong)value) & 0xffff_ffff_ffff, isLittleEndian);
    }

    /// <summary>Reads a sixteen-byte signed integer in the requested byte order.</summary>
    public static Int128 ReadInt128(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadInt128LittleEndian(buffer) : BinaryPrimitives.ReadInt128BigEndian(buffer);
    }

    /// <summary>Reads a sixteen-byte unsigned integer in the requested byte order.</summary>
    public static UInt128 ReadUInt128(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadUInt128LittleEndian(buffer) : BinaryPrimitives.ReadUInt128BigEndian(buffer);
    }

    /// <summary>Writes a sixteen-byte signed integer in the requested byte order.</summary>
    public static void WriteInt128(Stream stream, Int128 value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteInt128LittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteInt128BigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Writes a sixteen-byte unsigned integer in the requested byte order.</summary>
    public static void WriteUInt128(Stream stream, UInt128 value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteUInt128LittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt128BigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Reads an IEEE-754 binary16 value bit for bit.</summary>
    public static Half ReadHalf(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadHalfLittleEndian(buffer) : BinaryPrimitives.ReadHalfBigEndian(buffer);
    }

    /// <summary>Writes an IEEE-754 binary16 value bit for bit.</summary>
    public static void WriteHalf(Stream stream, Half value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteHalfLittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteHalfBigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Reads one required byte and turns an unexpected end of stream into a layout-specific error.</summary>
    public static byte ReadByteExactly(Stream stream)
    {
        int value = stream.ReadByte();
        if (value < 0)
        {
            throw new CStructReadException("Not enough bytes: needed 1, available 0.");
        }

        return (byte)value;
    }

    /// <summary>
    ///     Describes a short read: how many bytes the item needed and how many the source still had (when the
    ///     source can tell), so the caller knows whether the input is truncated or the layout is wrong.
    /// </summary>
    internal static string DescribeShortRead(Stream stream, int needed)
    {
        long? available = null;
        try
        {
            if (stream.CanSeek)
            {
                available = Math.Max(0, stream.Length - stream.Position);
            }
        }
        catch (Exception exception) when (exception is IOException or NotSupportedException or ObjectDisposedException)
        {
            available = null;
        }

        return available is { } remaining
                   ? $"Not enough bytes: needed {needed}, available {remaining}."
                   : $"Not enough bytes: needed {needed}.";
    }

    /// <summary>Reads a two-byte UTF-16 code unit in the requested byte order.</summary>
    public static char ReadChar(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return (char)(isLittleEndian
                          ? BinaryPrimitives.ReadUInt16LittleEndian(buffer)
                          : BinaryPrimitives.ReadUInt16BigEndian(buffer));
    }

    /// <summary>Reads a two-byte signed integer in the requested byte order.</summary>
    public static short ReadInt16(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadInt16LittleEndian(buffer) : BinaryPrimitives.ReadInt16BigEndian(buffer);
    }

    /// <summary>Reads a two-byte unsigned integer in the requested byte order.</summary>
    public static ushort ReadUInt16(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(buffer) : BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    /// <summary>Reads a four-byte signed integer in the requested byte order.</summary>
    public static int ReadInt32(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadInt32LittleEndian(buffer) : BinaryPrimitives.ReadInt32BigEndian(buffer);
    }

    /// <summary>Reads a four-byte unsigned integer in the requested byte order.</summary>
    public static uint ReadUInt32(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(buffer) : BinaryPrimitives.ReadUInt32BigEndian(buffer);
    }

    /// <summary>Reads an eight-byte signed integer in the requested byte order.</summary>
    public static long ReadInt64(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadInt64LittleEndian(buffer) : BinaryPrimitives.ReadInt64BigEndian(buffer);
    }

    /// <summary>Reads an eight-byte unsigned integer in the requested byte order.</summary>
    public static ulong ReadUInt64(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(buffer) : BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    /// <summary>Reads a four-byte IEEE 754 value in the requested byte order.</summary>
    public static float ReadSingle(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadSingleLittleEndian(buffer) : BinaryPrimitives.ReadSingleBigEndian(buffer);
    }

    /// <summary>Reads an eight-byte IEEE 754 value in the requested byte order.</summary>
    public static double ReadDouble(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactlyOrThrow(stream, buffer);
        return isLittleEndian ? BinaryPrimitives.ReadDoubleLittleEndian(buffer) : BinaryPrimitives.ReadDoubleBigEndian(buffer);
    }

    /// <summary>
    ///     Reads a 1, 2, 4, or 8-byte unsigned integer whose width is a runtime value rather than known at the call
    ///     site - used for the layout's configured pointer width.
    /// </summary>
    public static ulong ReadUnsignedBySize(Stream stream, int byteSize, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        Span<byte> slice = buffer[..byteSize];
        ReadExactlyOrThrow(stream, slice);
        return byteSize switch
        {
            1 => slice[0],
            2 => isLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(slice) : BinaryPrimitives.ReadUInt16BigEndian(slice),
            4 => isLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(slice) : BinaryPrimitives.ReadUInt32BigEndian(slice),
            8 => isLittleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(slice) : BinaryPrimitives.ReadUInt64BigEndian(slice),
            _ => throw new InvalidOperationException("Unsupported integer size: " + byteSize),
        };
    }

    /// <summary>Converts one to eight bytes into an unsigned number in the layout's byte order.</summary>
    public static ulong ReadUnsigned(ReadOnlySpan<byte> buffer, bool littleEndian)
    {
        switch (buffer.Length)
        {
            case 1:
                return buffer[0];
            case 2:
                return littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(buffer) : BinaryPrimitives.ReadUInt16BigEndian(buffer);
            case 4:
                return littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(buffer) : BinaryPrimitives.ReadUInt32BigEndian(buffer);
            case 8:
                return littleEndian ? BinaryPrimitives.ReadUInt64LittleEndian(buffer) : BinaryPrimitives.ReadUInt64BigEndian(buffer);
            case > 0 and <= 8:
                {
                    // A packed SysV bitfield window can be any width up to eight bytes.
                    ulong value = 0;
                    for (int index = 0; index < buffer.Length; index++)
                    {
                        int shift = littleEndian ? index * 8 : (buffer.Length - 1 - index) * 8;
                        value |= (ulong)buffer[index] << shift;
                    }

                    return value;
                }

            default:
                throw new InvalidOperationException("Unsupported integer size: " + buffer.Length);
        }
    }

    /// <summary>Reads a one-to-eight-byte unsigned bitfield storage unit at the stream position, memory-backed when possible.</summary>
    public static ulong ReadBitfieldUnit(Stream stream, int byteSize, bool littleEndian)
    {
        if (stream is ReadBudgetStream budget && budget.TryReadSpan(byteSize, out ReadOnlySpan<byte> direct))
        {
            return ReadUnsigned(direct, littleEndian);
        }

        Span<byte> buffer = stackalloc byte[8];
        Span<byte> slice = buffer[..byteSize];
        ReadExactlyOrThrow(stream, slice);
        return ReadUnsigned(slice, littleEndian);
    }

    /// <summary>Writes a two-byte UTF-16 code unit in the requested byte order.</summary>
    public static void WriteChar(Stream stream, char value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Writes a two-byte signed integer in the requested byte order.</summary>
    public static void WriteInt16(Stream stream, short value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Writes a two-byte unsigned integer in the requested byte order.</summary>
    public static void WriteUInt16(Stream stream, ushort value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Writes a four-byte signed integer in the requested byte order.</summary>
    public static void WriteInt32(Stream stream, int value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Writes a four-byte unsigned integer in the requested byte order.</summary>
    public static void WriteUInt32(Stream stream, uint value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Writes an eight-byte signed integer in the requested byte order.</summary>
    public static void WriteInt64(Stream stream, long value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Writes an eight-byte unsigned integer in the requested byte order.</summary>
    public static void WriteUInt64(Stream stream, ulong value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt64BigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Writes a four-byte IEEE 754 value in the requested byte order.</summary>
    public static void WriteSingle(Stream stream, float value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteSingleLittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteSingleBigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Writes an eight-byte IEEE 754 value in the requested byte order.</summary>
    public static void WriteDouble(Stream stream, double value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        if (isLittleEndian)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(buffer, value);
        }
        else
        {
            BinaryPrimitives.WriteDoubleBigEndian(buffer, value);
        }

        stream.Write(buffer);
    }

    /// <summary>Converts an unsigned value to one, two, four, or eight bytes in the layout's byte order.</summary>
    public static byte[] WriteUnsigned(ulong value, int byteSize, bool littleEndian)
    {
        byte[] bytes = new byte[byteSize];
        switch (byteSize)
        {
        case 1:
            bytes[0] = (byte)value;
            break;
        case 2:
            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(bytes, (ushort)value);
            }

            break;
        case 4:
            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(bytes, (uint)value);
            }

            break;
        case 8:
            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
            }

            break;
        case > 0 and <= 8:
            for (int index = 0; index < byteSize; index++)
            {
                int shift = littleEndian ? index * 8 : (byteSize - 1 - index) * 8;
                bytes[index] = (byte)(value >> shift);
            }

            break;
        default:
            throw new InvalidOperationException("Unsupported integer size: " + byteSize);
        }

        return bytes;
    }

    /// <summary>Reads exactly the requested number of bytes, translating a short read into a layout-specific error.</summary>
    internal static void ReadExactlyOrThrow(Stream stream, Span<byte> buffer)
    {
        long start = stream.CanSeek ? stream.Position : -1;
        try
        {
            // ReadExactly handles throttled and network-like streams that return fewer bytes per Read call.
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            if (start >= 0)
            {
                // ReadExactly leaves the position at the end; report what was available at the item's start.
                stream.Position = start;
                string message = DescribeShortRead(stream, buffer.Length);
                stream.Position = stream.Length;
                throw new CStructReadException(message, exception);
            }

            throw new CStructReadException(DescribeShortRead(stream, buffer.Length), exception);
        }
    }
}
