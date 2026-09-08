namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
///     Provides the strict text encodings, terminated-string read/write logic, and other primitive-type-naming
///     support shared by the primitive reader and writer maps that <see cref="CStruct"/> builds for itself.
/// </summary>
internal static class PrimitiveCodecs
{
    /// <summary>
    ///     The number of bytes requested per underlying <see cref="Stream.Read(byte[],int,int)"/> call while
    ///     scanning for a string terminator. Decoding still happens one byte at a time so behavior is unchanged;
    ///     this only amortizes the I/O call cost for a stream (such as a raw
    ///     <see cref="System.Net.Sockets.NetworkStream"/> or another unbuffered custom stream) that does not
    ///     already buffer internally. Any bytes read past the terminator, or past the point where the encoded-byte
    ///     budget is exceeded, are seeked back before returning or throwing so the caller-visible stream position
    ///     exactly matches reading one byte at a time.
    /// </summary>
    private const int TerminatedStringReadChunkSize = 256;

    public static readonly Encoding StrictAsciiEncoding = Encoding.GetEncoding(
        Encoding.ASCII.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    public static readonly Encoding StrictUtf8Encoding = new UTF8Encoding(false, true);

    public static readonly Encoding StrictUtf16BigEndianEncoding = new UnicodeEncoding(true, false, true);

    public static readonly Encoding StrictUtf16LittleEndianEncoding = new UnicodeEncoding(false, false, true);

    public static readonly IReadOnlyDictionary<string, string> FieldTypeAliasses = new Dictionary<string, string>
    {
        ["short"] = "int16",
        ["ushort"] = "uint16",
        ["int"] = "int32",
        ["uint"] = "uint32",
        ["long"] = "int64",
        ["ulong"] = "uint64",
        ["string"] = "unicode_string_zero",
        ["string>"] = "unicode_string_zero>",
        ["string<"] = "unicode_string_zero<",
        ["cstring"] = "ascii_string_zero",

        // Wider C integer spellings (LANG-03a). Portable's long/ulong are always 64-bit regardless of host data
        // model (see differences-from-c.md), so every "long"-containing multi-word spelling below is consistently
        // 64-bit rather than inferred from any native ABI.
        ["signed"] = "int32",
        ["unsigned"] = "uint32",
        ["signed int"] = "int32",
        ["unsigned int"] = "uint32",
        ["signed short"] = "int16",
        ["unsigned short"] = "uint16",
        ["signed long"] = "int64",
        ["unsigned long"] = "uint64",
        ["long long"] = "int64",
        ["signed long long"] = "int64",
        ["unsigned long long"] = "uint64",
        ["signed char"] = "int8",
        ["unsigned char"] = "uint8",
        ["int8_t"] = "int8",
        ["uint8_t"] = "uint8",
        ["int16_t"] = "int16",
        ["uint16_t"] = "uint16",
        ["int32_t"] = "int32",
        ["uint32_t"] = "uint32",
        ["int64_t"] = "int64",
        ["uint64_t"] = "uint64",
    };

    /// <summary>Returns whether a primitive handler consumes bytes until a terminator instead of having a fixed footprint.</summary>
    public static bool IsVariableLengthType(string typeName)
    {
        return typeName is "ascii_string_zero" or "ascii_string_newline" or "utf8_string_zero" or
               "utf8_string_newline" or "unicode_string_zero" or "unicode_string_zero>" or
               "unicode_string_zero<" or "unicode_string_newline" or "unicode_string_newline>" or
               "unicode_string_newline<" or "cstring" or "string" or "string>" or "string<";
    }

    /// <summary>Reads characters until a terminator and leaves the stream immediately after that terminator.</summary>
    public static string ReadIntoString(Stream stream, Encoding encoding, char terminator)
    {
        // Decode one byte at a time instead of using StreamReader: StreamReader may read ahead, which makes byte
        // budgets and exact binary stream positions impossible to enforce reliably. I/O is still requested in
        // chunks - only the decode granularity, budget accounting, and terminator search stay byte-by-byte - and
        // any bytes read but not yet decoded are seeked back before returning or throwing so the exact-position and
        // budget contract observed by a caller is identical to a strictly byte-by-byte reader.
        StringBuilder builder = new();
        Decoder decoder = encoding.GetDecoder();
        byte[] chunk = new byte[TerminatedStringReadChunkSize];
        char[] output = new char[2];
        long encodedByteCount = 0;
        long? maxStringBytes = stream is ReadBudgetStream budget ? budget.MaxStringBytes : null;

        while (true)
        {
            int bytesRead = stream.Read(chunk, 0, chunk.Length);
            if (bytesRead == 0)
            {
                throw new CStructReadException("Not enough bytes in stream.");
            }

            for (int i = 0; i < bytesRead; i++)
            {
                encodedByteCount++;
                if (maxStringBytes.HasValue && encodedByteCount > maxStringBytes.Value)
                {
                    // A byte-by-byte reader would already have physically consumed this over-budget byte (it reads
                    // the byte, then checks the budget), leaving the stream one byte past the limit rather than
                    // exactly at it. Only seek back the remainder of the chunk that was never inspected at all, so
                    // the throw-time position matches that exactly.
                    SeekBackUnconsumedChunkBytes(stream, bytesRead, i + 1);
                    throw new CStructReadLimitException("String field exceeded the configured encoded-byte limit.");
                }

                int charsUsed;
                try
                {
                    decoder.Convert(chunk, i, 1, output, 0, output.Length, false, out _, out charsUsed, out _);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new CStructReadException(
                        "String field contains bytes that are invalid for its encoding.",
                        exception);
                }

                for (int c = 0; c < charsUsed; c++)
                {
                    char character = output[c];
                    if (character == terminator)
                    {
                        // Do not include the terminator in the public string value, and leave the stream
                        // immediately after it, exactly as a byte-by-byte reader would.
                        SeekBackUnconsumedChunkBytes(stream, bytesRead, i + 1);
                        return builder.ToString();
                    }

                    builder.Append(character);
                }
            }
        }
    }

    /// <summary>Encodes a string and appends the layout's required terminator.</summary>
    public static void WriteTerminatedString(Stream stream, Encoding encoding, string value, char terminator)
    {
        if (value.Contains(terminator, StringComparison.Ordinal))
        {
            throw new CStructWriteException("String value contains its encoded terminator.");
        }

        byte[] payload;
        try
        {
            long encodedByteCount = checked(
                (long)encoding.GetByteCount(value) +
                encoding.GetByteCount(new[] { terminator, }));
            if (stream is WriteBudgetStream budget)
            {
                budget.EnsureStringBytes(encodedByteCount);
            }

            payload = encoding.GetBytes(value + terminator);
        }
        catch (EncoderFallbackException exception)
        {
            throw new CStructWriteException("String value contains characters that are invalid for its encoding.", exception);
        }

        stream.Write(payload, 0, payload.Length);
    }

    /// <summary>Converts one CLR character to the raw one-byte domain used by the layout's <c>char</c> type.</summary>
    public static byte ConvertToNarrowCharacter(object value)
    {
        char character = Convert.ToChar(value);
        if (character > byte.MaxValue)
        {
            throw new CStructWriteException(
                "Character value U+" + ((int)character).ToString("X4", System.Globalization.CultureInfo.InvariantCulture) +
                " does not fit the one-byte char type.");
        }

        return (byte)character;
    }

    /// <summary>
    ///     Seeks a stream back by the tail of the most recent chunk read that was not actually consumed, so a
    ///     chunked read leaves the stream at the same position a byte-by-byte reader would have stopped at.
    /// </summary>
    /// <param name="stream">The stream positioned immediately after the chunk read supplying <paramref name="bytesRead"/>.</param>
    /// <param name="bytesRead">The number of bytes the most recent chunk read actually returned.</param>
    /// <param name="consumedCount">The number of leading bytes of that chunk that were actually decoded or counted.</param>
    private static void SeekBackUnconsumedChunkBytes(Stream stream, int bytesRead, int consumedCount)
    {
        int unconsumed = bytesRead - consumedCount;
        if (unconsumed > 0)
        {
            stream.Position -= unconsumed;
        }
    }
}
