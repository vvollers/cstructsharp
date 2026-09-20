namespace CStructSharp.Generated;

/// <summary>The encodings a terminated string field can have.</summary>
public enum TerminatedTextEncoding
{
    /// <summary><c>ascii_string_zero</c>, <c>ascii_string_newline</c>, <c>cstring</c>: strict 7-bit ASCII.</summary>
    Ascii,

    /// <summary><c>utf8_string_zero</c>, <c>utf8_string_newline</c>: strict UTF-8.</summary>
    Utf8,

    /// <summary><c>unicode_string_zero&lt;</c>, <c>string&lt;</c>: UTF-16 little-endian.</summary>
    Utf16LittleEndian,

    /// <summary><c>unicode_string_zero&gt;</c>, <c>string&gt;</c>: UTF-16 big-endian.</summary>
    Utf16BigEndian,
}
