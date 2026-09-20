namespace CStructSharp.Codecs;

using System;
using System.IO;
using CStructSharp.Generated;

/// <summary>Stream adapters over the shared LEB128 rule (<see cref="Leb128Decoder"/>, <see cref="Codec.WriteULeb128"/>, <see cref="Codec.WriteSLeb128"/>).</summary>
internal static class Leb128Codec
{
    private const int MaximumBytes = 10;

    public static bool IsType(string name) => name is "uleb128_32" or "uleb128_64" or "sleb128_32" or "sleb128_64";

    /// <summary>Reads one LEB128 integer byte by byte, so the stream stops exactly after the terminating byte.</summary>
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

    public static void WriteUnsigned(Stream stream, ulong value)
    {
        Span<byte> buffer = stackalloc byte[MaximumBytes];
        stream.Write(buffer[..Codec.WriteULeb128(buffer, value)]);
    }

    public static void WriteSigned(Stream stream, long value)
    {
        Span<byte> buffer = stackalloc byte[MaximumBytes];
        stream.Write(buffer[..Codec.WriteSLeb128(buffer, value)]);
    }
}
