namespace CStructSharp;

using System;
using System.Text;

/// <summary>Locale-independent codecs for byte-counted text buffers.</summary>
internal static class BoundedTextCodec
{
    // Unicode CP437 mapping: https://www.unicode.org/Public/MAPPINGS/VENDORS/MICSFT/PC/CP437.TXT
    private const string Cp437High = "ÇüéâäàåçêëèïîìÄÅÉæÆôöòûùÿÖÜ¢£¥₧ƒáíóúñÑªº¿⌐¬½¼¡«»░▒▓│┤╡╢╖╕╣║╗╝╜╛┐└┴┬├─┼╞╟╚╔╩╦╠═╬╧╨╤╥╙╘╒╓╫╪┘┌█▄▌▐▀αßΓπΣσµτΦΘΩδ∞φε∩≡±≥≤⌠⌡÷≈°∙·√ⁿ²■ ";
    private static readonly Encoding Latin1 = Encoding.GetEncoding(28591, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);

    /// <summary>Identifies byte-counted text primitive spellings.</summary>
    public static bool IsType(string type) => type is "utf8" or "latin1" or "cp437" or "utf16le" or "utf16be";

    /// <summary>Identifies encodings that require an even byte capacity.</summary>
    public static bool IsUtf16(string type) => type is "utf16le" or "utf16be";

    /// <summary>Decodes a complete field without consuming neighbouring bytes or stripping a BOM.</summary>
    public static string Decode(string type, byte[] bytes)
    {
        if (type != "cp437")
        {
            return GetEncoding(type).GetString(bytes);
        }

        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
        {
            chars[i] = bytes[i] < 128 ? (char)bytes[i] : Cp437High[bytes[i] - 128];
        }

        return new string(chars);
    }

    /// <summary>Validates the writer domain and calculates capacity before allocation.</summary>
    public static int GetByteCount(string type, string value)
    {
        if (type != "cp437")
        {
            return GetEncoding(type).GetByteCount(value);
        }

        foreach (char character in value)
        {
            _ = EncodeCp437(character);
        }

        return value.Length;
    }

    /// <summary>Encodes strict text without inserting a BOM or terminator.</summary>
    public static byte[] Encode(string type, string value)
    {
        if (type != "cp437")
        {
            return GetEncoding(type).GetBytes(value);
        }

        var bytes = new byte[value.Length];
        for (int i = 0; i < value.Length; i++)
        {
            bytes[i] = EncodeCp437(value[i]);
        }

        return bytes;
    }

    private static byte EncodeCp437(char character)
    {
        if (character < 128)
        {
            return (byte)character;
        }

        int index = Cp437High.IndexOf(character);
        return index >= 0 ? (byte)(index + 128) : throw new EncoderFallbackException("Character is not representable in CP437.");
    }

    private static Encoding GetEncoding(string type) => type switch
    {
        "utf8" => PrimitiveCodecs.StrictUtf8Encoding,
        "latin1" => Latin1,
        "utf16le" => PrimitiveCodecs.StrictUtf16LittleEndianEncoding,
        "utf16be" => PrimitiveCodecs.StrictUtf16BigEndianEncoding,
        _ => throw new ArgumentException("Unknown bounded text encoding.", nameof(type)),
    };
}
