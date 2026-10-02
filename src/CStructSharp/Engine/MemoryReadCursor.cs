namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Streams;

/// <summary>
///     The cursor over input in memory: a pinned region (span, <see cref="byte"/> array, <see cref="ReadOnlyMemory{T}"/>,
///     single-segment sequence) or the exposed buffer of a caller's <see cref="MemoryStream"/>. Reading, bounds and
///     budget charges go through <see cref="MemoryReadCore"/>, which a memory-backed <see cref="ReadBudgetStream"/> also
///     uses, and arrays, text and short reads run the shared readers, so a value read from memory or from a
///     memory-backed stream consumes, charges and fails the same way.
/// </summary>
/// <remarks>
///     A plain mutable struct (not a <see langword="ref"/> struct, so it also works where the executor stores it):
///     pass it by reference and never copy it mid-operation. A region must stay pinned until the operation ends. A
///     cursor over a caller's stream leaves that stream's position alone until <see cref="FlushPosition"/>. A region
///     that holds only the first part of a buffered input (<see cref="BufferedInput"/>) raises
///     <see cref="BufferedInputShortfallException"/> wherever the read needs a byte past it.
/// </remarks>
internal unsafe struct MemoryReadCursor : IReadCursor, ITextReadSource
{
    private readonly Stream? owner;
    private readonly long maxStringBytes;
    private readonly CancellationToken cancellationToken;

    // Mutated through this field only: a copy would lose the position and the charges.
    private MemoryReadCore core;

    /// <summary>Creates a cursor over a region its owner keeps pinned until the operation ends.</summary>
    /// <param name="region">The input's byte 0.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="position">The starting position in bytes from byte 0.</param>
    /// <param name="maxStringBytes">The largest number of encoded bytes one string may consume.</param>
    /// <param name="maxTotalBytesRead">The largest number of bytes the whole operation may read.</param>
    /// <param name="cancellationToken">The operation's token.</param>
    /// <param name="continuedLength">
    ///     <see cref="BufferedInput.WholeInput"/> when the region is the whole input; otherwise the whole input's length
    ///     in bytes or <see cref="BufferedInput.UnknownLength"/> (<see cref="ReadOptions.ContinuedInputLength"/>).
    /// </param>
    public MemoryReadCursor(byte* region, long length, long position, long maxStringBytes, long maxTotalBytesRead, CancellationToken cancellationToken, long continuedLength = BufferedInput.WholeInput)
        : this(MemoryReadCore.OverRegion(region, length, position, maxTotalBytesRead), null, maxStringBytes, cancellationToken)
    {
        // The partly buffered core is made in place, out of line: a whole-input read pays only this comparison.
        if (continuedLength != BufferedInput.WholeInput)
        {
            this.core.ContinueAsPartOf(continuedLength);
        }
    }

    /// <summary>Creates a cursor over part of an array.</summary>
    /// <param name="array">The array holding the input.</param>
    /// <param name="offset">The index of the input's byte 0 within <paramref name="array"/>.</param>
    /// <param name="length">The input length in bytes; the input ends within the array.</param>
    /// <param name="position">The starting position in bytes from byte 0.</param>
    /// <param name="maxStringBytes">The largest number of encoded bytes one string may consume.</param>
    /// <param name="maxTotalBytesRead">The largest number of bytes the whole operation may read.</param>
    /// <param name="cancellationToken">The operation's token.</param>
    public MemoryReadCursor(byte[] array, int offset, long length, long position, long maxStringBytes, long maxTotalBytesRead, CancellationToken cancellationToken)
        : this(MemoryReadCore.OverArray(array, offset, length, position, maxTotalBytesRead), null, maxStringBytes, cancellationToken)
    {
    }

    /// <summary>Wraps a memory-backed core.</summary>
    /// <param name="core">The core, memory-backed.</param>
    /// <param name="owner">The caller's stream whose position <see cref="FlushPosition"/> writes, or null.</param>
    /// <param name="maxStringBytes">The largest number of encoded bytes one string may consume.</param>
    /// <param name="cancellationToken">The operation's token.</param>
    private MemoryReadCursor(MemoryReadCore core, Stream? owner, long maxStringBytes, CancellationToken cancellationToken)
    {
        this.core = core;
        this.owner = owner;
        this.maxStringBytes = maxStringBytes;
        this.cancellationToken = cancellationToken;
    }

