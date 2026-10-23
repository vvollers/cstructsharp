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
    internal static string Decode(string type, ReadOnlySpan<byte> bytes) => Decode(type, bytes, lenient: false);

    /// <summary>Decodes the field's complete bytes, reading invalid sequences as U+FFFD instead of failing when <paramref name="lenient"/>.</summary>
    /// <param name="type">The bounded-text encoding spelling.</param>
    /// <param name="bytes">The borrowed field bytes; the returned string owns its characters.</param>
    /// <param name="lenient">Whether invalid sequences become U+FFFD.</param>
    /// <returns>The decoded text, including BOMs, embedded NULs and trailing padding.</returns>
    /// <exception cref="DecoderFallbackException">The bytes are invalid and <paramref name="lenient"/> is false.</exception>
    /// <exception cref="ArgumentException">The encoding spelling is unknown.</exception>
    internal static string Decode(string type, ReadOnlySpan<byte> bytes, bool lenient)
    {
        if (type == "cp437")
        {
            return Decode(type, bytes.ToArray(), lenient);
        }

        try
        {
            return GetEncoding(type, lenient).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Encoding overloads can report different indices for malformed data. Keep the original overload's
            // exception, including its message, Index and BytesUnknown, while valid fields avoid the copy.
            return Decode(type, bytes.ToArray(), lenient);
        }
    }
}
