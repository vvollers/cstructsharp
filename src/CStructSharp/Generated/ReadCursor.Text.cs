namespace CStructSharp.Generated;

using System;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>Consuming text: fixed character buffers, encoded text buffers and terminated strings, with the runtime's string limit and failure texts.</summary>
public ref partial struct ReadCursor
{
    /// <summary>Consumes an encoded text buffer (<c>utf8[N]</c>, ...): the string byte limit, then the bytes, with the runtime's texts.</summary>
    /// <param name="count">The buffer size in bytes.</param>
    /// <param name="member">The text field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The buffer's bytes.</returns>
    /// <exception cref="CStructReadLimitException">The buffer exceeds <c>MaxStringBytes</c> or the read budget.</exception>
    /// <exception cref="CStructReadException">The buffer does not fit in the remaining bytes.</exception>
    internal ReadOnlySpan<byte> TakeBoundedText(int count, string member, string? memberType)
    {
        this.RequireBoundedTextBytes(count, member, memberType);
        if (count > this.Remaining)
        {
            this.RequireBuffered(this.position, (long)this.position + count);
            this.Charge(this.Remaining, member, memberType);
            this.position = this.source.Length;
            throw this.Fail(ReadFailures.BoundedTextShortRead, member, memberType);
        }

        return this.Take(count, member, memberType);
    }

    /// <summary>Reads a fixed one-byte character buffer (<c>char[N]</c>) as the runtime does: element by element, Latin-1 code points, <c>TrimFixedText</c> applied.</summary>
    /// <param name="count">The buffer length in characters.</param>
    /// <param name="member">The text field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The text.</returns>
    public string TakeFixedText(int count, string member, string? memberType)
    {
        ReadOnlySpan<byte> bytes = this.TakeElements(count, 1, member, memberType);
        return Codec.DecodeFixedText(bytes, this.settings.TrimFixedText);
    }

    /// <summary>Reads a fixed wide-character buffer (<c>wchar[N]</c>): element by element, validated as UTF-16, <c>TrimFixedText</c> applied.</summary>
    /// <param name="count">The buffer length in code units.</param>
    /// <param name="littleEndian">The code units' byte order.</param>
    /// <param name="member">The text field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The text.</returns>
    /// <exception cref="CStructReadException">The code units are not valid UTF-16.</exception>
    /// <exception cref="OverflowException">The code-unit count is negative.</exception>
    public string TakeWideText(int count, bool littleEndian, string member, string? memberType)
    {
        ReadOnlySpan<byte> bytes = this.TakeElements(count, 2, member, memberType);
        if (count < 0)
        {
            // This advanced entry point historically reaches array allocation for a negative count, after the
            // cursor's cancellation check. Keep that allocation failure even though valid reads need no array.
            _ = new char[count];
        }

        string text;
        try
        {
            text = Codec.DecodeWideCharacters(bytes, littleEndian);
        }
        catch (CStructReadException exception)
        {
            throw this.Fail(ReadFailures.WideTextInvalid, member, memberType, exception.InnerException);
        }

        return this.settings.TrimFixedText ? text.TrimEnd('\0') : text;
    }

    /// <summary>Reads an encoded text buffer (<c>utf8[N]</c>, <c>latin1[N]</c>, <c>cp437[N]</c>, <c>utf16le[N]</c>, <c>utf16be[N]</c>) with the runtime's limits, texts, and <c>TrimFixedText</c>.</summary>
    /// <param name="count">The buffer size in bytes.</param>
    /// <param name="encoding">The layout's encoding name.</param>
    /// <param name="member">The text field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The text.</returns>
    public string TakeEncodedText(int count, string encoding, string member, string? memberType)
    {
        ReadOnlySpan<byte> bytes = this.TakeBoundedText(count, member, memberType);
        try
        {
            return Codec.DecodeBoundedText(bytes, encoding, this.settings.TrimFixedText);
        }
        catch (CStructReadException exception)
        {
            throw this.Fail(exception.Message, member, memberType, exception.InnerException);
        }
    }

    /// <summary>
    ///     Reads a terminated string (<c>cstring</c>, <c>utf8_string_zero</c>, <c>string</c>, ...): the terminator is
    ///     consumed but not returned, the encoded length counts against <c>MaxStringBytes</c>, and the runtime's texts
    ///     report a missing terminator or invalid bytes.
    /// </summary>
    /// <param name="encoding">The string's encoding.</param>
    /// <param name="terminator">The terminator character (<c>'\0'</c> or <c>'\n'</c>).</param>
    /// <param name="member">The string field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The text without its terminator.</returns>
    public string TakeTerminatedString(TerminatedTextEncoding encoding, char terminator, string member, string? memberType)
    {
        // The runtime reads the string in chunks of 256 bytes (fewer when the read budget is smaller) and, per chunk,
        // checks the string byte limit, decodes the bytes before the terminator (flushing only when the terminator is in
        // the chunk), and stops at the terminator; the checks run in that order, and the position a failure reports is
        // the end of the chunk being read (the limit failure: one byte past the limit). Only the bytes through the
        // terminator are charged. A string those chunks would read without a failure is first read in one step
        // (PrimitiveCodecs.TryReadWholeTerminated, shared with the engine's memory cursor); any other string goes chunk by
        // chunk, which reports its failure.
        const int Chunk = Codecs.PrimitiveCodecs.TerminatedStringReadChunkSize;
        System.Text.Encoding strict = Codecs.PrimitiveCodecs.StrictEncodingOf(encoding);
        int unitSize = encoding is TerminatedTextEncoding.Utf16LittleEndian or TerminatedTextEncoding.Utf16BigEndian ? 2 : 1;
        Span<byte> terminatorBytes = stackalloc byte[4];
        int terminatorLength = Codecs.PrimitiveCodecs.EncodeTerminator(strict, terminator, terminatorBytes);
        terminatorBytes = terminatorBytes.Slice(0, terminatorLength);
        long budget = this.settings.MaxTotalBytesRead - this.bytesRead;
        if (!this.settings.CancellationToken.IsCancellationRequested &&
            Codecs.PrimitiveCodecs.TryReadWholeTerminated(
                this.source.Slice(this.position),
                strict,
                unitSize,
                terminatorBytes,
                this.settings.MaxStringBytes,
                budget,
                out string? whole,
                out int consumed))
        {
            this.bytesRead += consumed;
            this.position += consumed;
            return whole;
        }

        ReadOnlySpan<byte> remaining = this.source.Slice(this.position);
        int start = this.position;
        System.Text.Decoder decoder = strict.GetDecoder();
        char[]? decoded = null;
        long encodedByteCount = 0;
        int offset = 0;
        while (true)
        {
            this.settings.CancellationToken.ThrowIfCancellationRequested();
            int request = Codecs.PrimitiveCodecs.ChunkRequest(budget - offset, unitSize);
            if (request + unitSize - 1 > remaining.Length - offset)
            {
                // The chunk, or the second byte of its last code unit, would run past a partly buffered source.
                this.RequireBuffered(start + offset, (long)start + offset + request + unitSize - 1);
            }

            int bytesRead = Math.Min(request, remaining.Length - offset);
            if (bytesRead == 0)
            {
                throw this.Fail(ReadFailures.TerminatedStringUnterminated, member, memberType);
            }

            // The runtime charges each read as it happens: a read past the budget fails at its end. A budget below one
            // code unit asks for one byte, and the runtime then reads the unit's second byte as a second read.
            this.position = start + offset + bytesRead;
            if (offset + bytesRead > budget)
            {
                throw this.FailLimit(ReadFailures.TotalBytesLimit, member, memberType);
            }

            if (bytesRead % unitSize != 0 && offset + bytesRead < remaining.Length)
            {
                bytesRead++;
                this.position++;
                if (offset + bytesRead > budget)
                {
                    throw this.FailLimit(ReadFailures.TotalBytesLimit, member, memberType);
                }
            }

            ReadOnlySpan<byte> chunk = remaining.Slice(offset, bytesRead);

            int alignmentOffset = (int)((unitSize - (encodedByteCount % unitSize)) % unitSize);
            int terminatorIndex = Codec.FindTerminator(chunk, terminatorBytes, unitSize, alignmentOffset);
            long allowed = this.settings.MaxStringBytes - encodedByteCount;
            long consumedIfFound = terminatorIndex < 0 ? bytesRead : terminatorIndex + terminatorLength;
            if (consumedIfFound > allowed)
            {
                this.position = start + offset + (int)Math.Min(bytesRead, allowed + 1);
                throw this.FailLimit(ReadFailures.TerminatedStringLimit, member, memberType);
            }

            int prefixLength = terminatorIndex < 0 ? bytesRead : terminatorIndex;
            decoded ??= new char[Chunk + 2];
            try
            {
                decoder.Convert(chunk.Slice(0, prefixLength), decoded, terminatorIndex >= 0, out _, out _, out _);
            }
            catch (System.Text.DecoderFallbackException exception)
            {
                throw this.Fail(ReadFailures.TerminatedStringInvalid, member, memberType, exception);
            }

            if (terminatorIndex < 0)
            {
                encodedByteCount += bytesRead;
                offset += bytesRead;
                continue;
            }

            // Every chunk stayed within the budget, so charging the string and its terminator cannot fail.
            int payloadLength = offset + terminatorIndex;
            this.Charge(payloadLength + terminatorLength, member, memberType);
            this.position = start + payloadLength + terminatorLength;
            return strict.GetString(remaining.Slice(0, payloadLength));
        }
    }

    /// <summary>Validates an encoded text buffer's size against <c>MaxStringBytes</c>.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The text field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructReadLimitException">The buffer exceeds the limit.</exception>
    internal readonly void RequireBoundedTextBytes(long count, string member, string? memberType)
    {
        if (count > this.settings.MaxStringBytes)
        {
            throw this.FailLimit(ReadFailures.BoundedTextLimit, member, memberType);
        }
    }
}
