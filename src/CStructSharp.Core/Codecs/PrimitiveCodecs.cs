namespace CStructSharp.Codecs;

using System.Text;

/// <summary>
///     The compile-time half of the primitive codec vocabulary: the strict text encodings the text codecs decode
///     with and the variable-length name predicate. The runtime half is the engine's own decoding and the codec
///     writer delegates of the runtime's codec table.
/// </summary>
internal static partial class PrimitiveCodecs
{
    /// <summary>ASCII that throws on bytes or characters outside 0 to 127 instead of substituting <c>?</c>.</summary>
    public static readonly Encoding StrictAsciiEncoding = Encoding.GetEncoding(
        Encoding.ASCII.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    /// <summary>UTF-8 without a byte order mark that throws on invalid byte sequences or unpaired surrogates.</summary>
    public static readonly Encoding StrictUtf8Encoding = new UTF8Encoding(false, true);

    /// <summary>Big-endian UTF-16 without a byte order mark that throws on invalid code units.</summary>
    public static readonly Encoding StrictUtf16BigEndianEncoding = new UnicodeEncoding(true, false, true);

    /// <summary>Little-endian UTF-16 without a byte order mark that throws on invalid code units.</summary>
    public static readonly Encoding StrictUtf16LittleEndianEncoding = new UnicodeEncoding(false, false, true);

    /// <summary>Returns whether a primitive handler consumes bytes until a terminator instead of having a fixed footprint.</summary>
    /// <param name="typeName">The resolved primitive type name, such as <c>cstring</c> or <c>uint32</c>.</param>
    /// <returns><see langword="true"/> for a zero- or newline-terminated string codec.</returns>
    public static bool IsVariableLengthType(string typeName)
    {
        return typeName is "ascii_string_zero" or "ascii_string_newline" or "utf8_string_zero" or
               "utf8_string_newline" or "unicode_string_zero" or "unicode_string_zero>" or
               "unicode_string_zero<" or "unicode_string_newline" or "unicode_string_newline>" or
               "unicode_string_newline<" or "cstring" or "string" or "string>" or "string<";
    }
}
