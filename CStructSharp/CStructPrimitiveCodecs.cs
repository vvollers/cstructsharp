namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CStructSharp.Structure;

/// <summary>Builds the primitive binary codec maps used by the CStruct facade.</summary>
public partial class CStruct
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

    private static readonly Encoding StrictAsciiEncoding = Encoding.GetEncoding(
        Encoding.ASCII.CodePage,
        EncoderFallback.ExceptionFallback,
        DecoderFallback.ExceptionFallback);

    private static readonly Encoding StrictUtf8Encoding = new UTF8Encoding(false, true);

    private static readonly Encoding StrictUtf16BigEndianEncoding = new UnicodeEncoding(true, false, true);

    private static readonly Encoding StrictUtf16LittleEndianEncoding = new UnicodeEncoding(false, false, true);

    private static readonly Dictionary<string, string> FieldTypeAliasses = new()
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
    };

    /// <summary>Returns whether a primitive handler consumes bytes until a terminator instead of having a fixed footprint.</summary>
    private static bool IsVariableLengthType(string typeName)
    {
        return typeName is "ascii_string_zero" or "ascii_string_newline" or "utf8_string_zero" or
               "utf8_string_newline" or "unicode_string_zero" or "unicode_string_zero>" or
               "unicode_string_zero<" or "unicode_string_newline" or "unicode_string_newline>" or
               "unicode_string_newline<" or "cstring" or "string" or "string>" or "string<";
    }

    /// <summary>Reads characters until a terminator and leaves the stream immediately after that terminator.</summary>
    private static string ReadIntoString(Stream stream, Encoding encoding, char terminator)
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

    /// <summary>Encodes a string and appends the layout's required terminator.</summary>
    private static void WriteTerminatedString(Stream stream, Encoding encoding, string value, char terminator)
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
    private static byte ConvertToNarrowCharacter(object value)
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

    /// <summary>Builds the named primitive readers and records each primitive's byte size for layout calculations.</summary>
    private void BuildFieldHandlers()
    {
        // First register only the canonical primitive spellings. Each delegate consumes exactly one encoded value.
        this.fieldHandlers.ReplaceWith(
            new Dictionary<string, Func<Stream, object>>
            {
                ["byte"] = stream => ReadByteExactly(stream),
                ["int8"] = stream => unchecked((sbyte)ReadByteExactly(stream)),
                ["uint8"] = stream => ReadByteExactly(stream),
                ["char"] = stream => (char)ReadByteExactly(stream),
                ["wchar>"] = stream => BitConverter.ToChar(ReadIntoBuffer(stream, 2, false)),
                ["wchar<"] = stream => BitConverter.ToChar(ReadIntoBuffer(stream, 2, true)),
                ["int16>"]
                                     = stream => (short)((ReadByteExactly(stream) << 8) | ReadByteExactly(stream)),
                ["int16<"]
                                     = stream => (short)(ReadByteExactly(stream) | (ReadByteExactly(stream) << 8)),
                ["uint16>"]
                                     = stream => (ushort)((ReadByteExactly(stream) << 8) | ReadByteExactly(stream)),
                ["uint16<"]
                                     = stream => (ushort)(ReadByteExactly(stream) | (ReadByteExactly(stream) << 8)),
                ["int32>"]
                                     = stream => (ReadByteExactly(stream) << 24) |
                                                 (ReadByteExactly(stream) << 16) |
                                                 (ReadByteExactly(stream) << 8) |
                                                 ReadByteExactly(stream),
                ["int32<"]
                                     = stream => ReadByteExactly(stream) |
                                                 (ReadByteExactly(stream) << 8) |
                                                 (ReadByteExactly(stream) << 16) |
                                                 (ReadByteExactly(stream) << 24),
                ["uint32>"]
                                     = stream => ((uint)ReadByteExactly(stream) << 24) |
                                                 ((uint)ReadByteExactly(stream) << 16) |
                                                 ((uint)ReadByteExactly(stream) << 8) |
                                                 ReadByteExactly(stream),
                ["uint32<"]
                                     = stream => ReadByteExactly(stream) |
                                                 ((uint)ReadByteExactly(stream) << 8) |
                                                 ((uint)ReadByteExactly(stream) << 16) |
                                                 ((uint)ReadByteExactly(stream) << 24),
                ["int64>"] = stream => BitConverter.ToInt64(ReadIntoBuffer(stream, 8, false)),
                ["int64<"] = stream => BitConverter.ToInt64(ReadIntoBuffer(stream, 8, true)),
                ["uint64>"] = stream => BitConverter.ToUInt64(ReadIntoBuffer(stream, 8, false)),
                ["uint64<"] = stream => BitConverter.ToUInt64(ReadIntoBuffer(stream, 8, true)),
                ["ascii_string_zero"] = stream => ReadIntoString(stream, StrictAsciiEncoding, '\0'),
                ["ascii_string_newline"] = stream => ReadIntoString(stream, StrictAsciiEncoding, '\n'),
                ["utf8_string_zero"] = stream => ReadIntoString(stream, StrictUtf8Encoding, '\0'),
                ["utf8_string_newline"] = stream => ReadIntoString(stream, StrictUtf8Encoding, '\n'),
                ["unicode_string_zero>"] = stream => ReadIntoString(stream, StrictUtf16BigEndianEncoding, '\0'),
                ["unicode_string_zero<"] = stream => ReadIntoString(stream, StrictUtf16LittleEndianEncoding, '\0'),
                ["unicode_string_newline>"] = stream => ReadIntoString(stream, StrictUtf16BigEndianEncoding, '\n'),
                ["unicode_string_newline<"] =
                    stream => ReadIntoString(stream, StrictUtf16LittleEndianEncoding, '\n'),
            });

        // Canonical numeric names have explicit `>` and `<` variants; collect one side to derive their neutral names.
        List<string> fieldTypesWithSpecificEndianness = this.FieldHandlers.Keys.Where(o => o.EndsWith('>')).ToList();

        // Add the unsuffixed names (for example, int32) using the byte order chosen for this CStruct instance.
        foreach (string alias in fieldTypesWithSpecificEndianness.Select(fieldType => fieldType[..^1]))
        {
            // Choose the instance's default endian reader once so field parsing stays a simple dictionary lookup.
            this.fieldHandlers[alias]
                = this.IsLittleEndian ? this.fieldHandlers[alias + '<'] : this.fieldHandlers[alias + '>'];
        }

        foreach (KeyValuePair<string, string> alias in FieldTypeAliasses)
        {
            // C-style spellings such as `int` and `long` are aliases, not separate codecs with different behavior.
            this.fieldHandlers[alias.Key] = this.fieldHandlers[alias.Value];
        }

        // Run every reader against enough zero bytes to measure its fixed width for alignment and sizing calculations.
        byte[] buffer = "\x00\n\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00".Select(o => (byte)o).ToArray();

        foreach (KeyValuePair<string, Func<Stream, object>> fieldHandler in this.fieldHandlers)
        {
            if (IsVariableLengthType(fieldHandler.Key))
            {
                // A terminated string has no fixed footprint. Keep its alignment at one without trying to read a
                // synthetic terminator, because the real reader now correctly treats an unterminated string as an error.
                this.fieldAlignments[fieldHandler.Key] = 1;
                continue;
            }

            var stream = new MemoryStream(buffer);
            fieldHandler.Value(stream);

            // Variable-length strings report zero consumed bytes here, which is normalized to alignment one.
            this.fieldAlignments[fieldHandler.Key] = (byte)Math.Max(1, stream.Position);
        }
    }

    /// <summary>Builds the named primitive writers using the same aliases and byte order as the readers.</summary>
    private void BuildWriteHandlers()
    {
        // Mirror the reader map with canonical writers so serialize and update use the exact same type vocabulary.
        this.writeHandlers.ReplaceWith(
            new Dictionary<string, Action<Stream, object>>
            {
                ["byte"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
                ["int8"]
                                     = (stream, value) => stream.WriteByte(unchecked((byte)Convert.ToSByte(value))),
                ["uint8"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
                ["char"] = (stream, value) => stream.WriteByte(ConvertToNarrowCharacter(value)),
                ["wchar>"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.GetBytes(Convert.ToChar(value)),
                                                                           false),
                ["wchar<"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.GetBytes(Convert.ToChar(value)),
                                                                           true),
                ["int16>"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.
                                                                               GetBytes(Convert.ToInt16(value)),
                                                                           false),
                ["int16<"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.
                                                                               GetBytes(Convert.ToInt16(value)),
                                                                           true),
                ["uint16>"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.GetBytes(
                                                                            Convert.ToUInt16(value)),
                                                                           false),
                ["uint16<"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.GetBytes(
                                                                            Convert.ToUInt16(value)),
                                                                           true),
                ["int32>"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.
                                                                               GetBytes(Convert.ToInt32(value)),
                                                                           false),
                ["int32<"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.
                                                                               GetBytes(Convert.ToInt32(value)),
                                                                           true),
                ["uint32>"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.GetBytes(
                                                                            Convert.ToUInt32(value)),
                                                                           false),
                ["uint32<"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.GetBytes(
                                                                            Convert.ToUInt32(value)),
                                                                           true),
                ["int64>"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.
                                                                               GetBytes(Convert.ToInt64(value)),
                                                                           false),
                ["int64<"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.
                                                                               GetBytes(Convert.ToInt64(value)),
                                                                           true),
                ["uint64>"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.GetBytes(
                                                                            Convert.ToUInt64(value)),
                                                                           false),
                ["uint64<"]
                                     = (stream, value) => WriteEndianBytes(
                                                                           stream,
                                                                           BitConverter.GetBytes(
                                                                            Convert.ToUInt64(value)),
                                                                           true),
                ["ascii_string_zero"]
                                     = (stream, value) => WriteTerminatedString(
                                                                                 stream,
                                                                                 StrictAsciiEncoding,
                                                                                 Convert.ToString(value) ?? string.Empty,
                                                                                 '\0'),
                ["ascii_string_newline"]
                                     = (stream, value) => WriteTerminatedString(
                                                                                 stream,
                                                                                 StrictAsciiEncoding,
                                                                                 Convert.ToString(value) ?? string.Empty,
                                                                                 '\n'),
                ["utf8_string_zero"]
                                     = (stream, value) => WriteTerminatedString(
                                                                                 stream,
                                                                                 StrictUtf8Encoding,
                                                                                 Convert.ToString(value) ?? string.Empty,
                                                                                 '\0'),
                ["utf8_string_newline"]
                                     = (stream, value) => WriteTerminatedString(
                                                                                 stream,
                                                                                 StrictUtf8Encoding,
                                                                                 Convert.ToString(value) ?? string.Empty,
                                                                                 '\n'),
                ["unicode_string_zero>"]
                                     = (stream, value) => WriteTerminatedString(
                                                                                 stream,
                                                                                 StrictUtf16BigEndianEncoding,
                                                                                 Convert.ToString(value) ?? string.Empty,
                                                                                 '\0'),
                ["unicode_string_zero<"]
                                     = (stream, value) => WriteTerminatedString(
                                                                                 stream,
                                                                                 StrictUtf16LittleEndianEncoding,
                                                                                 Convert.ToString(value) ?? string.Empty,
                                                                                 '\0'),
                ["unicode_string_newline>"]
                                     = (stream, value) => WriteTerminatedString(
                                                                                 stream,
                                                                                 StrictUtf16BigEndianEncoding,
                                                                                 Convert.ToString(value) ?? string.Empty,
                                                                                 '\n'),
                ["unicode_string_newline<"]
                                     = (stream, value) => WriteTerminatedString(
                                                                                 stream,
                                                                                 StrictUtf16LittleEndianEncoding,
                                                                                 Convert.ToString(value) ?? string.Empty,
                                                                                 '\n'),
            });

        // Build default-endian names after both explicit byte-order writers have been registered.
        List<string> fieldTypesWithSpecificEndianness = this.WriteHandlers.Keys.Where(o => o.EndsWith('>')).ToList();

        foreach (string alias in fieldTypesWithSpecificEndianness.Select(fieldType => fieldType[..^1]))
        {
            // The layout-level endianness selects the neutral writer once, avoiding a branch for every field value.
            this.writeHandlers[alias]
                = this.IsLittleEndian ? this.writeHandlers[alias + '<'] : this.writeHandlers[alias + '>'];
        }

        foreach (KeyValuePair<string, string> alias in FieldTypeAliasses)
        {
            // Reuse the canonical delegate for familiar C aliases and string shorthand names.
            this.writeHandlers[alias.Key] = this.writeHandlers[alias.Value];
        }
    }

    /// <summary>Selects strict UTF-16 in the explicit field order, or in the layout order for neutral <c>wchar</c>.</summary>
    private Encoding GetWideCharacterEncoding(Identifier type)
    {
        if (type.Equals(WcharBigEndianType))
        {
            return StrictUtf16BigEndianEncoding;
        }

        if (type.Equals(WcharLittleEndianType))
        {
            return StrictUtf16LittleEndianEncoding;
        }

        return this.IsLittleEndian ? StrictUtf16LittleEndianEncoding : StrictUtf16BigEndianEncoding;
    }
}