    /// <inheritdoc/>
    public long Position
    {
        readonly get => this.core.Position;
        set => this.core.SetPosition(value);
    }

    /// <inheritdoc/>
    /// <exception cref="BufferedInputShortfallException">Only part of the input is buffered and its length is unknown.</exception>
    public readonly long Length => this.core.InputLength;

    /// <inheritdoc/>
    public readonly long MaxStringBytes => this.maxStringBytes;

    /// <inheritdoc cref="IReadCursor.CancellationToken"/>
    public readonly CancellationToken CancellationToken => this.cancellationToken;

    /// <inheritdoc/>
    readonly long? ITextReadSource.StringByteLimit => this.maxStringBytes;

    /// <inheritdoc/>
    readonly long ITextReadSource.RemainingReadBudget => this.core.RemainingBudget;

    /// <summary>
    ///     Creates a cursor over <paramref name="source"/>'s memory when <see cref="ReadBudgetStream"/> would read it
    ///     from memory: a read-only pinned region stream or a seekable <see cref="MemoryStream"/> whose buffer is
    ///     exposed. The cursor starts at the stream's position; <see cref="FlushPosition"/> writes the final one back.
    /// </summary>
    /// <param name="source">The caller's stream; it must stay open, and its region pinned, until the operation ends.</param>
    /// <param name="maxStringBytes">The largest number of encoded bytes one string may consume.</param>
    /// <param name="maxTotalBytesRead">The largest number of bytes the whole operation may read.</param>
    /// <param name="cancellationToken">The operation's token.</param>
    /// <param name="cursor">The cursor, or a default value when the method returns <see langword="false"/>.</param>
    /// <returns>Whether the stream's input is in memory the cursor can read.</returns>
    public static bool TryCreate(Stream source, long maxStringBytes, long maxTotalBytesRead, CancellationToken cancellationToken, out MemoryReadCursor cursor)
    {
        MemoryReadCore core = MemoryReadCore.Over(source, maxTotalBytesRead);
        cursor = core.IsMemoryBacked ? new MemoryReadCursor(core, source, maxStringBytes, cancellationToken) : default;
        return core.IsMemoryBacked;
    }

    /// <inheritdoc/>
    public readonly bool EndsAtOrBefore(long address) => this.core.EndsAtOrBefore(address);

    /// <inheritdoc/>
    public readonly void ThrowIfCancellationRequested() => this.cancellationToken.ThrowIfCancellationRequested();

    /// <inheritdoc/>
    public void Charge(long count) => this.core.Charge(count);

    /// <inheritdoc/>
    /// <remarks>The scan runs over the input in place; the position does not move.</remarks>
    public readonly int ScanTerminated(int elementSize, int maximumElements, string fieldName)
    {
        // A memory input always exposes its remaining bytes, since the position never passes the end (a partly
        // buffered input raises the buffered-input signal instead, and the scan raises it when it runs off the buffer).
        _ = this.core.TryPeekRemaining(out ReadOnlySpan<byte> remaining);
        return DynamicArrayExtent.ScanSpan(remaining, elementSize, maximumElements, this.core.RemainingBudget, fieldName, this.core.IsPartial ? this.core.Position : -1);
    }

    /// <inheritdoc/>
    public void Skip(long count) => this.core.SetPosition(checked(this.core.Position + count));

    /// <inheritdoc/>
    public void Align(long origin, int alignment)
        => this.core.SetPosition(origin + LayoutMath.AlignUp(this.core.Position - origin, alignment));

    /// <inheritdoc/>
    public readonly bool IsShortBy(long count) => this.core.IsShortBy(count);

    /// <inheritdoc/>
    public bool TryReadSpan(int count, out ReadOnlySpan<byte> bytes) => this.core.TryReadSpan(count, out bytes);

    /// <inheritdoc/>
    public bool TryReadSpanWithinBudget(int count, out ReadOnlySpan<byte> bytes) => this.core.TryReadSpanWithinBudget(count, out bytes);

