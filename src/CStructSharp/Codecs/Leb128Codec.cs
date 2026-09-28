namespace CStructSharp.Codecs;

using System;
using System.IO;
using CStructSharp.Generated;

/// <summary>Stream adapters over the shared LEB128 rule (<see cref="Leb128Decoder"/>, <see cref="Codec.WriteULeb128"/>, <see cref="Codec.WriteSLeb128"/>).</summary>
internal static class Leb128Codec
{
    private const int MaximumBytes = 10;

    /// <summary>Reads one LEB128 integer byte by byte, so the stream stops exactly after the terminating byte.</summary>
    /// <param name="stream">The source, positioned at the first encoded byte.</param>
    /// <param name="width">The declared integer width, in bits; encoded payload bits past it are rejected.</param>
    /// <param name="signed">Whether the value is SLEB128 and is sign-extended from its last payload bit.</param>
    /// <returns>The decoded bits; a signed value is sign-extended to 64 bits (reinterpret as <c>long</c>).</returns>
    /// <exception cref="Diagnostics.CStructReadException">
    ///     The stream ends before the terminating byte, or the value exceeds <paramref name="width"/>.
    /// </exception>
    public static ulong Read(Stream stream, int width, bool signed)
    {
        var decoder = new Leb128Decoder(width, signed);
        while (true)
        {
            if (decoder.Push(BinaryPrimitiveIO.ReadByteExactly(stream), out ulong value))
            {
                return value;
            }
        }
    }

    /// <summary>Writes a value as ULEB128: seven bits per byte, low bits first, at most 10 bytes.</summary>
    /// <param name="stream">The destination, written at its current position.</param>
    /// <param name="value">The unsigned integer to encode.</param>
    public static void WriteUnsigned(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[MaximumBytes];
        stream.Write(buffer[..Codec.WriteULeb128(buffer, value)]);
    }

    /// <summary>Writes a value as SLEB128: seven bits per byte, low bits first, at most 10 bytes.</summary>
    /// <param name="stream">The destination, written at its current position.</param>
    /// <param name="value">The signed integer to encode in two's complement.</param>
    public static void WriteSigned(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[MaximumBytes];
        stream.Write(buffer[..Codec.WriteSLeb128(buffer, value)]);
    }
}
