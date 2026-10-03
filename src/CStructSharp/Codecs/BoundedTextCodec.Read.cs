namespace CStructSharp.Codecs;

using System;
using System.Text;

/// <summary>Decodes already buffered text without copying valid input into a temporary byte array.</summary>
internal static partial class BoundedTextCodec
{
    /// <summary>Decodes the field's complete bytes while retaining the array decoder's exact failures.</summary>
    /// <param name="type">The bounded-text encoding spelling.</param>
    /// <param name="bytes">The borrowed field bytes; the returned string owns its characters.</param>
    /// <returns>The decoded text, including BOMs, embedded NULs and trailing padding.</returns>
    /// <exception cref="DecoderFallbackException">The bytes are invalid; the failure comes from the original array decoder.</exception>
    /// <exception cref="ArgumentException">The encoding spelling is unknown.</exception>
    internal static string Decode(string type, ReadOnlySpan<byte> bytes)
    {
        if (type == "cp437")
        {
            return Decode(type, bytes.ToArray());
        }

        try
        {
            return GetEncoding(type).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Encoding overloads can report different indices for malformed data. Keep the original overload's
            // exception, including its message, Index and BytesUnknown, while valid fields avoid the copy.
            return Decode(type, bytes.ToArray());
        }
    }
}
