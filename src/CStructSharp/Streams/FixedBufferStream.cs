namespace CStructSharp.Streams;

using System;
using System.IO;
using CStructSharp.Diagnostics;

/// <summary>
///     Exposes one caller-pinned byte region through the synchronous stream contract used by the compiled executor.
///     The owning public operation keeps the source span fixed for this stream's complete lifetime.
/// </summary>
internal sealed unsafe class FixedBufferStream : Stream
{
    private readonly byte* buffer;
    private readonly int capacity;
    private readonly bool writable;
    private long length;
    private long position;

    /// <summary>Creates a read-only initialized region or an empty writable region over fixed caller storage.</summary>
    /// <param name="buffer">The region's first byte; the caller keeps it pinned for the stream's lifetime.</param>
    /// <param name="capacity">The region's size in bytes.</param>
    /// <param name="writable">
    ///     Whether the stream may write; a writable region starts empty and a read-only one starts fully initialized.
    /// </param>
    public FixedBufferStream(byte* buffer, int capacity, bool writable)
        : this(buffer, capacity, writable, initialized: !writable)
    {
    }

    /// <summary>
    ///     Creates a stream over caller storage; <paramref name="initialized"/> says whether the whole region already
    ///     holds data (an in-place update reads and keeps it) or only what the stream writes counts as its length.
    /// </summary>
    /// <param name="buffer">The region's first byte.</param>
    /// <param name="capacity">The region's size.</param>
    /// <param name="writable">Whether the stream may write.</param>
    /// <param name="initialized">Whether the region's bytes are the stream's initial contents.</param>
    public FixedBufferStream(byte* buffer, int capacity, bool writable, bool initialized)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.buffer = buffer;
        this.capacity = capacity;
        this.writable = writable;
        this.length = initialized ? capacity : 0;
    }

    /// <summary>Gets the region's size in bytes, the most the stream can hold.</summary>
    internal int Capacity => this.capacity;

    /// <summary>Gets a value indicating whether the stream can read; always <see langword="true"/>.</summary>
    public override bool CanRead => true;

    /// <summary>Gets a value indicating whether the stream can seek; always <see langword="true"/>.</summary>
    public override bool CanSeek => true;

    /// <summary>Gets a value indicating whether the stream was created writable.</summary>
    public override bool CanWrite => this.writable;

    /// <summary>Gets the initialized length in bytes, which never exceeds <see cref="Capacity"/>.</summary>
    public override long Length => this.length;

    /// <summary>Gets or sets the position in bytes from the region start.</summary>
    /// <exception cref="CStructException">The value is negative or past the capacity.</exception>
    public override long Position
    {
        get => this.position;
        set
        {
            if (value < 0 || value > this.capacity)
            {
                throw this.CreateBoundsException("The requested position is outside the supplied memory region.");
            }

            this.position = value;
        }
    }

    /// <summary>Has no external resource to flush.</summary>
    public override void Flush()
    {
    }

    /// <summary>Exposes the pinned region so a read-budget cursor can serve reads without virtual calls.</summary>
    /// <param name="region">
    ///     Receives the region's first byte; the caller must not use it after the region is unpinned.
    /// </param>
    /// <param name="length">Receives the initialized length in bytes.</param>
    /// <returns>
    ///     <see langword="true"/> for a read-only region, whose bytes cannot change; <see langword="false"/> for a
    ///     writable one.
    /// </returns>
    internal bool TryGetReadOnlyRegion(out byte* region, out long length)
    {
        region = this.buffer;
        length = this.length;
        return !this.writable;
    }

    /// <summary>Reads initialized bytes from the current region position.</summary>
    /// <param name="destination">The array that receives the bytes.</param>
    /// <param name="offset">The index in <paramref name="destination"/> where the first byte is stored.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes copied, or 0 at or past the initialized length.</returns>
    public override int Read(byte[] destination, int offset, int count)
    {
        StreamArgumentValidation.ValidateRange(destination, offset, count, nameof(destination));
        return this.Read(destination.AsSpan(offset, count));
    }

    /// <summary>Reads initialized bytes from the current region position.</summary>
    /// <param name="destination">The span to fill; at most its length in bytes is copied.</param>
    /// <returns>The number of bytes copied, or 0 at or past the initialized length.</returns>
    public override int Read(Span<byte> destination)
    {
        int count = (int)Math.Min(destination.Length, Math.Max(0, this.length - this.position));
        if (count == 0)
        {
            return 0;
        }

        new ReadOnlySpan<byte>(this.buffer + (int)this.position, count).CopyTo(destination);
        this.position += count;
        return count;
    }

    /// <summary>Moves within the fixed region without changing its logical length.</summary>
    /// <param name="offset">The signed byte distance from <paramref name="origin"/>.</param>
    /// <param name="origin">
    ///     The reference point: the region start, the current position, or the end of the initialized length.
    /// </param>
    /// <returns>The new position in bytes from the region start.</returns>
    /// <exception cref="CStructException">The target lies before the start or past the capacity.</exception>
    public override long Seek(long offset, SeekOrigin origin)
    {
        long basis = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => this.position,
            SeekOrigin.End => this.length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        long target;
        try
        {
            target = checked(basis + offset);
        }
        catch (OverflowException exception)
        {
            throw this.CreateBoundsException(
                "The requested position is outside the supplied memory region.",
                exception);
        }

        this.Position = target;
        return target;
    }

    /// <summary>Changes the initialized extent of a writable region without exceeding its capacity.</summary>
    /// <param name="value">
    ///     The new length in bytes, from 0 to the capacity; growth zero-fills the new bytes and shrinking clamps the
    ///     position.
    /// </param>
    /// <exception cref="NotSupportedException">The region is read-only.</exception>
    /// <exception cref="CStructWriteException">The length is negative or exceeds the capacity.</exception>
    public override void SetLength(long value)
    {
        this.EnsureWritable();
        if (value < 0 || value > this.capacity)
        {
            throw new CStructWriteException("The requested length is outside the supplied memory region.");
        }

        if (value > this.length)
        {
            new Span<byte>(this.buffer + (int)this.length, (int)(value - this.length)).Clear();
        }

        this.length = value;
        if (this.position > value)
        {
            this.position = value;
        }
    }

    /// <summary>Writes into caller storage and extends the initialized prefix.</summary>
    /// <param name="source">The array holding the bytes to write.</param>
    /// <param name="offset">The index in <paramref name="source"/> of the first byte to write.</param>
    /// <param name="count">The number of bytes to write.</param>
    public override void Write(byte[] source, int offset, int count)
    {
        StreamArgumentValidation.ValidateRange(source, offset, count, nameof(source));
        this.Write(source.AsSpan(offset, count));
    }

    /// <summary>Writes into caller storage and extends the initialized prefix.</summary>
    /// <param name="source">
    ///     The bytes to copy at the current position; a gap left by seeking past the length is zero-filled first.
    /// </param>
    /// <exception cref="NotSupportedException">The region is read-only.</exception>
    /// <exception cref="CStructWriteException">The bytes would run past the region's capacity.</exception>
    public override void Write(ReadOnlySpan<byte> source)
    {
        this.EnsureWritable();
        if (source.Length > this.capacity - this.position)
        {
            throw new CStructWriteException(WriteFailures.DestinationCapacity);
        }

        if (this.position > this.length)
        {
            new Span<byte>(this.buffer + (int)this.length, (int)(this.position - this.length)).Clear();
        }

        source.CopyTo(new Span<byte>(this.buffer + (int)this.position, source.Length));
        this.position += source.Length;
        this.length = Math.Max(this.length, this.position);
    }

    /// <summary>Does not own or unpin the caller's memory.</summary>
    /// <param name="disposing">Whether the call comes from <see cref="Stream.Dispose()"/>; ignored.</param>
    protected override void Dispose(bool disposing)
    {
    }

    /// <summary>Rejects writes through a read-only input region.</summary>
    private void EnsureWritable()
    {
        if (!this.writable)
        {
            throw new NotSupportedException("The supplied memory region is read-only.");
        }
    }

    /// <summary>
    ///     Reports an out-of-bounds position through the exception family matching this region's direction: a
    ///     read-only region backs <c>Parse</c>/<c>ReadValue</c>, so it reports <see cref="CStructReadException"/>;
    ///     a writable region backs <c>Serialize</c>, so it reports <see cref="CStructWriteException"/>.
    /// </summary>
    private CStructException CreateBoundsException(string message, Exception? innerException = null)
    {
        return (this.writable, innerException) switch
        {
            (true, null) => new CStructWriteException(message),
            (true, not null) => new CStructWriteException(message, innerException),
            (false, null) => new CStructReadException(message),
            (false, not null) => new CStructReadException(message, innerException),
        };
    }
}
