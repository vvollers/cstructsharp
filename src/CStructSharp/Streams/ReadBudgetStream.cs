namespace CStructSharp.Streams;

using System;
using System.IO;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>
///     Counts bytes read through a caller-owned stream and enforces one read-like operation's byte budget. When the
///     source is memory (a pinned region or an exposable <see cref="MemoryStream"/>) it also acts as the operation's
///     read cursor: the position lives here, fixed-width values are served straight from memory, and the inner
///     stream's position is written back once by <see cref="FlushPosition"/>.
/// </summary>
/// <remarks>
///     The budget and the memory mode live in a <see cref="MemoryReadCore"/>, which the engine's memory cursor also
///     reads through, so both charge, bound and word their failures identically. This class adds the stream mode:
///     delegation to the inner stream with its failures translated into <see cref="CStructReadException"/>. The reads
///     over caller memory go straight to the engine's memory cursor; the memory mode here serves <c>Update</c>, whose path
///     walk, layout captures and sparse staging read a span or an exposable memory stream through this stream.
/// </remarks>
internal sealed class ReadBudgetStream : Stream
{
    private readonly Stream inner;

    // Mutated through this field only: a copy would lose the position and the charges.
    private MemoryReadCore core;

    /// <summary>Wraps a readable stream without taking ownership of it.</summary>
    /// <param name="inner">The caller-owned source stream; it is never disposed by this wrapper.</param>
    /// <param name="options">The read options supplying the per-string and total byte budgets.</param>
    /// <exception cref="ArgumentNullException">
    ///     <paramref name="inner"/> or <paramref name="options"/> is null.
    /// </exception>
    public ReadBudgetStream(Stream inner, ReadOptions options)
        : this(inner, (options ?? throw new ArgumentNullException(nameof(options))).MaxStringBytes, options.MaxTotalBytesRead)
    {
    }

    /// <summary>Wraps a readable stream using operation-owned limit values.</summary>
    /// <param name="inner">The caller-owned source stream; it is never disposed by this wrapper.</param>
    /// <param name="maxStringBytes">The largest number of encoded bytes one string may consume.</param>
    /// <param name="maxTotalBytesRead">The largest number of bytes the whole operation may read.</param>
    public ReadBudgetStream(Stream inner, long maxStringBytes, long maxTotalBytesRead)
        : this(inner, maxStringBytes, maxTotalBytesRead, default)
    {
    }

