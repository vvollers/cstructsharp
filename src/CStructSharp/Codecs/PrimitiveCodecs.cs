namespace CStructSharp.Codecs;

using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Streams;

/// <summary>
///     Provides the strict text encodings, terminated-string read/write logic, and other primitive-type-naming
///     support shared by the primitive reader and writer maps that <see cref="CStruct"/> builds for itself.
/// </summary>
internal static partial class PrimitiveCodecs
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

    /// <summary>Checks that decoded wide characters form valid UTF-16 (no unpaired surrogate), as every read requires.</summary>
    /// <param name="text">The decoded text.</param>
    /// <param name="strictEncoding">A strict UTF-16 encoding, which throws on an invalid sequence.</param>
    /// <exception cref="CStructReadException">The text is not valid UTF-16.</exception>
    public static void ValidateWideText(string text, Encoding strictEncoding)
    {
        try
        {
            _ = strictEncoding.GetByteCount(text);
        }
        catch (EncoderFallbackException exception)
        {
            throw new CStructReadException(ReadFailures.WideTextInvalid, exception);
        }
    }

    /// <summary>Reads exactly the declared encoded byte extent, including embedded NULs, without reading ahead.</summary>
    /// <param name="stream">The source stream, positioned at the first text byte.</param>
    /// <param name="byteCount">The fixed extent in bytes (not characters) to read and decode.</param>
    /// <param name="type">The bounded-text codec name that selects the decoding, such as <c>utf8</c>.</param>
    /// <returns>The decoded text, including any embedded NUL characters.</returns>
    /// <exception cref="CStructReadLimitException">
    ///     <paramref name="byteCount"/> exceeds the per-string byte budget of a <see cref="ReadBudgetStream"/>.
    /// </exception>
    /// <exception cref="CStructReadException">The stream ends early or the bytes are invalid text.</exception>
    public static string ReadBoundedText(Stream stream, int byteCount, string type)
    {
        var source = new StreamTextSource(stream);
        return ReadBoundedText(ref source, byteCount, type);
    }

    /// <summary>
    ///     Reads exactly the declared encoded byte extent from any text source, including embedded NULs, without reading
    ///     ahead: the one implementation behind the stream overload and the engine's memory cursor.
    /// </summary>
    /// <typeparam name="TSource">The source type, a struct so the reader is compiled per source.</typeparam>
    /// <param name="source">The source, positioned at the first text byte; it advances past the bytes read.</param>
    /// <param name="byteCount">The fixed extent in bytes (not characters) to read and decode.</param>
    /// <param name="type">The bounded-text codec name that selects the decoding, such as <c>utf8</c>.</param>
    /// <returns>The decoded text, including any embedded NUL characters.</returns>
    /// <exception cref="CStructReadLimitException"><paramref name="byteCount"/> exceeds the source's string limit.</exception>
    /// <exception cref="CStructReadException">The source ends early or the bytes are invalid text.</exception>
    public static string ReadBoundedText<TSource>(ref TSource source, int byteCount, string type)
        where TSource : struct, ITextReadSource
    {
        if (source.StringByteLimit is long limit && byteCount > limit)
        {
            throw new CStructReadLimitException(ReadFailures.BoundedTextLimit);
        }

        byte[] bytes = new byte[byteCount];
        try
        {
            source.ReadExactly(bytes);
            return BoundedTextCodec.Decode(type, bytes);
        }
        catch (EndOfStreamException exception)
        {
            throw new CStructReadException(ReadFailures.BoundedTextShortRead, exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw new CStructReadException(ReadFailures.BoundedTextInvalid, exception);
        }
    }

    /// <summary>Reads characters until a terminator and leaves the stream immediately after that terminator.</summary>
    /// <param name="stream">The source stream; a budget stream adds its string limit and cancellation.</param>
    /// <param name="encoding">The strict encoding that decodes the text and encodes the terminator.</param>
    /// <param name="terminator">The character that ends the string, typically NUL or newline.</param>
    /// <returns>The decoded text without the terminator.</returns>
    /// <exception cref="CStructReadException">The stream ends before the terminator or holds invalid text.</exception>
    /// <exception cref="CStructReadLimitException">The encoded string exceeds the per-string byte budget.</exception>
    public static string ReadIntoString(Stream stream, Encoding encoding, char terminator)
    {
        var source = new StreamTextSource(stream);
        return ReadIntoString(ref source, encoding, terminator);
    }

    /// <summary>
    ///     Reads characters from any text source until a terminator and leaves the source immediately after it: the one
    ///     implementation behind the stream overload and the engine's memory cursor.
    /// </summary>
    /// <typeparam name="TSource">The source type, a struct so the reader is compiled per source.</typeparam>
    /// <param name="source">The source; its string limit and token apply.</param>
    /// <param name="encoding">The strict encoding that decodes the text and encodes the terminator.</param>
    /// <param name="terminator">The character that ends the string, typically NUL or newline.</param>
    /// <returns>The decoded text without the terminator.</returns>
    /// <exception cref="CStructReadException">The source ends before the terminator or holds invalid text.</exception>
    /// <exception cref="CStructReadLimitException">The encoded string exceeds the per-string byte budget.</exception>
    /// <exception cref="OperationCanceledException">The source's token is cancelled before a chunk is read.</exception>
    public static string ReadIntoString<TSource>(ref TSource source, Encoding encoding, char terminator)
        where TSource : struct, ITextReadSource
    {
        // Chunked reads, one decode per chunk prefix, observably the same as reading one byte at a time: the source
        // ends immediately after the terminator, an over-budget read leaves
        // the source one byte past the limit, a decode failure leaves it at the end of the chunk being decoded, and
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
            long? maxStringBytes = source.StringByteLimit;
            System.Threading.CancellationToken cancellation = source.CancellationToken;

            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                int bytesRead = source.Read(chunk, 0, TerminatedStringReadChunkSize);
                if (bytesRead == 0)
                {
                    throw new CStructReadException(ReadFailures.TerminatedStringUnterminated);
                }

                // A source may return fewer bytes than asked, splitting a UTF-16 code unit - and so a terminator -
                // between two reads. Complete the unit, so every chunk holds whole units from the string's start and
                // the terminator search sees it as a span's single read does; only the end of the input leaves a
                // partial unit, which then fails as unterminated there too. The chunk size is a whole number of units.
                while ((encodedByteCount + bytesRead) % unitSize != 0)
                {
                    int completion = source.Read(chunk, bytesRead, TerminatedStringReadChunkSize - bytesRead);
                    if (completion == 0)
                    {
                        break;
                    }

                    bytesRead += completion;
                }

                // Search from the first position that starts an encoding unit relative to the string's own start.
                int alignmentOffset = (int)((unitSize - (encodedByteCount % unitSize)) % unitSize);
                int terminatorIndex = Codec.FindTerminator(chunk.AsSpan(0, bytesRead), terminatorBytes, unitSize, alignmentOffset);

                // Budget arithmetic equivalent to counting every consumed byte, including the terminator's own bytes.
                if (maxStringBytes.HasValue)
                {
                    long allowed = maxStringBytes.Value - encodedByteCount;
                    long consumedIfFound = terminatorIndex < 0 ? bytesRead : terminatorIndex + terminatorLength;
                    if (consumedIfFound > allowed)
                    {
                        // The byte-by-byte reader consumed the over-budget byte before checking, so the source is left
                        // exactly one byte past the limit; only the never-inspected remainder is seeked back.
                        SeekBackUnconsumedChunkBytes(ref source, bytesRead, (int)Math.Min(bytesRead, allowed + 1));
                        throw new CStructReadLimitException(ReadFailures.TerminatedStringLimit);
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
                    throw new CStructReadException(ReadFailures.TerminatedStringInvalid, exception);
                }

                if (terminatorIndex < 0)
                {
                    encodedByteCount += bytesRead;
                    (builder ??= new StringBuilder()).Append(decoded, 0, charsUsed);
                    continue;
                }

                // Do not include the terminator in the public string value, and leave the source immediately after it.
                SeekBackUnconsumedChunkBytes(ref source, bytesRead, terminatorIndex + terminatorLength);
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

    /// <summary>Writes the encoded text followed by its encoded terminator.</summary>
    /// <param name="stream">The destination stream; a budget stream enforces its per-string byte limit.</param>
    /// <param name="encoding">The strict encoding for both the text and the terminator.</param>
    /// <param name="value">The text to write; it must not contain <paramref name="terminator"/>.</param>
    /// <param name="terminator">The character appended after the text, typically NUL or newline.</param>
    /// <exception cref="CStructWriteException">The value contains the terminator or cannot be encoded.</exception>
    public static void WriteTerminatedString(Stream stream, Encoding encoding, string value, char terminator)
    {
        if (value.Contains(terminator, StringComparison.Ordinal))
        {
            throw new CStructWriteException(WriteFailures.TerminatorInValue);
        }

        // Encode into a pooled buffer instead of concatenating the terminator and allocating a fresh byte[].
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
            throw new CStructWriteException(WriteFailures.InvalidForEncoding, exception);
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
    /// <param name="value">A value convertible to <see cref="char"/>, such as a char or a one-character string.</param>
    /// <returns>The character's code as one byte (0 to 255).</returns>
    /// <exception cref="CStructWriteException">The character's code is above 255.</exception>
    public static byte ConvertToNarrowCharacter(object value)
    {
        char character = Convert.ToChar(value, CultureInfo.InvariantCulture);
        if (character > byte.MaxValue)
        {
            throw new CStructWriteException(WriteFailures.NarrowCharacter(character));
        }

        return (byte)character;
    }

    /// <summary>
    ///     Moves a source back by the tail of the most recent chunk read that was not actually consumed, so a
    ///     chunked read leaves the source at the same position a byte-by-byte reader would have stopped at.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <param name="source">The source positioned immediately after the chunk read supplying <paramref name="bytesRead"/>.</param>
    /// <param name="bytesRead">The number of bytes the most recent chunk read actually returned.</param>
    /// <param name="consumedCount">The number of leading bytes of that chunk that were actually decoded or counted.</param>
    private static void SeekBackUnconsumedChunkBytes<TSource>(ref TSource source, int bytesRead, int consumedCount)
        where TSource : struct, ITextReadSource
    {
        int unconsumed = bytesRead - consumedCount;
        if (unconsumed > 0)
        {
            source.Rewind(unconsumed);
        }
    }
}
