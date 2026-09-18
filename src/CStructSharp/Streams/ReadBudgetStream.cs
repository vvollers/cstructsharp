namespace CStructSharp.Streams;

using System;
using System.IO;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>
///     Counts bytes read through a caller-owned stream and enforces one read-like operation's byte budget. When the
///     source is memory (a pinned region or an exposable <see cref="MemoryStream"/>) it also acts as the operation's
///     read cursor: the position lives here, fixed-width values are served straight from memory, and the inner
///     stream's position is written back once by <see cref="FlushPosition"/> (E2.1).
/// </summary>
internal sealed unsafe class ReadBudgetStream : Stream
{
    private readonly Stream inner;
    private readonly long maxTotalBytesRead;
    private readonly byte* memoryPointer;
    private readonly byte[]? memoryArray;
    private readonly int memoryArrayOffset;
    private readonly long memoryLength;
    private readonly bool memoryBacked;
    private readonly bool memoryIsBoundedRegion;
    private long bytesRead;
    private long position;

    /// <summary>Wraps a readable stream without taking ownership of it.</summary>
    public ReadBudgetStream(Stream inner, ReadOptions options)
        : this(inner, (options ?? throw new ArgumentNullException(nameof(options))).MaxStringBytes, options.MaxTotalBytesRead)
    {
    }

    /// <summary>Wraps a readable stream using operation-owned limit values.</summary>
    public ReadBudgetStream(Stream inner, long maxStringBytes, long maxTotalBytesRead)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.maxTotalBytesRead = maxTotalBytesRead;
        this.MaxStringBytes = maxStringBytes;

