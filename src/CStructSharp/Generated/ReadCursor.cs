namespace CStructSharp.Generated;

using System;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;

/// <summary>
///     The reading state a generated <c>Parse</c> method carries through one operation: the source bytes, the
///     position, the <see cref="ReadOptions"/> snapshot, and the accounting the runtime reader performs - the total
///     read-byte budget, array and string limits, nesting and pointer depth - with the runtime's failure texts, so a
///     generated reader and <see cref="CStruct.Parse(ReadOnlySpan{byte}, string?, System.Collections.Generic.IReadOnlyDictionary{string, int}?, ReadOptions?)"/>
///     report the same error for the same bytes. Every failure carries the runtime's context: the innermost field
///     and its type, the operation path, and the position the cursor had reached.
/// </summary>
/// <remarks>
///     This is an advanced surface, public so the code the <c>[CStructLayout]</c> generator emits can use it.
/// </remarks>
public ref struct ReadCursor
{
    private readonly ReadOnlySpan<byte> source;
    private readonly ReadOperationSettings settings;
    private readonly string? path;
    private int position;
    private long bytesRead;
    private int nestingDepth;
    private int pointerDepth;
    private int unionDepth;
    private System.Collections.Generic.HashSet<(long Address, string Type, int Depth)>? activeTargets;
    private System.Collections.Generic.Stack<(long Address, string Type, int Depth)>? activeTargetStack;

    /// <summary>Creates a cursor at the start of <paramref name="source"/>.</summary>
    /// <param name="source">The bytes to read; offset 0 is coordinate zero for addresses and diagnostics.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="path">The path the operation reads (<c>root</c>, <c>root.items[1]</c>), reported by every failure as the runtime does.</param>
    public ReadCursor(ReadOnlySpan<byte> source, ReadOptions? options = null, string? path = null)
    {
        this.source = source;
        this.settings = ReadOperationSettings.SnapshotReadOptions(options);
        this.path = path;
    }

    /// <summary>Gets or sets the offset of the next byte to read.</summary>
    public int Position
    {
        readonly get => this.position;
        set
        {
            if (value < 0 || value > this.source.Length)
            {
                throw this.Fail(ReadFailures.OutsideRegion, null, null);
            }

            this.position = value;
        }
    }

    /// <summary>Gets the path the operation reads, as reported in diagnostics.</summary>
    public readonly string? Path => this.path;

    /// <summary>Gets the number of bytes from the position to the end of the source.</summary>
    public readonly int Remaining => this.source.Length - this.position;

    /// <summary>Gets the whole source.</summary>
    public readonly ReadOnlySpan<byte> Source => this.source;

    /// <summary>Gets how pointer addresses are interpreted.</summary>
    public readonly PointerAddressingMode AddressingMode => this.settings.AddressingMode;

    /// <summary>Gets the origin relative pointers are measured from.</summary>
    public readonly long Origin => this.settings.Origin;

    /// <summary>Gets whether pointers are followed.</summary>
    public readonly bool DereferencePointers => this.settings.DereferencePointers;

    /// <summary>Gets whether pointers are followed here: the option, unless a union is being read (an untagged union never follows an address that merely overlaps its bytes).</summary>
    public readonly bool FollowsPointers => this.settings.DereferencePointers && this.unionDepth == 0;

    /// <summary>Gets the byte limit for one pointer target, when configured.</summary>
    public readonly long? MaxPointerTargetBytes => this.settings.MaxPointerTargetBytes;

    /// <summary>Gets whether fixed text drops its trailing NUL padding.</summary>
    public readonly bool TrimFixedText => this.settings.TrimFixedText;

    /// <summary>Gets the configured array element limit.</summary>
    public readonly int MaxArrayElements => this.settings.MaxArrayElements;

    /// <summary>Gets the configured encoded-string byte limit.</summary>
    public readonly long MaxStringBytes => this.settings.MaxStringBytes;

    /// <summary>Moves to <paramref name="position"/> (a placement result: an aligned field start, the end of a composite), failing with the runtime's text and context when it lies outside the input.</summary>
    /// <param name="position">The position to move to.</param>
    /// <param name="member">The layout field being placed, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void Seek(long position, string? member, string? memberType)
    {
        if (position < 0 || position > this.source.Length)
        {
            throw this.Fail(ReadFailures.OutsideRegion, member, memberType);
        }

        this.position = (int)position;
    }

    /// <summary>
    ///     Consumes <paramref name="count"/> bytes: checks the total read budget, checks that the bytes exist, and
    ///     advances. A short read moves the cursor to the end, as the stream reader ends there, and its message
    ///     states how many bytes the item needed and how many were left.
    /// </summary>
    /// <param name="count">The number of bytes the item occupies.</param>
    /// <param name="member">The layout field being read, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The consumed bytes.</returns>
    /// <exception cref="CStructReadLimitException">The total read budget is exceeded.</exception>
    /// <exception cref="CStructReadException">Fewer than <paramref name="count"/> bytes remain.</exception>
    public ReadOnlySpan<byte> Take(int count, string member, string? memberType)
    {
        this.Charge(count, member, memberType);
        if (count < 0 || count > this.Remaining)
        {
            throw this.ShortRead(count, member, memberType);
        }

        ReadOnlySpan<byte> bytes = this.source.Slice(this.position, count);
        this.position += count;
        return bytes;
    }

    /// <summary>
    ///     Consumes a whole numeric array as the runtime's bulk array reader does: the extent is checked before any
    ///     byte is read, and a short read names the element count and size.
    /// </summary>
    /// <param name="count">The element count.</param>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The array's bytes.</returns>
    /// <exception cref="CStructReadException">The array does not fit in the remaining bytes.</exception>
    /// <exception cref="CStructReadLimitException">The total read budget is exceeded.</exception>
    public ReadOnlySpan<byte> TakeArray(int count, int elementSize, string member, string? memberType)
    {
        long total = (long)count * elementSize;
        if (total > this.Remaining)
        {
            int available = this.Remaining;
            this.position = this.source.Length;
            throw this.Fail(ReadFailures.ArrayShortRead(count, elementSize, available), member, memberType);
        }

        return this.Take((int)total, member, memberType);
    }

    /// <summary>
    ///     Consumes a multidimensional numeric array as the runtime's block reader does: the extent is read in
    ///     blocks of at most 64 KiB, and a short read names the block that did not fit.
    /// </summary>
    /// <param name="count">The total element count.</param>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The array's bytes.</returns>
    public ReadOnlySpan<byte> TakeInBlocks(int count, int elementSize, string member, string? memberType)
    {
        const int BlockSize = 64 * 1024;
        long total = (long)count * elementSize;
        if (total <= 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        int blockCapacity = (int)Math.Min(total, BlockSize) / elementSize * elementSize;
        long remainingElementsBytes = total;
        int start = this.position;
        while (remainingElementsBytes > 0)
        {
            int blockLength = (int)Math.Min(remainingElementsBytes, blockCapacity);
            if (blockLength > this.Remaining)
            {
                int available = this.Remaining;
                this.position = this.source.Length;
                throw this.Fail(ReadFailures.ShortRead(blockLength, available), member, memberType);
            }

            this.Charge(blockLength, member, memberType);
            this.position += blockLength;
            remainingElementsBytes -= blockLength;
        }

        return this.source.Slice(start, (int)total);
    }

    /// <summary>
    ///     Consumes <paramref name="count"/> elements of <paramref name="elementSize"/> bytes as the runtime's
    ///     per-element loop does: a short read reports the element size as the need and what was left after the
    ///     last whole element, and a budget failure surfaces after the element that crossed the limit.
    /// </summary>
    /// <param name="count">The element count.</param>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The elements' bytes.</returns>
    /// <exception cref="CStructReadException">An element does not fit in the remaining bytes.</exception>
    /// <exception cref="CStructReadLimitException">The total read budget is exceeded.</exception>
    public ReadOnlySpan<byte> TakeElements(int count, int elementSize, string member, string? memberType)
    {
        if (count <= 0 || elementSize <= 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        long total = (long)count * elementSize;
        long available = this.Remaining;
        long wholeElements = available / elementSize;
        long allowed = Math.Max(0, this.settings.MaxTotalBytesRead - this.bytesRead);
        long affordableElements = allowed / elementSize;
        if (total > available && wholeElements <= affordableElements)
        {
            int leftover = (int)(available - (wholeElements * elementSize));
            this.position = this.source.Length;
            throw this.Fail(ReadFailures.ShortRead(elementSize, leftover), member, memberType);
        }

        if (total > allowed)
        {
            // The runtime charged element by element and failed after reading the element that crossed the limit.
            this.position += (int)Math.Min(available, (affordableElements + 1) * elementSize);
            throw this.FailLimit(ReadFailures.TotalBytesLimit, member, memberType);
        }

        return this.Take((int)total, member, memberType);
    }

    /// <summary>Consumes an encoded text buffer (<c>utf8[N]</c>, ...): the string byte limit, then the bytes, with the runtime's texts.</summary>
    /// <param name="count">The buffer size in bytes.</param>
    /// <param name="member">The text field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The buffer's bytes.</returns>
    /// <exception cref="CStructReadLimitException">The buffer exceeds <c>MaxStringBytes</c> or the read budget.</exception>
    /// <exception cref="CStructReadException">The buffer does not fit in the remaining bytes.</exception>
    public ReadOnlySpan<byte> TakeBoundedText(int count, string member, string? memberType)
    {
        this.RequireBoundedTextBytes(count, member, memberType);
        if (count > this.Remaining)
        {
            this.Charge(this.Remaining, member, memberType);
            this.position = this.source.Length;
            throw this.Fail(ReadFailures.BoundedTextShortRead, member, memberType);
        }

        return this.Take(count, member, memberType);
    }

    /// <summary>
    ///     Counts the elements of a <c>T values[EOF]</c> array: the whole elements from the position to the end of
    ///     the input, with the runtime's checks and texts.
    /// </summary>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <param name="fieldName">The layout field name, named in the messages.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The element count.</returns>
    public readonly int CountToEnd(int elementSize, string fieldName, string member, string? memberType)
    {
        long remaining = this.Remaining;
        if (elementSize == 0)
        {
            return 0;
        }

        if (remaining % elementSize != 0)
        {
            throw this.Fail(ReadFailures.ToEndRemainder(remaining, elementSize, fieldName), member, memberType);
        }

        long count = remaining / elementSize;
        if (count > this.settings.MaxArrayElements)
        {
            throw this.FailLimit(ReadFailures.ArrayLengthLimit(count, this.settings.MaxArrayElements), member, memberType);
        }

        return (int)count;
    }

    /// <summary>
    ///     Counts the elements of a <c>T values[]</c> array up to (not including) its all-zero terminator element,
    ///     leaving the position where it was; the runtime's texts report a missing terminator or too many elements.
    /// </summary>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <param name="fieldName">The layout field name, named in the messages.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The element count.</returns>
    public readonly int CountTerminated(int elementSize, string fieldName, string member, string? memberType)
    {
        if (elementSize == 0)
        {
            return 0;
        }

        int count = 0;
        int offset = this.position;
        while (true)
        {
            if (offset + elementSize > this.source.Length)
            {
                throw this.Fail(ReadFailures.TerminatedArrayUnterminated(fieldName), member, memberType);
            }

            if (this.source.Slice(offset, elementSize).IndexOfAnyExcept((byte)0) < 0)
            {
                return count;
            }

            if (++count > this.settings.MaxArrayElements)
            {
                throw this.FailLimit(ReadFailures.ArrayLengthLimit(count, this.settings.MaxArrayElements), member, memberType);
            }

            offset += elementSize;
        }
    }

    /// <summary>A layout failure (an offset assertion that does not hold) at the current position, with the runtime's context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructLayoutException FailLayout(string message, string? member, string? memberType)
    {
        var exception = new CStructLayoutException(message);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>Reads a 16-byte identifier (<c>uuid</c> in network order, <c>guid</c> in Windows order) with the runtime's short-read text.</summary>
    /// <param name="networkOrder">Whether the identifier is stored in network (big-endian) order.</param>
    /// <param name="member">The identifier field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The identifier.</returns>
    public Guid TakeGuid(bool networkOrder, string member, string? memberType)
    {
        if (this.Remaining < 16)
        {
            this.Charge(this.Remaining, member, memberType);
            this.position = this.source.Length;
            throw this.Fail(ReadFailures.IdentifierShortRead, member, memberType);
        }

        return Codec.ReadGuid(this.Take(16, member, memberType), networkOrder);
    }

    /// <summary>Reads one LEB128 integer byte by byte, as the runtime does, so a short read reports the single byte it needed.</summary>
    /// <param name="width">The payload width in bits (32 or 64).</param>
    /// <param name="signed">Whether the encoding is SLEB128.</param>
    /// <param name="member">The field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The decoded bits (sign-extended for a signed encoding).</returns>
    public ulong TakeLeb128(int width, bool signed, string member, string? memberType)
    {
        var decoder = new Codecs.Leb128Decoder(width, signed);
        while (true)
        {
            byte octet = this.Take(1, member, memberType)[0];
            try
            {
                if (decoder.Push(octet, out ulong value))
                {
                    return value;
                }
            }
            catch (CStructReadException exception)
            {
                throw this.Fail(exception.Message, member, memberType);
            }
        }
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
    public string TakeWideText(int count, bool littleEndian, string member, string? memberType)
    {
        ReadOnlySpan<byte> bytes = this.TakeElements(count, 2, member, memberType);
        var characters = new char[count];
        for (int index = 0; index < count; index++)
        {
            characters[index] = Codec.ReadChar(bytes.Slice(index * 2, 2), littleEndian);
        }

        string text = new(characters);
        try
        {
            _ = (littleEndian ? Codecs.PrimitiveCodecs.StrictUtf16LittleEndianEncoding : Codecs.PrimitiveCodecs.StrictUtf16BigEndianEncoding).GetByteCount(text);
        }
        catch (System.Text.EncoderFallbackException exception)
        {
            throw this.Fail(ReadFailures.WideTextInvalid, member, memberType, exception);
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
        System.Text.Encoding strict = encoding switch
        {
            TerminatedTextEncoding.Ascii => Codecs.PrimitiveCodecs.StrictAsciiEncoding,
            TerminatedTextEncoding.Utf8 => Codecs.PrimitiveCodecs.StrictUtf8Encoding,
            TerminatedTextEncoding.Utf16LittleEndian => Codecs.PrimitiveCodecs.StrictUtf16LittleEndianEncoding,
            _ => Codecs.PrimitiveCodecs.StrictUtf16BigEndianEncoding,
        };
        int unitSize = encoding is TerminatedTextEncoding.Utf16LittleEndian or TerminatedTextEncoding.Utf16BigEndian ? 2 : 1;
        Span<byte> terminatorBytes = stackalloc byte[4];
        int terminatorLength = strict.GetBytes(new ReadOnlySpan<char>(in terminator), terminatorBytes);
        ReadOnlySpan<byte> remaining = this.source.Slice(this.position);
        int index = Codec.FindTerminator(remaining, terminatorBytes.Slice(0, terminatorLength), unitSize, 0);
        long consumed = index < 0 ? remaining.Length : index + terminatorLength;
        if (consumed > this.settings.MaxStringBytes)
        {
            this.position += (int)Math.Min(remaining.Length, this.settings.MaxStringBytes + 1);
            throw this.FailLimit(ReadFailures.TerminatedStringLimit, member, memberType);
        }

        if (index < 0)
        {
            this.Charge(remaining.Length, member, memberType);
            this.position = this.source.Length;
            throw this.Fail(ReadFailures.TerminatedStringUnterminated, member, memberType);
        }

        ReadOnlySpan<byte> payload = this.Take((int)consumed, member, memberType).Slice(0, index);
        try
        {
            return strict.GetString(payload);
        }
        catch (System.Text.DecoderFallbackException exception)
        {
            throw this.Fail(ReadFailures.TerminatedStringInvalid, member, memberType, exception);
        }
    }

    /// <summary>The next <paramref name="count"/> bytes without consuming them or charging the budget.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The layout field being read, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="CStructReadException">Fewer than <paramref name="count"/> bytes remain.</exception>
    public ReadOnlySpan<byte> Peek(int count, string member, string? memberType)
    {
        if (count < 0 || count > this.Remaining)
        {
            throw this.ShortRead(count, member, memberType);
        }

        return this.source.Slice(this.position, count);
    }

    /// <summary>Moves past padding or a skipped member without reading it; skipped bytes are not charged.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The layout field being skipped, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructReadException">The skip would leave the source.</exception>
    public void Skip(int count, string member, string? memberType)
    {
        if (count < 0 || count > this.Remaining)
        {
            throw this.ShortRead(count, member, memberType);
        }

        this.position += count;
    }

    /// <summary>Moves to the next multiple of <paramref name="alignment"/> measured from <paramref name="origin"/>.</summary>
    /// <param name="alignment">The alignment in bytes.</param>
    /// <param name="origin">The position alignment is measured from.</param>
    /// <param name="member">The layout field being aligned, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void Align(int alignment, long origin, string member, string? memberType)
    {
        if (alignment <= 1)
        {
            return;
        }

        long relative = this.position - origin;
        long padding = (alignment - (relative % alignment)) % alignment;
        this.Skip((int)padding, member, memberType);
    }

    /// <summary>Enters a nested struct or union, enforcing <c>MaxNestingDepth</c>.</summary>
    /// <param name="member">The composite field being entered, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructReadLimitException">The nesting limit is exceeded.</exception>
    public void EnterComposite(string member, string? memberType)
    {
        if (this.nestingDepth >= this.settings.MaxNestingDepth)
        {
            throw this.FailLimit(ReadFailures.NestingLimit, member, memberType);
        }

        this.nestingDepth++;
    }

    /// <summary>Leaves the current struct or union.</summary>
    public void ExitComposite() => this.nestingDepth--;

    /// <summary>Reads a stored pointer address of the layout's width, as a signed stream position.</summary>
    /// <param name="pointerSize">The pointer width in bytes (1, 2, 4, or 8).</param>
    /// <param name="littleEndian">The layout's byte order.</param>
    /// <param name="member">The pointer field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The address; 0 is null.</returns>
    /// <exception cref="CStructReadException">The address does not fit the signed position range.</exception>
    public long TakePointerAddress(int pointerSize, bool littleEndian, string member, string? memberType)
    {
        ulong raw = Codec.ReadUnsigned(this.Take(pointerSize, member, memberType), littleEndian);
        if (raw > long.MaxValue)
        {
            throw this.Fail(ReadFailures.PointerAddressRange, member, memberType, new OverflowException("Pointer address exceeds the signed stream-position range."));
        }

        return (long)raw;
    }

    /// <summary>Marks the start of a union's members; pointers inside are not followed. Pair with <see cref="ExitUnion"/>.</summary>
    public void EnterUnion() => this.unionDepth++;

    /// <summary>Marks the end of a union's members.</summary>
    public void ExitUnion() => this.unionDepth--;

    /// <summary>
    ///     Follows a non-null pointer with the runtime's checks: the pointer depth limit, the resolved address
    ///     (relative to <see cref="Origin"/> when configured) inside the input, the optional target byte budget, and
    ///     a cycle on the active path. Restore the position afterwards with <see cref="ExitPointer"/>.
    /// </summary>
    /// <param name="address">The stored address (non-zero).</param>
    /// <param name="depth">The pointer depth being followed (1 for <c>T *</c>).</param>
    /// <param name="targetSize">The target's fixed size, or <see langword="null"/> for a variable-length target.</param>
    /// <param name="targetType">The pointer field's type spelling, which keys the cycle check.</param>
    /// <param name="member">The pointer field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The position to return to.</returns>
    /// <exception cref="CStructReadLimitException">The pointer depth or target byte limit is exceeded.</exception>
    /// <exception cref="CStructReadException">The target lies outside the input or is already being read.</exception>
    public int EnterPointer(long address, int depth, long? targetSize, string targetType, string member, string? memberType)
    {
        if (this.pointerDepth >= this.settings.MaxPointerDepth)
        {
            throw this.FailLimit(ReadFailures.PointerDepthLimit, member, memberType);
        }

        long target;
        try
        {
            target = this.settings.AddressingMode == PointerAddressingMode.Relative ? checked(address + this.settings.Origin) : address;
        }
        catch (OverflowException exception)
        {
            throw this.Fail(ReadFailures.RelativePointerOverflow, member, memberType, exception);
        }

        if (target < 0 || target >= this.source.Length)
        {
            throw this.Fail(ReadFailures.PointerTargetOutside(target), member, memberType);
        }

        if (this.settings.MaxPointerTargetBytes is { } limit)
        {
            if (targetSize is null)
            {
                throw this.FailLimit(ReadFailures.PointerTargetVariableLength, member, memberType);
            }

            if (targetSize.Value > limit)
            {
                throw this.FailLimit(ReadFailures.PointerTargetLimit, member, memberType);
            }
        }

        this.activeTargets ??= new System.Collections.Generic.HashSet<(long, string, int)>();
        if (!this.activeTargets.Add((target, targetType, depth)))
        {
            throw this.Fail(ReadFailures.CyclicPointer(target), member, memberType);
        }

        int resume = this.position;
        this.pointerDepth++;
        this.position = (int)target;
        this.activeTargetStack ??= new System.Collections.Generic.Stack<(long, string, int)>();
        this.activeTargetStack.Push((target, targetType, depth));
        return resume;
    }

    /// <summary>Returns from a pointer target to <paramref name="resume"/>, the value <see cref="EnterPointer"/> returned.</summary>
    /// <param name="resume">The position to return to.</param>
    public void ExitPointer(int resume)
    {
        this.pointerDepth--;
        this.position = resume;
        if (this.activeTargetStack is { Count: > 0 })
        {
            this.activeTargets!.Remove(this.activeTargetStack.Pop());
        }
    }

    /// <summary>Validates an array length against <c>MaxArrayElements</c> and returns it as an <see cref="int"/>.</summary>
    /// <param name="count">The element count.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The validated count.</returns>
    /// <exception cref="CStructReadLimitException">The length exceeds the limit.</exception>
    public readonly int RequireArrayLength(long count, string member, string? memberType)
    {
        if (count < 0 || count > this.settings.MaxArrayElements)
        {
            throw this.FailLimit(ReadFailures.ArrayLengthLimit(count, this.settings.MaxArrayElements), member, memberType);
        }

        return (int)count;
    }

    /// <summary>Validates an encoded text buffer's size against <c>MaxStringBytes</c>.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The text field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructReadLimitException">The buffer exceeds the limit.</exception>
    public readonly void RequireBoundedTextBytes(long count, string member, string? memberType)
    {
        if (count > this.settings.MaxStringBytes)
        {
            throw this.FailLimit(ReadFailures.BoundedTextLimit, member, memberType);
        }
    }

    /// <summary>Validates a terminated string's encoded length (terminator included) against <c>MaxStringBytes</c>.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The string field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructReadLimitException">The string exceeds the limit.</exception>
    public readonly void RequireTerminatedStringBytes(long count, string member, string? memberType)
    {
        if (count > this.settings.MaxStringBytes)
        {
            throw this.FailLimit(ReadFailures.TerminatedStringLimit, member, memberType);
        }
    }

    /// <summary>
    ///     A read failure at the current position with the runtime's context: the innermost field and its type, the
    ///     operation path, and the offset.
    /// </summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructReadException Fail(string message, string? member, string? memberType)
        => this.Fail(message, member, memberType, null);

    /// <summary>A read failure at the current position with the runtime's context and a lower-level cause.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <param name="cause">The exception that caused the failure, or <see langword="null"/>.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructReadException Fail(string message, string? member, string? memberType, Exception? cause)
    {
        var exception = cause is null ? new CStructReadException(message) : new CStructReadException(message, cause);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>A limit failure at the current position, with the runtime's context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructReadLimitException FailLimit(string message, string? member, string? memberType)
    {
        var exception = new CStructReadLimitException(message);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>
    ///     The failure of a layout expression (an array length, a condition) evaluated by generated code: the
    ///     runtime's <c>Cannot evaluate {context}: {reason}</c> text around an exception one of the
    ///     <see cref="Expressions"/> operators raised.
    /// </summary>
    /// <param name="exception">The exception the expression raised.</param>
    /// <param name="context">What was being evaluated, as the runtime names it (<c>array length for items</c>).</param>
    /// <param name="member">The layout field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw, or <paramref name="exception"/> itself when it is not an expression failure.</returns>
    public readonly Exception FailExpression(Exception exception, string context, string? member, string? memberType)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!LayoutExpressionEvaluator.IsExpressionFailure(exception))
        {
            return exception;
        }

        var failure = new CStructReadException(LayoutExpressionEvaluator.DescribeFailure(context, exception), exception);
        this.Attach(failure, member, memberType);
        return failure;
    }

    /// <summary>
    ///     Records the position an operation had reached when <paramref name="exception"/> left it, as the runtime
    ///     does at its operation boundary: a pointer target's failure reports the position after the pointer, not
    ///     the position inside the target. Generated <c>Parse</c> methods call this in their catch block.
    /// </summary>
    /// <param name="exception">The failure leaving the operation.</param>
    public readonly void Complete(CStructException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.AttachContext(this.path, this.position);
    }

    private readonly void Attach(CStructException exception, string? member, string? memberType)
    {
        if (member is not null)
        {
            exception.AttachMember(member, memberType);
        }

        // The offset is attached when the exception leaves the operation (Complete), where the runtime attaches it.
        exception.AttachContext(this.path, null);
    }

    private CStructReadException ShortRead(int count, string member, string? memberType)
    {
        int available = this.Remaining;
        this.position = this.source.Length;
        return this.Fail(ReadFailures.ShortRead(count, available), member, memberType);
    }

    private void Charge(int count, string member, string? memberType)
    {
        if (count <= 0)
        {
            return;
        }

        long total = this.bytesRead + count;
        if (total > this.settings.MaxTotalBytesRead)
        {
            throw this.FailLimit(ReadFailures.TotalBytesLimit, member, memberType);
        }

        this.bytesRead = total;
    }
}
