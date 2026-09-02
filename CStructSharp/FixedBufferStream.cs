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
                throw new IOException("The requested position is outside the supplied memory region.");
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
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > destination.Length - count)
        {
            throw new ArgumentException("The destination range is outside the supplied array.", nameof(destination));
        }

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
            throw new IOException("The requested position is outside the supplied memory region.", exception);
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
            throw new IOException("The requested length is outside the supplied memory region.");
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
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > source.Length - count)
        {
            throw new ArgumentException("The source range is outside the supplied array.", nameof(source));
        }

        this.Write(source.AsSpan(offset, count));
    }

    /// <summary>Writes into caller storage and extends the initialized prefix.</summary>
    public override void Write(ReadOnlySpan<byte> source)
    {
        this.EnsureWritable();
        if (source.Length > this.capacity - this.position)
        {
            throw new IOException("The serialized value exceeds the supplied destination capacity.");
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
}
