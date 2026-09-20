namespace CStructSharp.Codecs;

using System.Text;

/// <summary>
///     The compile-time half of the primitive codec vocabulary: the strict text encodings the text codecs decode
///     with and the variable-length name predicate; the stream readers and writers are the runtime half.
/// </summary>
internal static partial class PrimitiveCodecs
{
    public static readonly Encoding StrictAsciiEncoding = Encoding.GetEncoding(
        Encoding.ASCII.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    public static readonly Encoding StrictUtf8Encoding = new UTF8Encoding(false, true);

    public static readonly Encoding StrictUtf16BigEndianEncoding = new UnicodeEncoding(true, false, true);

    public static readonly Encoding StrictUtf16LittleEndianEncoding = new UnicodeEncoding(false, false, true);

    /// <summary>Returns whether a primitive handler consumes bytes until a terminator instead of having a fixed footprint.</summary>
    public static bool IsVariableLengthType(string typeName)
    {
        return typeName is "ascii_string_zero" or "ascii_string_newline" or "utf8_string_zero" or
               "utf8_string_newline" or "unicode_string_zero" or "unicode_string_zero>" or
               "unicode_string_zero<" or "unicode_string_newline" or "unicode_string_newline>" or
               "unicode_string_newline<" or "cstring" or "string" or "string>" or "string<";
    }
}