        // Exactly one memory backing is chosen; every other source keeps the delegating stream path.
        if (inner is FixedBufferStream fixedBuffer && fixedBuffer.TryGetReadOnlyRegion(out byte* region, out long regionLength))
        {
            this.memoryPointer = region;
            this.memoryLength = regionLength;
            this.memoryBacked = true;
            this.memoryIsBoundedRegion = true;
            this.position = fixedBuffer.Position;
        }
        else if (inner is MemoryStream memoryStream && memoryStream.CanSeek && memoryStream.TryGetBuffer(out ArraySegment<byte> segment))
        {
            this.memoryArray = segment.Array;
            this.memoryArrayOffset = segment.Offset;
            this.memoryLength = memoryStream.Length;
            this.memoryBacked = true;
            this.position = memoryStream.Position;
        }
    }

    /// <summary>Gets the configured per-string encoded-byte budget.</summary>
    public long MaxStringBytes { get; }

    public override bool CanRead => this.inner.CanRead;

    public override bool CanSeek => this.inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            if (this.memoryBacked)
            {
                return this.memoryLength;
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

    public override long Position
    {
        get
        {
            if (this.memoryBacked)
            {
                return this.position;
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
            if (this.memoryBacked)
            {
                // Mirror the backing stream's own rules: a pinned region rejects positions outside it (as
                // FixedBufferStream does); a MemoryStream accepts any non-negative position.
                if (value < 0 || (this.memoryIsBoundedRegion && value > this.memoryLength))
                {
                    throw new CStructReadException("The requested position is outside the supplied memory region.");
                }

                this.position = value;
                return;
            }

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

    /// <summary>Flushes the wrapped stream without closing or otherwise taking ownership of it.</summary>
    /// <summary>
    ///     Reports whether the source provably cannot supply <paramref name="count"/> more bytes: the region or seekable
    ///     stream ends before them. Only a definite shortfall returns <see langword="true"/>; a non-seekable stream, or
    ///     one whose length cannot be read, returns <see langword="false"/> so the caller reads and lets the ordinary
    ///     short-read failure decide. Lets a bulk reader fail before it allocates for a count the data cannot back.
    /// </summary>
    public bool IsShortBy(long count)
    {
        if (this.memoryBacked)
        {
            return this.position + count > this.memoryLength;
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
    ///     in which case the caller takes the ordinary stream path (which then produces the usual short-read error).
    /// </summary>
    public bool TryReadSpan(int count, out ReadOnlySpan<byte> bytes)
    {
        if (this.memoryBacked && this.position + count <= this.memoryLength)
        {
            bytes = this.memoryArray is null
                        ? new ReadOnlySpan<byte>(this.memoryPointer + this.position, count)
                        : new ReadOnlySpan<byte>(this.memoryArray, this.memoryArrayOffset + (int)this.position, count);
            this.position += count;
            this.RecordRead(count);
            return true;
        }

        bytes = default;
        return false;
    }

    /// <summary>
    ///     The bytes from the current position to the end of a memory-backed source, without consuming or charging
    ///     them; false for a stream source. Pair with <see cref="Advance"/> once the consumer knows how many it used.
    /// </summary>
    public bool TryPeekRemaining(out ReadOnlySpan<byte> bytes)
    {
        if (this.memoryBacked && this.position <= this.memoryLength)
        {
            int count = (int)Math.Min(this.memoryLength - this.position, int.MaxValue);
            bytes = this.memoryArray is null
                        ? new ReadOnlySpan<byte>(this.memoryPointer + this.position, count)
                        : new ReadOnlySpan<byte>(this.memoryArray, this.memoryArrayOffset + (int)this.position, count);
            return true;
        }

        bytes = default;
        return false;
    }

    /// <summary>Consumes <paramref name="count"/> bytes that <see cref="TryPeekRemaining"/> exposed, charging them like a read.</summary>
    public void Advance(int count)
    {
        this.position += count;
        this.RecordRead(count);
    }

    /// <summary>
    ///     Like <see cref="TryReadSpan"/> but also fails, without charging or throwing, when the read would exceed the
    ///     total read budget - so a caller can fall back to a path that reports the limit failure at its usual place.
    /// </summary>
    public bool TryReadSpanWithinBudget(int count, out ReadOnlySpan<byte> bytes)
    {
        if (this.memoryBacked && this.position + count <= this.memoryLength && this.bytesRead + (long)count <= this.maxTotalBytesRead)
        {
            return this.TryReadSpan(count, out bytes);
        }

        bytes = default;
        return false;
    }

    /// <summary>
    ///     Stream-source counterpart of <see cref="TryReadSpanWithinBudget"/>: fills <paramref name="destination"/> with the
    ///     next <c>destination.Length</c> bytes when the seekable source provably holds them and the budget allows,
    ///     charging the budget exactly as a sequence of reads would; false (nothing consumed) otherwise.
    /// </summary>
    public bool TryReadBlockWithinBudget(Span<byte> destination)
    {
        if (this.memoryBacked || destination.Length == 0)
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

        if (available < destination.Length || this.bytesRead + (long)destination.Length > this.maxTotalBytesRead)
        {
            return false;
        }

        BinaryPrimitiveIO.ReadExactlyOrThrow(this, destination);
        return true;
    }

    /// <summary>Writes the memory-mode position back to the inner stream; a no-op for stream sources.</summary>
    public void FlushPosition()
    {
        if (this.memoryBacked)
        {
            this.inner.Position = this.position;
        }
    }

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
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (this.memoryBacked)
        {
            StreamArgumentValidation.ValidateRange(buffer, offset, count, nameof(buffer));
            return this.Read(buffer.AsSpan(offset, count));
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

        this.RecordRead(read);
        return read;
    }

    /// <summary>Reads span data while charging the operation-wide budget only for bytes actually returned.</summary>
    public override int Read(Span<byte> buffer)
    {
        if (this.memoryBacked)
        {
            int available = (int)Math.Min(buffer.Length, Math.Max(0, this.memoryLength - this.position));
            if (available > 0)
            {
                ReadOnlySpan<byte> source = this.memoryArray is null
                                                ? new ReadOnlySpan<byte>(this.memoryPointer + this.position, available)
                                                : new ReadOnlySpan<byte>(this.memoryArray, this.memoryArrayOffset + (int)this.position, available);
                source.CopyTo(buffer);
                this.position += available;
                this.RecordRead(available);
            }

            return available;
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

        this.RecordRead(read);
        return read;
    }

    /// <summary>Reads one byte while applying the same budget as bulk reads.</summary>
    public override int ReadByte()
    {
        if (this.memoryBacked)
        {
            if (this.position >= this.memoryLength)
            {
                return -1;
            }

            byte result = this.memoryArray is null
                              ? this.memoryPointer[this.position]
                              : this.memoryArray[this.memoryArrayOffset + (int)this.position];
            this.position++;
            this.RecordRead(1);
            return result;
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
            this.RecordRead(1);
        }

        return value;
    }

    /// <summary>Seeks in the wrapped stream without resetting the total physical-read budget.</summary>
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (this.memoryBacked)
        {
            long basis = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => this.position,
                SeekOrigin.End => this.memoryLength,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            this.Position = checked(basis + offset);
            return this.position;
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

    /// <summary>Rejects length changes because this wrapper advertises a read-only stream contract.</summary>
    public override void SetLength(long value)
    {
        throw new NotSupportedException("The parse budget stream is read-only.");
    }

    /// <summary>Rejects writes because this wrapper is only used by parse operations.</summary>
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("The parse budget stream is read-only.");
    }

    /// <summary>Leaves the caller-owned stream open when parser state is released.</summary>
    protected override void Dispose(bool disposing)
    {
        // Intentionally do not dispose this.inner; public CStruct methods do not take stream ownership.
    }

    /// <summary>Raises a layout-specific read error when one operation exceeds its configured byte budget.</summary>
    private void RecordRead(int count)
    {
        if (count <= 0)
        {
            return;
        }

        try
        {
            this.bytesRead = checked(this.bytesRead + count);
        }
        catch (OverflowException exception)
        {
            throw new CStructReadLimitException(
                "Read operation exceeded the supported read-byte accounting range.",
                exception);
        }

        if (this.bytesRead > this.maxTotalBytesRead)
        {
            throw new CStructReadLimitException("Read operation exceeded the configured total read-byte limit.");
        }
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
        if (this.memoryBacked)
        {
            return this.position;
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
