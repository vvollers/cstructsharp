namespace CStructSharp;

using System;
using System.IO;

/// <summary>Distinct network UUID and Windows GUID storage conventions.</summary>
internal static class IdentifierCodec
{
    /// <summary>Reads all 128 bits without validating an identifier version or variant.</summary>
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

        return new Guid(bytes, bigEndian: networkOrder);
    }

    /// <summary>Accepts a managed Guid or canonical D-format text and writes exactly 16 bytes.</summary>
    public static void Write(Stream stream, object value, bool networkOrder)
    {
        Guid identifier;
        if (value is Guid typed)
        {
            identifier = typed;
        }
        else if (value is string text && Guid.TryParseExact(text, "D", out Guid parsed))
        {
            identifier = parsed;
        }
        else
        {
            throw new CStructWriteException("Identifier requires a Guid or canonical xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx text.");
        }

        Span<byte> bytes = stackalloc byte[16];
        identifier.TryWriteBytes(bytes, bigEndian: networkOrder, out _);
        stream.Write(bytes);
    }
}