    /// <inheritdoc/>
    /// <remarks>A memory source is never staged into a block, as <see cref="ReadBudgetStream.TryReadBlockWithinBudget"/> declines it too.</remarks>
    public readonly bool TryReadBlockWithinBudget(Span<byte> destination) => false;

    /// <inheritdoc/>
    public readonly bool TryPeekRemaining(out ReadOnlySpan<byte> bytes) => this.core.TryPeekRemaining(out bytes);

    /// <inheritdoc/>
    public void Advance(int count) => this.core.Advance(count);

    /// <inheritdoc cref="IReadCursor.Read"/>
    public int Read(byte[] buffer, int offset, int count) => this.core.Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public object? ReadCustom(ICustomCodec codec)
    {
        // A memory input always exposes its remaining bytes (the position never passes the end), so the codec is
        // handed the whole remainder in place rather than a doubling window.
        _ = this.core.TryPeekRemaining(out ReadOnlySpan<byte> remaining);
        if (this.core.IsPartial)
        {
            return this.ReadCustomFromPartialInput(codec, remaining);
        }

        return CustomCodecAdapter.ReadInMemory(codec, ref this, remaining);
    }

    /// <inheritdoc/>
    public byte ReadByteExactly()
    {
        // One byte through the core; at the end of the input the failure is the one-byte short-read text, with no
        // inner exception.
        int value = this.core.ReadByte();
        if (value < 0)
        {
            throw new CStructReadException(ReadFailures.ShortRead(1, 0));
        }

        return (byte)value;
    }

    /// <inheritdoc/>
    public void ReadExactly(Span<byte> destination)
    {
        // The bytes that are there are copied and charged first (so the budget can fail before the shortage); a
        // shortage then reports what was available at the item's start and leaves the position at the end of the input.
        long start = this.core.Position;
        try
        {
            this.ReadAvailableExactly(destination);
        }
        catch (EndOfStreamException exception)
        {
            this.core.SetPosition(start);
            string message = ReadFailures.ShortRead(destination.Length, Math.Max(0, this.core.InputLength - this.core.Position));
            this.core.SetPosition(this.core.InputLength);
            throw new CStructReadException(message, exception);
        }
    }

    /// <inheritdoc/>
    public void ReadExactlyOrEndOfStream(Span<byte> destination) => this.ReadAvailableExactly(destination);

    /// <inheritdoc/>
    public ReadOnlySpan<byte> ReadFixed(Span<byte> scratch)
    {
        if (this.core.TryReadSpan(scratch.Length, out ReadOnlySpan<byte> direct))
        {
            return direct;
        }

        if (scratch.Length == 1)
        {
            scratch[0] = this.ReadByteExactly();
        }
        else
        {
            this.ReadExactly(scratch);
        }

        return scratch;
    }

    /// <inheritdoc/>
    public IList<object?> ReadPrimitiveArray(PrimitiveCodec codec, int count) => PrimitiveArrayReader.Read(ref this, codec, count);

    /// <inheritdoc/>
    public string ReadTerminatedString(Encoding encoding, char terminator)
        => this.TryReadTerminatedInPlace(encoding, terminator, out string? text) ? text : PrimitiveCodecs.ReadIntoString(ref this, encoding, terminator);

    /// <inheritdoc/>
    public string ReadBoundedText(int byteCount, string type) => PrimitiveCodecs.ReadBoundedText(ref this, byteCount, type);

    /// <inheritdoc/>
    public readonly void FlushPosition()
    {
        if (this.owner is not null)
        {
            this.owner.Position = this.core.Position;
        }
    }

    /// <inheritdoc/>
    void ITextReadSource.ReadExactly(Span<byte> buffer) => this.ReadAvailableExactly(buffer);

    /// <inheritdoc/>
    void ITextReadSource.Rewind(int count)
    {
        this.core.SetPosition(this.core.Position - count);
        this.core.Refund(count);
    }

    /// <summary>
    ///     Raises the runtime's own <see cref="EndOfStreamException"/>, the inner exception a stream's
    ///     <see cref="Stream.ReadExactly(Span{byte})"/> produces: asking an empty stream for one byte throws exactly that,
    ///     so a short memory read carries the same inner type and text as a short stream read.
    /// </summary>
    /// <exception cref="EndOfStreamException">Always.</exception>
    private static void ThrowEndOfStream()
    {
        Stream.Null.ReadExactly(stackalloc byte[1]);
        throw new UnreachableException("Stream.Null supplied a byte.");
    }

