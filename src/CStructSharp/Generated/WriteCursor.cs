namespace CStructSharp.Generated;

using System;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Writing;

/// <summary>
///     The writing state a generated <c>Serialize</c> method carries through one operation: the destination, the
///     position, the <see cref="WriteOptions"/> snapshot, and the accounting the runtime writer performs - the
///     total byte budget, array and string limits, nesting depth - with the runtime's failure texts and context
///     (the innermost field and its type, the operation path, the offset). Bytes the cursor pads are zero-filled,
///     as the runtime writer does.
/// </summary>
/// <remarks>
///     This is an advanced surface, public so the code the <c>[CStructLayout]</c> generator emits can use it.
/// </remarks>
public ref struct WriteCursor
{
    private const int InitialOwnedCapacity = 256;

    private readonly WriteOptions options;
    private readonly string? path;
    private Span<byte> destination;
    private byte[]? owned;
    private int position;
    private int length;
    private int nestingDepth;

    /// <summary>
    ///     Creates a cursor over a buffer it grows as it writes (the runtime's <c>Serialize</c> to a new array): the
    ///     result is <see cref="ToArray"/>; call <see cref="Dispose"/> afterwards to return the buffer to the pool.
    /// </summary>
    /// <param name="options">The write options; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="path">The path the operation writes (<c>root</c>), reported by every failure as the runtime does.</param>
    public WriteCursor(WriteOptions? options, string? path)
    {
        this.options = CStructElementWriterState.SnapshotWriteOptions(options);
        CStructElementWriterState.ValidateWriteOptions(this.options);
        this.path = path;
        this.owned = System.Buffers.ArrayPool<byte>.Shared.Rent(InitialOwnedCapacity);
        this.destination = this.owned;
    }

    /// <summary>Creates a cursor at the start of <paramref name="destination"/>.</summary>
    /// <param name="destination">The bytes to write into; a value that does not fit fails with the runtime's capacity message.</param>
    /// <param name="options">The write options; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="path">The path the operation writes (<c>root</c>), reported by every failure as the runtime does.</param>
    public WriteCursor(Span<byte> destination, WriteOptions? options = null, string? path = null)
    {
        this.destination = destination;
        this.options = CStructElementWriterState.SnapshotWriteOptions(options);
        CStructElementWriterState.ValidateWriteOptions(this.options);
        this.path = path;
    }

    /// <summary>Gets or sets the offset of the next byte to write.</summary>
    public int Position
    {
        readonly get => this.position;
        set
        {
            if (value < 0 || value > this.length)
            {
                throw this.Fail(WriteFailures.DestinationCapacity, null, null);
            }

            this.position = value;
        }
    }

    /// <summary>Gets whether the cursor owns a growing buffer (see <see cref="WriteCursor(WriteOptions?, string?)"/>).</summary>
    public readonly bool IsGrowable => this.owned is not null;

    /// <summary>Gets the path the operation writes, as reported in diagnostics.</summary>
    public readonly string? Path => this.path;

    /// <summary>Gets the number of bytes written so far (the position; a serialize never seeks backwards).</summary>
    public readonly int Length => this.length;

    /// <summary>Gets the bytes written so far.</summary>
    public readonly ReadOnlySpan<byte> Written => this.destination.Slice(0, this.length);

    /// <summary>Gets how pointer addresses are interpreted.</summary>
    public readonly PointerAddressingMode AddressingMode => this.options.AddressingMode;

    /// <summary>Gets the origin relative pointers are measured from.</summary>
    public readonly long Origin => this.options.Origin;

    /// <summary>Gets the configured array element limit.</summary>
    public readonly int MaxArrayElements => this.options.MaxArrayElements;

    /// <summary>Gets the configured encoded-string byte limit.</summary>
    public readonly long MaxStringBytes => this.options.MaxStringBytes;

    /// <summary>
    ///     Creates a cursor over bytes that already hold a value (a generated <c>Update</c> setter): every byte counts
    ///     as written, so positioning inside the buffer touches nothing around the field.
    /// </summary>
    /// <param name="target">The bytes holding the value.</param>
    /// <param name="options">The write options; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="path">The path the operation writes, reported by every failure.</param>
    /// <returns>The cursor.</returns>
    public static WriteCursor ForUpdate(Span<byte> target, WriteOptions? options = null, string? path = null)
    {
        var cursor = new WriteCursor(target, options, path);
        cursor.length = target.Length;
        return cursor;
    }

    /// <summary>
    ///     The options an awaitable generated write runs with: <paramref name="options"/> carrying the token that
    ///     ends the operation - the options' own token, <paramref name="cancellationToken"/>, or a source linked from
    ///     both when both can cancel (the caller disposes <paramref name="linked"/> after the operation).
    /// </summary>
    /// <param name="options">The caller's write options, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token given to the async method.</param>
    /// <param name="linked">The linked source when both tokens can cancel; otherwise <see langword="null"/>.</param>
    /// <returns>The options to write with; <see langword="null"/> when neither token can cancel and none were given.</returns>
    public static WriteOptions? WithCancellation(WriteOptions? options, System.Threading.CancellationToken cancellationToken, out System.Threading.CancellationTokenSource? linked)
    {
        System.Threading.CancellationToken token = Streams.AsyncStreamBuffer.Link(options, cancellationToken, out linked);
        return token.CanBeCanceled ? (options ?? new WriteOptions()) with { CancellationToken = token, } : options;
    }

    /// <summary>
    ///     Reserves <paramref name="count"/> bytes for a member: checks the total byte budget and the destination
    ///     capacity, advances, and returns the slice to encode into.
    /// </summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The layout field being written, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The reserved bytes.</returns>
    /// <exception cref="CStructWriteLimitException">The total byte budget is exceeded.</exception>
    /// <exception cref="CStructWriteException">The destination is too small.</exception>
    public Span<byte> Reserve(int count, string? member, string? memberType)
    {
        long end = (long)this.position + count;
        if (count < 0 || end > this.options.MaxTotalBytesWritten)
        {
            throw this.FailLimit(WriteFailures.TotalBytesLimit, member, memberType);
        }

        if (end > this.destination.Length)
        {
            if (this.owned is null)
            {
                throw this.Fail(WriteFailures.DestinationCapacity, member, memberType);
            }

            this.Grow((int)end);
        }

        Span<byte> bytes = this.destination.Slice(this.position, count);
        this.position += count;
        if (this.position > this.length)
        {
            // Bytes between the old end and the new position that no write covered are zero, as a stream's are.
            this.length = this.position;
        }

        return bytes;
    }

    /// <summary>
    ///     Moves to <paramref name="position"/>: forward past the end writes zero padding (the runtime's aligned
    ///     padding and struct tails), backward re-positions inside what was written (a union's members all start at
    ///     the union's first byte).
    /// </summary>
    /// <param name="position">The new position.</param>
    /// <param name="member">The field being placed, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void Seek(long position, string? member, string? memberType)
    {
        if (position < 0 || position > int.MaxValue)
        {
            throw this.Fail(WriteFailures.DestinationCapacity, member, memberType);
        }

        if (position > this.length)
        {
            if (this.owned is null && position > this.destination.Length)
            {
                // The runtime moves its stream position past the padding; a fixed buffer rejects the position itself.
                throw this.Fail(ReadFailures.OutsideRegion, member, memberType);
            }

            this.position = this.length;
            this.Reserve((int)position - this.length, member, memberType).Clear();
            return;
        }

        this.position = (int)position;
    }

    /// <summary>
    ///     The bytes of a bitfield storage unit at <paramref name="unitStart"/>, extended with zeros when the unit
    ///     reaches past what was written; the position moves to the unit's end.
    /// </summary>
    /// <param name="unitStart">The unit's first byte.</param>
    /// <param name="unitSize">The unit size in bytes.</param>
    /// <param name="member">The bitfield, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The unit's bytes, to merge the field's bits into.</returns>
    public Span<byte> Unit(long unitStart, int unitSize, string member, string? memberType)
    {
        this.Seek(unitStart, member, memberType);
        long end = unitStart + unitSize;
        if (end > this.length)
        {
            this.position = this.length;
            this.Reserve((int)(end - this.length), member, memberType).Clear();
        }

        this.position = (int)end;
        return this.destination.Slice((int)unitStart, unitSize);
    }

    /// <summary>
    ///     Writes a terminated string (<c>cstring</c>, <c>string</c>, <c>wchar *</c>): the value must not contain the
    ///     terminator, its encoded bytes plus the terminator count against <c>MaxStringBytes</c>, and an unencodable
    ///     character fails with the runtime's text.
    /// </summary>
    /// <param name="encoding">The text encoding.</param>
    /// <param name="terminator">The terminator character.</param>
    /// <param name="value">The string to write.</param>
    /// <param name="member">The field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void WriteTerminatedString(TerminatedTextEncoding encoding, char terminator, string value, string member, string? memberType)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Contains(terminator, StringComparison.Ordinal))
        {
            throw this.Fail(WriteFailures.TerminatorInValue, member, memberType);
        }

        System.Text.Encoding strict = StrictEncoding(encoding);
        try
        {
            int valueBytes = strict.GetByteCount(value);
            int terminatorBytes = strict.GetByteCount(new ReadOnlySpan<char>(in terminator));
            this.RequireStringBytes((long)valueBytes + terminatorBytes, member, memberType);
            Span<byte> bytes = this.Reserve(valueBytes + terminatorBytes, member, memberType);
            int written = strict.GetBytes(value, bytes);
            strict.GetBytes(new ReadOnlySpan<char>(in terminator), bytes.Slice(written));
        }
        catch (System.Text.EncoderFallbackException exception)
        {
            var failure = new CStructWriteException(WriteFailures.InvalidForEncoding, exception);
            this.Attach(failure, member, memberType);
            throw failure;
        }
    }

    /// <summary>
    ///     Writes a fixed character array (<c>char name[N]</c>, <c>wchar name[N]</c>): the value must fit, the rest
    ///     is NUL padding; a one-byte <c>char</c> outside the byte range and an unencodable wide character fail with
    ///     the runtime's texts.
    /// </summary>
    /// <param name="count">The declared character count.</param>
    /// <param name="value">The string to write.</param>
    /// <param name="wide">Whether the element is <c>wchar</c> (two bytes per character).</param>
    /// <param name="littleEndian">The wide encoding's byte order.</param>
    /// <param name="member">The field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void WriteFixedText(int count, string value, bool wide, bool littleEndian, string member, string? memberType)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > count)
        {
            throw this.Fail(WriteFailures.FixedTextTooLong(member, value.Length, count), member, memberType);
        }

        this.RequireStringBytes((long)count * (wide ? 2 : 1), member, memberType);
        if (wide)
        {
            System.Text.Encoding strict = littleEndian ? Codecs.PrimitiveCodecs.StrictUtf16LittleEndianEncoding : Codecs.PrimitiveCodecs.StrictUtf16BigEndianEncoding;
            Span<byte> bytes = this.Reserve(count * 2, member, memberType);
            try
            {
                int written = strict.GetBytes(value, bytes);
                bytes.Slice(written).Clear();
            }
            catch (System.Text.EncoderFallbackException exception)
            {
                var failure = new CStructWriteException(WriteFailures.InvalidWideText, exception);
                this.Attach(failure, member, memberType);
                throw failure;
            }

            return;
        }

        Span<byte> narrow = this.Reserve(count, member, memberType);
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character > byte.MaxValue)
            {
                throw this.Fail(WriteFailures.NarrowCharacter(character), member, memberType);
            }

            narrow[index] = (byte)character;
        }

        narrow.Slice(value.Length).Clear();
    }

    /// <summary>
    ///     Writes an encoded text buffer (<c>utf8 label[N]</c>, ...): the byte capacity counts against
    ///     <c>MaxStringBytes</c>, a UTF-16 buffer needs an even capacity, the encoded value must fit, and the rest is
    ///     zero.
    /// </summary>
    /// <param name="count">The buffer size in bytes.</param>
    /// <param name="encoding">The encoding name (<c>utf8</c>, <c>utf16le</c>, ...).</param>
    /// <param name="value">The string to write.</param>
    /// <param name="member">The field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void WriteBoundedText(int count, string encoding, string value, string member, string? memberType)
    {
        ArgumentNullException.ThrowIfNull(encoding);
        ArgumentNullException.ThrowIfNull(value);
        this.RequireStringBytes(count, member, memberType);
        if (Codecs.BoundedTextCodec.IsUtf16(encoding) && (count & 1) != 0)
        {
            throw this.Fail(WriteFailures.Utf16CapacityOdd, member, memberType);
        }

        byte[] encoded;
        try
        {
            int length = Codecs.BoundedTextCodec.GetByteCount(encoding, value);
            if (length > count)
            {
                throw this.Fail(WriteFailures.BoundedTextTooLong(member, length, count), member, memberType);
            }

            encoded = Codecs.BoundedTextCodec.Encode(encoding, value);
        }
        catch (System.Text.EncoderFallbackException exception)
        {
            var failure = new CStructWriteException(WriteFailures.EncodingUnrepresentable, exception);
            this.Attach(failure, member, memberType);
            throw failure;
        }

        Span<byte> bytes = this.Reserve(count, member, memberType);
        encoded.CopyTo(bytes);
        bytes.Slice(encoded.Length).Clear();
    }

    /// <summary>Writes a pointer's stored address with the runtime's addressing rule (absolute, or relative to <see cref="Origin"/>; 0 stays null).</summary>
    /// <param name="address">The target's stream address.</param>
    /// <param name="pointerSize">The pointer width in bytes.</param>
    /// <param name="littleEndian">The layout's byte order.</param>
    /// <param name="member">The field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void WritePointerAddress(long address, int pointerSize, bool littleEndian, string member, string? memberType)
    {
        ulong stored;
        try
        {
            stored = Addressing.CStructPointerArithmetic.EncodeTargetAddress(address, this.options.AddressingMode, this.options.Origin, (byte)pointerSize);
        }
        catch (CStructWriteException exception)
        {
            this.Attach(exception, member, memberType);
            throw;
        }

        Codec.WriteUnsigned(this.Reserve(pointerSize, member, memberType), stored, littleEndian);
    }

    /// <summary>
    ///     Writes one value of a caller-supplied <see cref="ICustomCodec"/> as the runtime does: into a window that
    ///     grows while the codec asks for more room, up to <c>MaxStringBytes</c>, with the runtime's texts for a codec
    ///     that cannot encode the value, throws, or over-reports.
    /// </summary>
    /// <param name="codec">The codec, one of the instances the layout class provides.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="member">The field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void WriteCustom(ICustomCodec codec, object? value, string member, string? memberType)
    {
        ArgumentNullException.ThrowIfNull(codec);
        if (value is null)
        {
            throw this.Fail("Null is valid only for a scalar pointer field: " + member, member, memberType);
        }

        byte[] rented;
        int written;
        try
        {
            rented = Codecs.CustomCodecAdapter.EncodeToRented(codec, value, this.options.MaxStringBytes, out written);
        }
        catch (CStructException exception)
        {
            this.Attach(exception, member, memberType);
            throw;
        }

        try
        {
            rented.AsSpan(0, written).CopyTo(this.Reserve(written, member, memberType));
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    ///     Merges a bitfield's bits into its storage unit: the value is validated against the width with the
    ///     runtime's text, the unit's other bits are kept, and the position moves to the unit's end.
    /// </summary>
    /// <param name="slot">The unit and bit offset the placement chose.</param>
    /// <param name="bitSize">The field's width in bits.</param>
    /// <param name="value">The value to store (an integer, a bool, or an enum's raw bits).</param>
    /// <param name="littleEndian">The unit's byte order.</param>
    /// <param name="highBitFirst">Whether bits are allocated from the high end of the unit.</param>
    /// <param name="member">The bitfield, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void WriteBits(BitfieldSlot slot, int bitSize, object? value, bool littleEndian, bool highBitFirst, string member, string? memberType)
    {
        int unitBits = slot.UnitSize * 8;
        if (slot.BitOffset + bitSize > unitBits)
        {
            throw this.Fail("Bitfield exceeds its storage unit: " + member, member, memberType);
        }

        ulong bits;
        try
        {
            bits = Codec.ToBitfieldValue(member, bitSize, value);
        }
        catch (CStructWriteException exception)
        {
            this.Attach(exception, member, memberType);
            throw;
        }

        Span<byte> unit = this.Unit(slot.UnitStart, slot.UnitSize, member, memberType);
        ulong existing = Codec.ReadUnsigned(unit, littleEndian);
        ulong merged = Codec.MergeBits(existing, bits, Codec.BitfieldShift(slot.BitOffset, bitSize, unitBits, highBitFirst), bitSize);
        Codec.WriteUnsigned(unit, merged, littleEndian);
    }

    /// <summary>Notes the field a failure raised by a codec belongs to (the runtime's innermost-member rule) and returns it to throw.</summary>
    /// <param name="exception">The failure.</param>
    /// <param name="member">The field.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns><paramref name="exception"/>, with the member noted.</returns>
    public readonly CStructException WithMember(CStructException exception, string member, string? memberType)
    {
        ArgumentNullException.ThrowIfNull(exception);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>
    ///     Records the position an operation had reached when <paramref name="exception"/> left it, as the runtime does
    ///     at its operation boundary. Generated <c>Serialize</c> methods call this in their catch block.
    /// </summary>
    /// <param name="exception">The failure leaving the operation.</param>
    public readonly void Complete(CStructException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.AttachContext(this.path, this.position);
    }

    /// <summary>Copies the written bytes into a new array (the growable cursor's result).</summary>
    /// <returns>The bytes written, from the start to the furthest position.</returns>
    public readonly byte[] ToArray() => this.destination.Slice(0, this.length).ToArray();

    /// <summary>Returns a growable cursor's buffer to the pool; a cursor over a caller's span has nothing to release.</summary>
    public void Dispose()
    {
        if (this.owned is not null)
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(this.owned);
            this.owned = null;
            this.destination = default;
        }
    }

    private void Grow(int required)
    {
        int capacity = Math.Max(required, Math.Min(int.MaxValue / 2, this.destination.Length) * 2);
        byte[] larger = System.Buffers.ArrayPool<byte>.Shared.Rent(capacity);
        this.destination.Slice(0, this.length).CopyTo(larger);
        larger.AsSpan(this.length).Clear();
        System.Buffers.ArrayPool<byte>.Shared.Return(this.owned!);
        this.owned = larger;
        this.destination = larger;
    }

    /// <summary>Writes zero padding.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <param name="member">The layout field the padding belongs to, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    public void Pad(int count, string? member, string? memberType) => this.Reserve(count, member, memberType).Clear();

    /// <summary>Pads to the next multiple of <paramref name="alignment"/> measured from <paramref name="origin"/>.</summary>
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
        this.Pad((int)padding, member, memberType);
    }

    /// <summary>Enters a nested struct or union, enforcing <c>MaxNestingDepth</c>.</summary>
    /// <param name="member">The composite field being entered, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructWriteLimitException">The nesting limit is exceeded.</exception>
    public void EnterComposite(string member, string? memberType)
    {
        this.options.CancellationToken.ThrowIfCancellationRequested();
        if (this.nestingDepth >= this.options.MaxNestingDepth)
        {
            throw this.FailLimit(WriteFailures.NestingLimit, member, memberType);
        }

        this.nestingDepth++;
    }

    /// <summary>Leaves the current struct or union.</summary>
    public void ExitComposite() => this.nestingDepth--;

    /// <summary>Validates an array's element count against <c>MaxArrayElements</c>.</summary>
    /// <param name="count">The number of elements the caller supplies.</param>
    /// <param name="member">The array field, named in the message and the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructWriteLimitException">The count exceeds the limit.</exception>
    public readonly void RequireArrayLength(long count, string member, string? memberType)
    {
        if (count < 0 || count > this.options.MaxArrayElements)
        {
            throw this.FailLimit(WriteFailures.ArrayLengthLimit(member), member, memberType);
        }
    }

    /// <summary>Validates a string's encoded length (terminator included) against <c>MaxStringBytes</c>.</summary>
    /// <param name="count">The number of encoded bytes.</param>
    /// <param name="member">The string field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructWriteLimitException">The string exceeds the limit.</exception>
    public readonly void RequireStringBytes(long count, string member, string? memberType)
    {
        if (count < 0 || count > this.options.MaxStringBytes)
        {
            throw this.FailLimit(WriteFailures.StringBytesLimit, member, memberType);
        }
    }

    /// <summary>A write failure at the current position, with the runtime's context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructWriteException Fail(string message, string? member, string? memberType)
    {
        var exception = new CStructWriteException(message);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>A limit failure at the current position, with the runtime's context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructWriteLimitException FailLimit(string message, string? member, string? memberType)
    {
        var exception = new CStructWriteLimitException(message);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>
    ///     The failure of a value that cannot be encoded as its field: the runtime's text stating what was supplied
    ///     and what the field accepts (<c>Value 300 does not fit: uint8 accepts 0 to 255.</c>).
    /// </summary>
    /// <param name="value">The value the caller supplied.</param>
    /// <param name="typeSpelling">The field's type spelling.</param>
    /// <param name="acceptedRange">The integer range a fixed-width integer accepts (<c>0 to 255</c>), or <see langword="null"/>.</param>
    /// <param name="member">The layout field, for the diagnostics.</param>
    /// <param name="cause">The conversion exception, when one was raised.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructWriteException FailUnwritable(object? value, string typeSpelling, string? acceptedRange, string member, Exception? cause = null)
    {
        ArgumentNullException.ThrowIfNull(typeSpelling);
        string message = WriteFailures.UnwritableValue(value, typeSpelling, acceptedRange);
        var exception = cause is null ? new CStructWriteException(message) : new CStructWriteException(message, cause);
        this.Attach(exception, member, typeSpelling);
        return exception;
    }

    /// <summary>
    ///     The failure of a layout expression evaluated by generated code during a write: the runtime's
    ///     <c>Cannot evaluate {context}: {reason}</c> text around an exception one of the <see cref="Expressions"/>
    ///     operators raised.
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

        var failure = new CStructWriteException(LayoutExpressionEvaluator.DescribeFailure(context, exception), exception);
        this.Attach(failure, member, memberType);
        return failure;
    }

    private static System.Text.Encoding StrictEncoding(TerminatedTextEncoding encoding)
    {
        return encoding switch
        {
            TerminatedTextEncoding.Ascii => Codecs.PrimitiveCodecs.StrictAsciiEncoding,
            TerminatedTextEncoding.Utf8 => Codecs.PrimitiveCodecs.StrictUtf8Encoding,
            TerminatedTextEncoding.Utf16LittleEndian => Codecs.PrimitiveCodecs.StrictUtf16LittleEndianEncoding,
            _ => Codecs.PrimitiveCodecs.StrictUtf16BigEndianEncoding,
        };
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
}