    /// <summary>
    ///     Wraps a readable stream using operation-owned limit values and a cancellation token. A pinned
    ///     <see cref="FixedBufferStream"/> region or an exposable <see cref="MemoryStream"/> buffer is read directly
    ///     from memory, starting at the inner stream's current position; any other stream is read through delegation.
    /// </summary>
    /// <param name="inner">The caller-owned source stream; it is never disposed by this wrapper.</param>
    /// <param name="maxStringBytes">The largest number of encoded bytes one string may consume.</param>
    /// <param name="maxTotalBytesRead">The largest number of bytes the whole operation may read.</param>
    /// <param name="cancellationToken">The operation's token, exposed through <see cref="CancellationToken"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is null.</exception>
    public ReadBudgetStream(Stream inner, long maxStringBytes, long maxTotalBytesRead, System.Threading.CancellationToken cancellationToken)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.MaxStringBytes = maxStringBytes;
        this.CancellationToken = cancellationToken;
        this.core = MemoryReadCore.Over(inner, maxTotalBytesRead);
    }

    /// <summary>Gets the configured per-string encoded-byte budget.</summary>
    public long MaxStringBytes { get; }

    /// <summary>The operation's token, checked per chunk of a terminated string and per block of a primitive array.</summary>
    public System.Threading.CancellationToken CancellationToken { get; }

    /// <summary>Gets a value indicating whether the inner stream can be read.</summary>
    public override bool CanRead => this.inner.CanRead;

    /// <summary>Gets a value indicating whether the inner stream supports seeking.</summary>
    public override bool CanSeek => this.inner.CanSeek;

    /// <summary>Gets a value indicating whether writing is supported; always false for this wrapper.</summary>
    public override bool CanWrite => false;

    /// <summary>
    ///     Gets the source length in bytes: the memory region or buffer length when memory-backed, otherwise the inner
    ///     stream's length.
    /// </summary>
    /// <exception cref="CStructReadException">The inner stream failed to report its length.</exception>
    public override long Length
    {
        get
        {
            if (this.core.IsMemoryBacked)
            {
                return this.core.KnownLength;
            }

            try
            {
                return this.inner.Length;
            }
            catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
            {
                throw this.CreateReadFailure("Cannot read the source stream length.", exception);
            }
        }
    }

    /// <summary>
    ///     Gets or sets the read position in bytes from the start of the source. Memory-backed sources keep the
    ///     position here until <see cref="FlushPosition"/>; stream sources use the inner stream's position directly.
    /// </summary>
    /// <remarks>
    ///     Every source has one rule: the position may reach the end of the input but not pass it, because the input
    ///     then provably lacks bytes a layout places there - aligned padding between fields or after the last one is
    ///     part of a struct's storage. A pinned region, a memory stream and a seekable stream all report
    ///     <see cref="ReadFailures.OutsideRegion"/> for it, so a read fails identically whatever the source.
    /// </remarks>
    /// <exception cref="CStructReadException">
    ///     The position is negative, lies past the end of the input, or the inner stream failed.
    /// </exception>
    public override long Position
    {
        get
        {
            if (this.core.IsMemoryBacked)
            {
                return this.core.Position;
            }

            try
            {
                return this.inner.Position;
            }
            catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
            {
                throw this.CreateReadFailure("Cannot read the source stream position.", exception);
            }
        }

        set
        {
            if (this.core.IsMemoryBacked)
            {
                this.core.SetPosition(value);
                return;
            }

            this.RejectPastStreamEnd(value);
            try
            {
                this.inner.Position = value;
            }
            catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
            {
                throw this.CreateReadFailure("Cannot change the source stream position.", exception);
            }
        }
    }

    /// <summary>
    ///     Reports whether the source provably cannot supply <paramref name="count"/> more bytes: the region or seekable
    ///     stream ends before them. Only a definite shortfall returns <see langword="true"/>; a non-seekable stream, or
    ///     one whose length cannot be read, returns <see langword="false"/> so the caller reads and lets the ordinary
    ///     short-read failure decide. Lets a bulk reader fail before it allocates for a count the data cannot back.
    /// </summary>
    /// <param name="count">The nonnegative number of requested bytes.</param>
    /// <returns>Whether the remaining source length proves a shortfall, without consuming bytes.</returns>
    public bool IsShortBy(long count)
    {
        if (this.core.IsMemoryBacked)
        {
            return this.core.IsShortBy(count);
        }

        try
        {
            return this.inner.CanSeek && this.inner.Length - this.inner.Position < count;
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            return false;
        }
    }

    /// <summary>
    ///     Serves <paramref name="count"/> bytes straight from memory at the current position, advancing and charging
    ///     the budget exactly like a read would. False when the source is a stream or the bytes are not all available,
    ///     in which case the caller reads the bytes through the stream instead, which reports a shortage with the
    ///     short-read text.
    /// </summary>
    /// <param name="count">The nonnegative number of requested bytes.</param>
    /// <param name="bytes">The borrowed source span on success, or an empty span on failure.</param>
    /// <returns>Whether the bytes were available in memory; false leaves the position and budget unchanged.</returns>
    /// <exception cref="CStructReadLimitException">The bytes exceed the total read budget (they are consumed).</exception>
    public bool TryReadSpan(int count, out ReadOnlySpan<byte> bytes) => this.core.TryReadSpan(count, out bytes);

    /// <summary>
    ///     The bytes from the current position to the end of a memory-backed source, without consuming or charging
    ///     them; false for a stream source. Pair with <see cref="Advance"/> once the consumer knows how many it used.
    /// </summary>
    /// <param name="bytes">The borrowed remaining bytes (at most <see cref="int.MaxValue"/>), or an empty span.</param>
    /// <returns>Whether the source is memory-backed and the remaining bytes were exposed.</returns>
    public bool TryPeekRemaining(out ReadOnlySpan<byte> bytes) => this.core.TryPeekRemaining(out bytes);

    /// <summary>Consumes <paramref name="count"/> bytes that <see cref="TryPeekRemaining"/> exposed, charging them like a read.</summary>
    /// <param name="count">The number of bytes consumed from the peeked span.</param>
    /// <exception cref="CStructReadLimitException">The advance exceeds the operation's total read budget.</exception>
    public void Advance(int count) => this.core.Advance(count);

    /// <summary>
    ///     Like <see cref="TryReadSpan"/> but also fails, without charging or throwing, when the read would exceed the
    ///     total read budget - so a caller can fall back to a path that reports the limit failure at its usual place.
    /// </summary>
    /// <param name="count">The nonnegative number of requested bytes.</param>
    /// <param name="bytes">The borrowed source span on success, or an empty span on failure.</param>
    /// <returns>Whether the bytes were available within the budget; false consumes neither bytes nor budget.</returns>
    public bool TryReadSpanWithinBudget(int count, out ReadOnlySpan<byte> bytes) => this.core.TryReadSpanWithinBudget(count, out bytes);

    /// <summary>
    ///     Stream-source counterpart of <see cref="TryReadSpanWithinBudget"/>: fills <paramref name="destination"/> with the
    ///     next <c>destination.Length</c> bytes when the seekable source provably holds them and the budget allows,
    ///     charging the budget exactly as a sequence of reads would; false (nothing consumed) otherwise.
    /// </summary>
    /// <param name="destination">The span to fill if the remaining source and byte budget both suffice.</param>
    /// <returns>Whether the complete block was read; a failed preflight leaves the source and destination unchanged.</returns>
    public bool TryReadBlockWithinBudget(Span<byte> destination)
    {
        if (this.core.IsMemoryBacked || destination.Length == 0)
        {
            return false;
        }

        long available;
        try
        {
            available = this.inner.CanSeek ? this.inner.Length - this.inner.Position : -1;
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            return false;
        }

        if (available < destination.Length || !this.core.IsWithinBudget(destination.Length))
        {
            return false;
        }

        BinaryPrimitiveIO.ReadExactlyOrThrow(this, destination);
        return true;
    }

    /// <summary>Writes the memory-mode position back to the inner stream; a no-op for stream sources.</summary>
    public void FlushPosition()
    {
        if (this.core.IsMemoryBacked)
        {
            this.inner.Position = this.core.Position;
        }
    }

    /// <summary>Flushes the inner stream; the budget and the memory-mode position are unaffected.</summary>
    /// <exception cref="CStructReadException">The inner stream failed to flush.</exception>
    public override void Flush()
    {
        try
        {
            this.inner.Flush();
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateReadFailure("Cannot flush the source stream.", exception);
        }
    }

    /// <summary>Reads bytes while charging the operation-wide budget only for bytes actually returned.</summary>
    /// <param name="buffer">The array that receives the bytes.</param>
    /// <param name="offset">The zero-based index in <paramref name="buffer"/> at which to start storing bytes.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes read, which is 0 at the end of the source.</returns>
    /// <exception cref="CStructReadLimitException">The read exceeds the operation's total read budget.</exception>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (this.core.IsMemoryBacked)
        {
            StreamArgumentValidation.ValidateRange(buffer, offset, count, nameof(buffer));
            return this.core.Read(buffer.AsSpan(offset, count));
        }

        int read;
        try
        {
            read = this.inner.Read(buffer, offset, count);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateReadFailure("Cannot read from the source stream.", exception);
        }

        this.core.Charge(read);
        return read;
    }

    /// <summary>Reads span data while charging the operation-wide budget only for bytes actually returned.</summary>
    /// <param name="buffer">The span that receives up to its length in bytes.</param>
    /// <returns>The number of bytes copied into the span, which is 0 at the end of the source.</returns>
    /// <exception cref="CStructReadLimitException">The read exceeds the operation's total read budget.</exception>
    public override int Read(Span<byte> buffer)
    {
        if (this.core.IsMemoryBacked)
        {
            return this.core.Read(buffer);
        }

        int read;
        try
        {
            read = this.inner.Read(buffer);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateReadFailure("Cannot read from the source stream.", exception);
        }

        this.core.Charge(read);
        return read;
    }

    /// <summary>Reads one byte while applying the same budget as bulk reads.</summary>
    /// <returns>The byte value from 0 to 255, or -1 at the end of the source.</returns>
    public override int ReadByte()
    {
        if (this.core.IsMemoryBacked)
        {
            return this.core.ReadByte();
        }

        int value;
        try
        {
            value = this.inner.ReadByte();
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateReadFailure("Cannot read from the source stream.", exception);
        }

        if (value >= 0)
        {
            this.core.Charge(1);
        }

        return value;
    }

    /// <summary>Seeks in the wrapped stream without resetting the total physical-read budget.</summary>
    /// <param name="offset">The byte offset relative to <paramref name="origin"/>.</param>
    /// <param name="origin">The reference point: the source start, the current position, or the source end.</param>
    /// <returns>The new position in bytes from the start of the source.</returns>
    /// <exception cref="CStructReadException">The target position is invalid or the inner stream failed.</exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (this.core.IsMemoryBacked)
        {
            return this.core.Seek(offset, origin);
        }

        if (this.inner.CanSeek)
        {
            // A seek obeys the position setter's end-of-input rule.
            this.RejectPastStreamEnd(origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => checked(this.Position + offset),
                SeekOrigin.End => checked(this.Length + offset),
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            });
        }

        try
        {
            return this.inner.Seek(offset, origin);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateReadFailure("Cannot seek in the source stream.", exception);
        }
    }

    /// <summary>
    ///     Rejects a stream position past the end of a seekable stream's input with <see cref="ReadFailures.OutsideRegion"/>,
    ///     as a memory source rejects it. The length is queried only for a position beyond every length seen so far, so
    ///     a stream that has grown since is measured again before the position is rejected.
    /// </summary>
    /// <param name="value">The requested position.</param>
    /// <exception cref="CStructReadException">The position lies past the end, or the length cannot be read.</exception>
    private void RejectPastStreamEnd(long value)
    {
        if (value <= this.core.KnownLength || !this.inner.CanSeek)
        {
            return;
        }

        this.core.KnownLength = this.Length;
        if (value > this.core.KnownLength)
        {
            throw new CStructReadException(ReadFailures.OutsideRegion);
        }
    }

    /// <summary>Rejects length changes because this wrapper advertises a read-only stream contract.</summary>
    /// <param name="value">The requested length, which is ignored.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void SetLength(long value)
    {
        throw new NotSupportedException("The parse budget stream is read-only.");
    }

    /// <summary>Rejects writes because this wrapper is only used by parse operations.</summary>
    /// <param name="buffer">The source array, which is ignored.</param>
    /// <param name="offset">The start index in the source array, which is ignored.</param>
    /// <param name="count">The number of bytes to write, which is ignored.</param>
    /// <exception cref="NotSupportedException">Always thrown.</exception>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("The parse budget stream is read-only.");
    }

    /// <summary>Leaves the caller-owned stream open when parser state is released.</summary>
    /// <param name="disposing">Whether the call comes from <see cref="Stream.Dispose()"/>; nothing is released.</param>
    protected override void Dispose(bool disposing)
    {
        // Intentionally do not dispose this.inner; public CStruct methods do not take stream ownership.
    }

    /// <summary>Creates a physical source error and records its position when the stream can still report it.</summary>
    private CStructReadException CreateReadFailure(string message, Exception exception)
    {
        var result = new CStructReadException(message, exception);
        result.AttachContext(offset: this.TryGetPosition());
        return result;
    }

    /// <summary>Obtains diagnostic position context without allowing a secondary stream failure to hide the first.</summary>
    private long? TryGetPosition()
    {
        if (this.core.IsMemoryBacked)
        {
            return this.core.Position;
        }

        try
        {
            return this.inner.Position;
        }
        catch (Exception)
        {
            // Preserve the physical read failure even if the stream can no longer report diagnostic context.
            return null;
        }
    }
}
