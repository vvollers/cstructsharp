namespace CStructSharp.Codecs;

using System;
using System.IO;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Stream adapters over the shared identifier rule (<see cref="Codec.ReadGuid"/>, <see cref="Codec.WriteGuid"/>, <see cref="Codec.ToGuid"/>).</summary>
internal static class IdentifierCodec
{
    public static Guid Read(Stream stream, bool networkOrder)
    {
        Span<byte> bytes = stackalloc byte[16];
        try
        {
            stream.ReadExactly(bytes);
        }
        catch (EndOfStreamException exception)
        {
            throw new CStructReadException("Not enough bytes for a 16-byte identifier.", exception);
        }

        return Codec.ReadGuid(bytes, networkOrder);
    }

    public static void Write(Stream stream, object value, bool networkOrder)
    {
        Span<byte> bytes = stackalloc byte[16];
        Codec.WriteGuid(bytes, Codec.ToGuid(value), networkOrder);
        stream.Write(bytes);
    }
}
