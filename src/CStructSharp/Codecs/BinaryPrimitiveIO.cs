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
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded value, 0 to 16,777,215.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static uint ReadUInt24(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[3];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt24(buffer, isLittleEndian);
    }

    /// <summary>Reads a three-byte integer and sign-extends bit 23.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The sign-extended value, -8,388,608 to 8,388,607.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static int ReadInt24(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[3];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt24(buffer, isLittleEndian);
    }

    /// <summary>Writes an unsigned three-byte integer after checking its range.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The value to encode; it must fit in 24 bits.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteUInt24(Stream stream, uint value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[3];
        Codec.WriteUInt24(buffer, value, isLittleEndian);
        stream.Write(buffer);
    }

    /// <summary>Writes a signed three-byte integer after checking its range.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The value to encode; it must fit in a signed 24-bit range.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteInt24(Stream stream, int value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[3];
        Codec.WriteInt24(buffer, value, isLittleEndian);
        stream.Write(buffer);
    }

    /// <summary>Reads an unsigned six-byte integer in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded value, 0 to 2^48 - 1.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static ulong ReadUInt48(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[6];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt48(buffer, isLittleEndian);
    }

    /// <summary>Reads a six-byte integer and sign-extends bit 47.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The sign-extended value, -2^47 to 2^47 - 1.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static long ReadInt48(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[6];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt48(buffer, isLittleEndian);
    }

    /// <summary>Writes an unsigned six-byte integer after checking its range.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The value to encode; it must fit in 48 bits.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteUInt48(Stream stream, ulong value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[6];
        Codec.WriteUInt48(buffer, value, isLittleEndian);
        stream.Write(buffer);
    }

    /// <summary>Writes a signed six-byte integer after checking its range.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The value to encode; it must fit in a signed 48-bit range.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteInt48(Stream stream, long value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[6];
        Codec.WriteInt48(buffer, value, isLittleEndian);
        stream.Write(buffer);
    }

    /// <summary>Reads a sixteen-byte signed integer in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded signed 128-bit value.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static Int128 ReadInt128(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt128(buffer, isLittleEndian);
    }

    /// <summary>Reads a sixteen-byte unsigned integer in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded unsigned 128-bit value.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static UInt128 ReadUInt128(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt128(buffer, isLittleEndian);
    }

    /// <summary>Writes a sixteen-byte signed integer in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The signed 128-bit value to encode.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteInt128(Stream stream, Int128 value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        Codec.WriteInt128(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a sixteen-byte unsigned integer in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The unsigned 128-bit value to encode.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteUInt128(Stream stream, UInt128 value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[16];
        Codec.WriteUInt128(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Reads an IEEE-754 binary16 value bit for bit.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The half-precision value with the stored bit pattern, including NaN payloads.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static Half ReadHalf(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadHalf(buffer, isLittleEndian);
    }

    /// <summary>Writes an IEEE-754 binary16 value bit for bit.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The half-precision value whose bit pattern is written.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteHalf(Stream stream, Half value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        Codec.WriteHalf(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Reads one required byte and turns an unexpected end of stream into a layout-specific error.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <returns>The byte that was read.</returns>
    /// <exception cref="CStructReadException">The stream is already at its end.</exception>
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
    /// <param name="stream">The stream that ran short; its position is where the item started.</param>
    /// <param name="needed">The number of bytes the item required.</param>
    /// <returns>The error message, including the remaining byte count when the stream can seek.</returns>
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
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The UTF-16 code unit, which may be half of a surrogate pair.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static char ReadChar(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadChar(buffer, isLittleEndian);
    }

    /// <summary>Reads a two-byte signed integer in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded signed 16-bit value.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static short ReadInt16(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt16(buffer, isLittleEndian);
    }

    /// <summary>Reads a two-byte unsigned integer in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded unsigned 16-bit value.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static ushort ReadUInt16(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt16(buffer, isLittleEndian);
    }

    /// <summary>Reads a four-byte signed integer in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded signed 32-bit value.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static int ReadInt32(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt32(buffer, isLittleEndian);
    }

    /// <summary>Reads a four-byte unsigned integer in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded unsigned 32-bit value.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static uint ReadUInt32(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt32(buffer, isLittleEndian);
    }

    /// <summary>Reads an eight-byte signed integer in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded signed 64-bit value.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static long ReadInt64(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadInt64(buffer, isLittleEndian);
    }

    /// <summary>Reads an eight-byte unsigned integer in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded unsigned 64-bit value.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static ulong ReadUInt64(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadUInt64(buffer, isLittleEndian);
    }

    /// <summary>Reads a four-byte IEEE 754 value in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The single-precision value with the stored bit pattern.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static float ReadSingle(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadSingle(buffer, isLittleEndian);
    }

    /// <summary>Reads an eight-byte IEEE 754 value in the requested byte order.</summary>
    /// <param name="stream">The source, at the value's first byte; it advances past the bytes read.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The double-precision value with the stored bit pattern.</returns>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
    public static double ReadDouble(Stream stream, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        ReadExactlyOrThrow(stream, buffer);
        return Codec.ReadDouble(buffer, isLittleEndian);
    }

    /// <summary>Converts one to eight bytes into an unsigned number in the layout's byte order.</summary>
    /// <param name="buffer">The one to eight bytes of the value.</param>
    /// <param name="littleEndian">Whether the least significant byte comes first (little-endian).</param>
    /// <returns>The decoded value, zero-extended to 64 bits.</returns>
    public static ulong ReadUnsigned(ReadOnlySpan<byte> buffer, bool littleEndian)
        => Codec.ReadUnsigned(buffer, littleEndian);

    /// <summary>Writes a two-byte UTF-16 code unit in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The UTF-16 code unit to encode.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteChar(Stream stream, char value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        Codec.WriteUInt16(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a two-byte signed integer in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The signed 16-bit value to encode.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteInt16(Stream stream, short value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        Codec.WriteInt16(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a two-byte unsigned integer in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The unsigned 16-bit value to encode.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteUInt16(Stream stream, ushort value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[2];
        Codec.WriteUInt16(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a four-byte signed integer in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The signed 32-bit value to encode.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteInt32(Stream stream, int value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        Codec.WriteInt32(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a four-byte unsigned integer in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The unsigned 32-bit value to encode.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteUInt32(Stream stream, uint value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        Codec.WriteUInt32(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes an eight-byte signed integer in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The signed 64-bit value to encode.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteInt64(Stream stream, long value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        Codec.WriteInt64(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes an eight-byte unsigned integer in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The unsigned 64-bit value to encode.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteUInt64(Stream stream, ulong value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        Codec.WriteUInt64(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes a four-byte IEEE 754 value in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The single-precision value whose bit pattern is written.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteSingle(Stream stream, float value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[4];
        Codec.WriteSingle(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes an eight-byte IEEE 754 value in the requested byte order.</summary>
    /// <param name="stream">The destination, at the value's first byte; it advances past the bytes written.</param>
    /// <param name="value">The double-precision value whose bit pattern is written.</param>
    /// <param name="isLittleEndian">Whether the least significant byte comes first (little-endian).</param>
    public static void WriteDouble(Stream stream, double value, bool isLittleEndian)
    {
        Span<byte> buffer = stackalloc byte[8];
        Codec.WriteDouble(buffer, value, isLittleEndian);

        stream.Write(buffer);
    }

    /// <summary>Writes an unsigned value as one to eight bytes in the given byte order: a pointer, or a bitfield storage unit.</summary>
    /// <param name="stream">The destination, at the value's first byte.</param>
    /// <param name="value">The value.</param>
    /// <param name="byteSize">The width in bytes, 1 to 8.</param>
    /// <param name="littleEndian">Whether the bytes are little-endian.</param>
    /// <exception cref="InvalidOperationException">The width is not 1 to 8.</exception>
    public static void WriteUnsigned(Stream stream, ulong value, int byteSize, bool littleEndian)
    {
        Span<byte> bytes = stackalloc byte[8];
        Span<byte> unit = UnitOf(bytes, byteSize);
        Codec.WriteUnsigned(unit, value, littleEndian);
        stream.Write(unit);
    }

    /// <summary>The first <paramref name="byteSize"/> bytes of an eight-byte scratch buffer, for a value of one to eight bytes.</summary>
    /// <param name="scratch">An eight-byte buffer.</param>
    /// <param name="byteSize">The width in bytes.</param>
    /// <returns>The slice.</returns>
    /// <exception cref="InvalidOperationException">The width is not 1 to 8.</exception>
    internal static Span<byte> UnitOf(Span<byte> scratch, int byteSize)
        => byteSize is > 0 and <= 8 ? scratch[..byteSize] : throw new InvalidOperationException("Unsupported integer size: " + byteSize);

    /// <summary>Reads exactly the requested number of bytes, translating a short read into a layout-specific error.</summary>
    /// <param name="stream">The source stream; on a short read a seekable stream is left at its end.</param>
    /// <param name="buffer">The destination; its length is the number of bytes to read.</param>
    /// <exception cref="CStructReadException">The stream ends before all bytes of the value are read.</exception>
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
