namespace CStructSharp;

using System;
using System.IO;

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
    public FixedBufferStream(byte* buffer, int capacity, bool writable)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        this.buffer = buffer;
        this.capacity = capacity;
        this.writable = writable;
        this.length = writable ? 0 : capacity;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => this.writable;

    public override long Length => this.length;

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

    /// <summary>Reads initialized bytes from the current region position.</summary>
    public override int Read(byte[] destination, int offset, int count)
    {
        StreamArgumentValidation.ValidateRange(destination, offset, count, nameof(destination));
        return this.Read(destination.AsSpan(offset, count));
    }

    /// <summary>Reads initialized bytes from the current region position.</summary>
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
    public override void Write(byte[] source, int offset, int count)
    {
        StreamArgumentValidation.ValidateRange(source, offset, count, nameof(source));
        this.Write(source.AsSpan(offset, count));
    }

    /// <summary>Writes into caller storage and extends the initialized prefix.</summary>
    public override void Write(ReadOnlySpan<byte> source)
    {
        this.EnsureWritable();
        if (source.Length > this.capacity - this.position)
        {
            throw new CStructWriteException("The serialized value exceeds the supplied destination capacity.");
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
