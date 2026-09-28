namespace CStructSharp.Codecs;

using System;
using System.IO;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Stream adapters over the shared identifier rule (<see cref="Codec.ReadGuid"/>, <see cref="Codec.WriteGuid"/>, <see cref="Codec.ToGuid"/>).</summary>
internal static class IdentifierCodec
{
    /// <summary>Reads one 16-byte identifier from the stream's current position.</summary>
    /// <param name="stream">The source stream; it advances by 16 bytes.</param>
    /// <param name="networkOrder">True for a <c>uuid</c> (big-endian); false for a <c>guid</c> (Windows order).</param>
    /// <returns>The decoded identifier.</returns>
    /// <exception cref="CStructReadException">Fewer than 16 bytes remain.</exception>
    public static Guid Read(Stream stream, bool networkOrder)
    {
        Span<byte> bytes = stackalloc byte[16];
        try
        {
            stream.ReadExactly(bytes);
        }
        catch (EndOfStreamException exception)
        {
            throw new CStructReadException(ReadFailures.IdentifierShortRead, exception);
        }

        return Codec.ReadGuid(bytes, networkOrder);
    }

    /// <summary>Writes one 16-byte identifier at the stream's current position.</summary>
    /// <param name="stream">The destination stream; it advances by 16 bytes.</param>
    /// <param name="value">A <see cref="Guid"/> or its canonical <c>D</c> text.</param>
    /// <param name="networkOrder">True for a <c>uuid</c> (big-endian); false for a <c>guid</c> (Windows order).</param>
    /// <exception cref="CStructWriteException"><paramref name="value"/> is not a GUID or GUID text.</exception>
    public static void Write(Stream stream, object value, bool networkOrder)
    {
        Span<byte> bytes = stackalloc byte[16];
        Codec.WriteGuid(bytes, Codec.ToGuid(value), networkOrder);
        stream.Write(bytes);
    }
}
