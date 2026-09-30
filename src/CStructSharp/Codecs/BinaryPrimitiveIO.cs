namespace CStructSharp.Codecs;

using System;
using System.IO;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Streams;

/// <summary>Reads and writes fixed-width unsigned integers in a caller-chosen byte order.</summary>
internal static class BinaryPrimitiveIO
{
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
