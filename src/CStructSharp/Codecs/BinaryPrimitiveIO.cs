namespace CStructSharp.Codecs;

using System;
using System.IO;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Streams;

/// <summary>Reads and writes fixed-width unsigned integers in a caller-chosen byte order.</summary>
internal static class BinaryPrimitiveIO
{
    /// <summary>Reads an unsigned three-byte integer.</summary>
    public static uint ReadUInt24(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[3];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt24(buffer, isLittleEndian);
    }

    /// <summary>Reads a three-byte integer and sign-extends bit 23.</summary>
    public static int ReadInt24(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[3];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt24(buffer, isLittleEndian);
    }

    /// <summary>Writes an unsigned three-byte integer after checking its range.</summary>
    public static void WriteUInt24(Stream stream, uint value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[3];
        Codec.WriteUInt24(buffer, value, isLittleEndian);
        stream.Write(buffer);
    }

    /// <summary>Writes a signed three-byte integer after checking its range.</summary>
    public static void WriteInt24(Stream stream, int value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[3];
        Codec.WriteInt24(buffer, value, isLittleEndian);
        stream.Write(buffer);
    }

    /// <summary>Reads an unsigned six-byte integer in the requested byte order.</summary>
    public static ulong ReadUInt48(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[6];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt48(buffer, isLittleEndian);
    }

    /// <summary>Reads a six-byte integer and sign-extends bit 47.</summary>
    public static long ReadInt48(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[6];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt48(buffer, isLittleEndian);
    }

    /// <summary>Writes an unsigned six-byte integer after checking its range.</summary>
    public static void WriteUInt48(Stream stream, ulong value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[6];
        Codec.WriteUInt48(buffer, value, isLittleEndian);
        stream.Write(buffer);
    }

    /// <summary>Writes a signed six-byte integer after checking its range.</summary>
    public static void WriteInt48(Stream stream, long value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[6];
        Codec.WriteInt48(buffer, value, isLittleEndian);
        stream.Write(buffer);
    }

    /// <summary>Reads a sixteen-byte signed integer in the requested byte order.</summary>
    public static Int128 ReadInt128(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt128(buffer, isLittleEndian);
    }

    /// <summary>Reads a sixteen-byte unsigned integer in the requested byte order.</summary>
    public static UInt128 ReadUInt128(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt128(buffer, isLittleEndian);
    }

    /// <summary>Writes a sixteen-byte signed integer in the requested byte order.</summary>
    public static void WriteInt128(Stream stream, Int128 value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        Codec.WriteInt128(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a sixteen-byte unsigned integer in the requested byte order.</summary>
    public static void WriteUInt128(Stream stream, UInt128 value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        Codec.WriteUInt128(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Reads an IEEE-754 binary16 value bit for bit.</summary>
    public static Half ReadHalf(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadHalf(buffer, isLittleEndian);
    }

    /// <summary>Writes an IEEE-754 binary16 value bit for bit.</summary>
    public static void WriteHalf(Stream stream, Half value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        Codec.WriteHalf(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Reads one required byte and turns an unexpected end of stream into a layout-specific error.</summary>
    public static byte ReadByteExactly(Stream stream)
    {
        int value = stream.ReadByte();
        if (value < 0)
        {
            throw new CStructReadException(ReadFailures.ShortRead(1, 0));
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
                   ? ReadFailures.ShortRead(needed, remaining)
                   : ReadFailures.ShortRead(needed);
    }

    /// <summary>Reads a two-byte UTF-16 code unit in the requested byte order.</summary>
    public static char ReadChar(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadChar(buffer, isLittleEndian);
    }

    /// <summary>Reads a two-byte signed integer in the requested byte order.</summary>
    public static short ReadInt16(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt16(buffer, isLittleEndian);
    }

    /// <summary>Reads a two-byte unsigned integer in the requested byte order.</summary>
    public static ushort ReadUInt16(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt16(buffer, isLittleEndian);
    }

    /// <summary>Reads a four-byte signed integer in the requested byte order.</summary>
    public static int ReadInt32(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt32(buffer, isLittleEndian);
    }

    /// <summary>Reads a four-byte unsigned integer in the requested byte order.</summary>
    public static uint ReadUInt32(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt32(buffer, isLittleEndian);
    }

    /// <summary>Reads an eight-byte signed integer in the requested byte order.</summary>
    public static long ReadInt64(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt64(buffer, isLittleEndian);
    }

    /// <summary>Reads an eight-byte unsigned integer in the requested byte order.</summary>
    public static ulong ReadUInt64(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt64(buffer, isLittleEndian);
    }

    /// <summary>Reads a four-byte IEEE 754 value in the requested byte order.</summary>
    public static float ReadSingle(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadSingle(buffer, isLittleEndian);
    }

    /// <summary>Reads an eight-byte IEEE 754 value in the requested byte order.</summary>
    public static double ReadDouble(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadDouble(buffer, isLittleEndian);
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
        return Codec.ReadUnsigned(slice, isLittleEndian);
    }

    /// <summary>Converts one to eight bytes into an unsigned number in the layout's byte order.</summary>
    public static ulong ReadUnsigned(ReadOnlySpan<byte> buffer, bool littleEndian)
        => Codec.ReadUnsigned(buffer, littleEndian);

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
        Codec.WriteUInt16(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a two-byte signed integer in the requested byte order.</summary>
    public static void WriteInt16(Stream stream, short value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        Codec.WriteInt16(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a two-byte unsigned integer in the requested byte order.</summary>
    public static void WriteUInt16(Stream stream, ushort value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        Codec.WriteUInt16(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a four-byte signed integer in the requested byte order.</summary>
    public static void WriteInt32(Stream stream, int value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        Codec.WriteInt32(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a four-byte unsigned integer in the requested byte order.</summary>
    public static void WriteUInt32(Stream stream, uint value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        Codec.WriteUInt32(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes an eight-byte signed integer in the requested byte order.</summary>
    public static void WriteInt64(Stream stream, long value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        Codec.WriteInt64(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes an eight-byte unsigned integer in the requested byte order.</summary>
    public static void WriteUInt64(Stream stream, ulong value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        Codec.WriteUInt64(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a four-byte IEEE 754 value in the requested byte order.</summary>
    public static void WriteSingle(Stream stream, float value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        Codec.WriteSingle(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes an eight-byte IEEE 754 value in the requested byte order.</summary>
    public static void WriteDouble(Stream stream, double value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        Codec.WriteDouble(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Converts an unsigned value to one, two, four, or eight bytes in the layout's byte order.</summary>
    public static byte[] WriteUnsigned(ulong value, int byteSize, bool littleEndian)
    {
        byte[] bytes = new byte[byteSize];
        Codec.WriteUnsigned(bytes, value, littleEndian);
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