    /// <summary>
    ///     Decodes a custom value from the buffered part of a partly buffered input: an answer that may rest on where the
    ///     buffered part ends (<see cref="CustomCodecAdapter.MayDependOnWindowEnd"/>) - a short read, a rejection, a value
    ///     that took every buffered byte, a fixed size that runs past them - raises the buffered-input signal before
    ///     anything is consumed, so the rerun over more of the input decides the outcome as the span form does.
    /// </summary>
    /// <param name="codec">The codec.</param>
    /// <param name="remaining">The buffered bytes from the value's start.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="BufferedInputShortfallException">The codec needs bytes past the buffered part.</exception>
    /// <exception cref="CStructReadException">The codec rejects the bytes, throws, or reports an impossible length.</exception>
    /// <exception cref="CStructReadLimitException">The advance exceeds the total read budget.</exception>
    private object ReadCustomFromPartialInput(ICustomCodec codec, ReadOnlySpan<byte> remaining)
    {
        CStructReadException? failure = CustomCodecAdapter.DecodeFromMemory(codec, remaining, out object? value, out int consumed);
        if (CustomCodecAdapter.MayDependOnWindowEnd(codec, failure, consumed, remaining.Length))
        {
            // The codec is promised the whole remaining input: an answer that may rest on where the buffered part
            // ends (a short read, a rejection, a value that took the whole window) is decided over more of it.
            this.core.RequireBuffered(this.core.Position, this.core.Position + remaining.Length + 1);
        }

        this.core.Advance(consumed);
        if (failure is not null)
        {
            throw failure;
        }

        return value!;
    }

    /// <summary>
    ///     Reads a terminated string straight from memory when its outcome is known to be a success: the terminator is in
    ///     the input, the text up to it is within <c>MaxStringBytes</c> and valid, the token is not cancelled, and the
    ///     string and its terminator are within the read budget. The result, the charge (the bytes through the terminator,
    ///     as <see cref="PrimitiveCodecs.ReadIntoString{TSource}"/> charges them) and the final position (just after the
    ///     terminator) are then that reader's. Otherwise nothing is consumed or charged, and the caller runs the chunked
    ///     reader, which reports the failure where it always has.
    /// </summary>
    /// <param name="encoding">The strict encoding.</param>
    /// <param name="terminator">The terminating character.</param>
    /// <param name="text">The text without its terminator, when the method returns <see langword="true"/>.</param>
    /// <returns>Whether the string was read.</returns>
    private bool TryReadTerminatedInPlace(Encoding encoding, char terminator, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? text)
    {
        text = null;
        if (this.cancellationToken.IsCancellationRequested || !this.core.TryPeekRemaining(out ReadOnlySpan<byte> remaining))
        {
            return false;
        }

        int unitSize = encoding is UnicodeEncoding ? 2 : 1;
        Span<byte> terminatorBytes = stackalloc byte[4];
        int terminatorLength = PrimitiveCodecs.EncodeTerminator(encoding, terminator, terminatorBytes);
        if (!PrimitiveCodecs.TryReadWholeTerminated(remaining, encoding, unitSize, terminatorBytes[..terminatorLength], this.maxStringBytes, this.core.RemainingBudget, out text, out int consumed))
        {
            return false;
        }

        // The string and its terminator fit the budget, so consuming them cannot fail.
        this.core.Advance(consumed);
        return true;
    }

    /// <summary>Reads exactly <c>buffer.Length</c> bytes as <see cref="Stream.ReadExactly(Span{byte})"/> does over the memory-mode read.</summary>
    /// <param name="buffer">The destination; its length is the number of bytes.</param>
    /// <exception cref="EndOfStreamException">The input ends first; the bytes that were there are consumed and charged.</exception>
    private void ReadAvailableExactly(Span<byte> buffer)
    {
        // Stream.ReadExactly's loop over the memory-mode Read: in memory the first read takes every available byte.
        int total = 0;
        while (total < buffer.Length)
        {
            int read = this.core.Read(buffer[total..]);
            if (read == 0)
            {
                ThrowEndOfStream();
            }

            total += read;
        }
    }
}
