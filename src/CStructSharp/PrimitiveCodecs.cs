namespace CStructSharp;

using System;
using System.Buffers;
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

    /// <summary>Returns whether a primitive handler consumes bytes until a terminator instead of having a fixed footprint.</summary>
    public static bool IsVariableLengthType(string typeName)
    {
        return typeName is "ascii_string_zero" or "ascii_string_newline" or "utf8_string_zero" or
               "utf8_string_newline" or "unicode_string_zero" or "unicode_string_zero>" or
               "unicode_string_zero<" or "unicode_string_newline" or "unicode_string_newline>" or
               "unicode_string_newline<" or "cstring" or "string" or "string>" or "string<";
    }

    /// <summary>Reads exactly the declared encoded byte extent, including embedded NULs, without reading ahead.</summary>
    public static string ReadBoundedText(Stream stream, int byteCount, string type)
    {
        if (stream is ReadBudgetStream budget && byteCount > budget.MaxStringBytes)
        {
            throw new CStructReadLimitException("Encoded text buffer exceeds the configured string byte limit.");
        }

        byte[] bytes = new byte[byteCount];
        try
        {
            stream.ReadExactly(bytes);
            return BoundedTextCodec.Decode(type, bytes);
        }
        catch (EndOfStreamException exception)
        {
            throw new CStructReadException("Not enough bytes for the declared Encoded text buffer.", exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new CStructReadException("Encoded text buffer contains an invalid byte sequence.", exception);
        }
    }

    /// <summary>Reads characters until a terminator and leaves the stream immediately after that terminator.</summary>
    public static string ReadIntoString(Stream stream, Encoding encoding, char terminator)
    {
        // Chunked reads, one decode per chunk prefix (E2.9). The observable contract of the former byte-by-byte
        // reader is preserved exactly: the stream ends immediately after the terminator, an over-budget read leaves
        // the stream one byte past the limit, a decode failure leaves it at the end of the chunk being decoded, and
        // invalid sequences that straddle chunks still fail because the decoder keeps its state across chunks.
        Decoder decoder = encoding.GetDecoder();
        int unitSize = encoding is UnicodeEncoding ? 2 : 1;
        Span<byte> terminatorBytes = stackalloc byte[4];
        int terminatorLength = encoding.GetBytes(new ReadOnlySpan<char>(in terminator), terminatorBytes);
        terminatorBytes = terminatorBytes[..terminatorLength];

        byte[] chunk = ArrayPool<byte>.Shared.Rent(TerminatedStringReadChunkSize);
        char[] decoded = ArrayPool<char>.Shared.Rent(TerminatedStringReadChunkSize + 2);
        try
        {
            StringBuilder? builder = null;
            long encodedByteCount = 0;
            long? maxStringBytes = stream is ReadBudgetStream budget ? budget.MaxStringBytes : null;

            while (true)
            {
                int bytesRead = stream.Read(chunk, 0, TerminatedStringReadChunkSize);
                if (bytesRead == 0)
                {
                    throw new CStructReadException("Not enough bytes in stream.");
                }

                // Search from the first position that starts an encoding unit relative to the string's own start.
                int alignmentOffset = (int)((unitSize - (encodedByteCount % unitSize)) % unitSize);
                int terminatorIndex = FindTerminator(chunk.AsSpan(0, bytesRead), terminatorBytes, unitSize, alignmentOffset);

                // Budget arithmetic equivalent to counting every consumed byte, including the terminator's own bytes.
                if (maxStringBytes.HasValue)
                {
                    long allowed = maxStringBytes.Value - encodedByteCount;
                    long consumedIfFound = terminatorIndex < 0 ? bytesRead : terminatorIndex + terminatorLength;
                    if (consumedIfFound > allowed)
                    {
                        // The byte-by-byte reader consumed the over-budget byte before checking, so the stream is left
                        // exactly one byte past the limit; only the never-inspected remainder is seeked back.
                        SeekBackUnconsumedChunkBytes(stream, bytesRead, (int)Math.Min(bytesRead, allowed + 1));
                        throw new CStructReadLimitException("String field exceeded the configured encoded-byte limit.");
                    }
                }

                int prefixLength = terminatorIndex < 0 ? bytesRead : terminatorIndex;
                int charsUsed;
                try
                {
                    // Flushing at the terminator surfaces an incomplete multi-byte sequence right before it, which the
                    // byte-by-byte reader rejected when the terminator byte arrived.
                    decoder.Convert(chunk.AsSpan(0, prefixLength), decoded, terminatorIndex >= 0, out _, out charsUsed, out _);
                }
                catch (DecoderFallbackException exception)
                {
                    throw new CStructReadException(
                        "String field contains bytes that are invalid for its encoding.",
                        exception);
                }

                if (terminatorIndex < 0)
                {
                    encodedByteCount += bytesRead;
                    (builder ??= new StringBuilder()).Append(decoded, 0, charsUsed);
                    continue;
                }

                // Do not include the terminator in the public string value, and leave the stream immediately after it.
                SeekBackUnconsumedChunkBytes(stream, bytesRead, terminatorIndex + terminatorLength);
                if (builder is null)
                {
                    return new string(decoded, 0, charsUsed);
                }

                builder.Append(decoded, 0, charsUsed);
                return builder.ToString();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
            ArrayPool<char>.Shared.Return(decoded);
        }
    }

    /// <summary>Finds the encoded terminator on an encoding-unit boundary, or -1.</summary>
    private static int FindTerminator(ReadOnlySpan<byte> data, ReadOnlySpan<byte> terminator, int unitSize, int alignmentOffset)
    {
        if (unitSize == 1)
        {
            return data.IndexOf(terminator[0]);
        }

        for (int index = alignmentOffset; index + terminator.Length <= data.Length; index += unitSize)
        {
            if (data.Slice(index, terminator.Length).SequenceEqual(terminator))
            {
                return index;
            }
        }

        return -1;
    }

    public static void WriteTerminatedString(Stream stream, Encoding encoding, string value, char terminator)
    {
        if (value.Contains(terminator, StringComparison.Ordinal))
        {
            throw new CStructWriteException("String value contains its encoded terminator.");
        }

        // Encode into a pooled buffer instead of concatenating the terminator and allocating a fresh byte[] (E2.9).
        byte[]? payload = null;
        try
        {
            int valueBytes = encoding.GetByteCount(value);
            int terminatorBytes = encoding.GetByteCount(new ReadOnlySpan<char>(in terminator));
            long encodedByteCount = checked((long)valueBytes + terminatorBytes);
            if (stream is WriteBudgetStream budget)
            {
                budget.EnsureStringBytes(encodedByteCount);
            }

            payload = ArrayPool<byte>.Shared.Rent(valueBytes + terminatorBytes);
            int written = encoding.GetBytes(value, payload.AsSpan(0, valueBytes));
            written += encoding.GetBytes(new ReadOnlySpan<char>(in terminator), payload.AsSpan(written, terminatorBytes));
            stream.Write(payload, 0, written);
        }
        catch (EncoderFallbackException exception)
        {
            throw new CStructWriteException("String value contains characters that are invalid for its encoding.", exception);
        }
        finally
        {
            if (payload is not null)
            {
                ArrayPool<byte>.Shared.Return(payload);
            }
        }
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
