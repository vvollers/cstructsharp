namespace CStructSharp;

using System;
using System.Buffers;
using System.IO;

/// <summary>
///     Adapts a forward-only <see cref="IBufferWriter{T}"/> to the small seek/read window required by the shared
///     serializer. Completed windows are advanced without copying; bitfield rewrites stay in the active window.
/// </summary>
internal sealed class BufferWriterStream : Stream
{
    private const int DefaultWindowSize = 4096;
    private readonly IBufferWriter<byte> writer;
    private Memory<byte> window;
    private long committedLength;
    private int windowLength;
    private int windowPosition;
    private bool completed;

    /// <summary>Creates an empty region that appends to the caller-owned writer.</summary>
    public BufferWriterStream(IBufferWriter<byte> writer)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length => checked(this.committedLength + this.windowLength);

    public override long Position
    {
        get => checked(this.committedLength + this.windowPosition);
        set => this.SetPosition(value);
    }

    /// <summary>Advances the final active writer window and returns this operation's appended length.</summary>
    public long Complete()
    {
        if (!this.completed)
        {
            this.CommitWindow();
            this.completed = true;
        }

        return this.committedLength;
    }

    /// <summary>The writer has no independent flush operation.</summary>
    public override void Flush()
    {
    }

    /// <summary>Reads bytes retained in the active window for bitfield merging.</summary>
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

    /// <summary>Reads bytes retained in the active window for bitfield merging.</summary>
    public override int Read(Span<byte> destination)
    {
        this.EnsureActive();
        int count = Math.Min(destination.Length, this.windowLength - this.windowPosition);
        if (count <= 0)
        {
            return 0;
        }

        this.window.Span.Slice(this.windowPosition, count).CopyTo(destination);
        this.windowPosition += count;
        return count;
    }

    /// <summary>Seeks only within the uncommitted window or forward from its current end.</summary>
    public override long Seek(long offset, SeekOrigin origin)
    {
        long basis = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => this.Position,
            SeekOrigin.End => this.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        long target;
        try
        {
            target = checked(basis + offset);
        }
        catch (OverflowException exception)
        {
            throw new IOException("The requested writer position overflowed.", exception);
        }

        this.Position = target;
        return target;
    }

    /// <summary>Supports length changes only inside the current uncommitted window.</summary>
    public override void SetLength(long value)
    {
        this.EnsureActive();
        if (value < this.committedLength)
        {
            throw new IOException("Committed IBufferWriter output cannot be truncated.");
        }

        long relative = value - this.committedLength;
        if (relative > int.MaxValue)
        {
            throw new IOException("The requested writer length is too large for one active window.");
        }

        int required = (int)relative;
        if (required > this.window.Length)
        {
            if (this.windowLength != 0 || this.windowPosition != 0)
            {
                throw new IOException("The requested length crosses an active writer-window boundary.");
            }

            this.EnsureWindow(required);
        }

        if (required > this.windowLength)
        {
            this.window.Span.Slice(this.windowLength, required - this.windowLength).Clear();
        }

        this.windowLength = required;
        this.windowPosition = Math.Min(this.windowPosition, required);
    }

    /// <summary>Appends or rewrites bytes in the active uncommitted window.</summary>
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

    /// <summary>Appends or rewrites bytes in the active uncommitted window.</summary>
    public override void Write(ReadOnlySpan<byte> source)
    {
        this.EnsureActive();
        this.EnsureWritableRange(source.Length);
        if (this.windowPosition > this.windowLength)
        {
            this.window.Span.Slice(this.windowLength, this.windowPosition - this.windowLength).Clear();
        }

        source.CopyTo(this.window.Span.Slice(this.windowPosition, source.Length));
        this.windowPosition += source.Length;
        this.windowLength = Math.Max(this.windowLength, this.windowPosition);
    }

    /// <summary>Does not own or complete the caller's writer when disposed.</summary>
    protected override void Dispose(bool disposing)
    {
    }

    /// <summary>Obtains enough contiguous active memory for the next complete write.</summary>
    private void EnsureWritableRange(int count)
    {
        long requiredEnd = checked((long)this.windowPosition + count);
        if (requiredEnd <= this.window.Length)
        {
            return;
        }

        if (this.windowPosition < this.windowLength)
        {
            throw new IOException("The shared serializer attempted to cross a committed writer-window boundary.");
        }

        int forwardGap = this.windowPosition - this.windowLength;
        this.CommitWindow();
        long requiredWindow = checked((long)forwardGap + count);
        if (requiredWindow > int.MaxValue)
        {
            throw new IOException("The requested writer range is too large for one active window.");
        }

        this.EnsureWindow((int)requiredWindow);
        this.windowPosition = forwardGap;
    }

    /// <summary>Obtains an active window satisfying the requested capacity.</summary>
    private void EnsureWindow(int required)
    {
        if (required <= this.window.Length)
        {
            return;
        }

        int sizeHint = Math.Max(DefaultWindowSize, required);
        this.window = this.writer.GetMemory(sizeHint);
        if (this.window.Length < required)
        {
            throw new InvalidOperationException("IBufferWriter returned less memory than the requested size hint.");
        }
    }

    /// <summary>Publishes the current high-water mark and starts the next region-relative window.</summary>
    private void CommitWindow()
    {
        if (this.windowLength > 0)
        {
            this.writer.Advance(this.windowLength);
            this.committedLength = checked(this.committedLength + this.windowLength);
        }

        this.window = default;
        this.windowLength = 0;
        this.windowPosition = 0;
    }

    /// <summary>Moves within the current region while refusing to revisit already advanced output.</summary>
    private void SetPosition(long value)
    {
        this.EnsureActive();
        if (value < this.committedLength)
        {
            throw new IOException("Committed IBufferWriter output cannot be revisited.");
        }

        long relative = value - this.committedLength;
        if (relative > int.MaxValue)
        {
            throw new IOException("The requested writer position is too large for one active window.");
        }

        int requested = (int)relative;
        if (requested > this.window.Length)
        {
            if (this.windowPosition < this.windowLength)
            {
                throw new IOException("The shared serializer attempted to seek across a writer-window boundary.");
            }

            int forward = requested - this.windowLength;
            this.CommitWindow();
            this.EnsureWindow(forward);
            this.windowPosition = forward;
            return;
        }

        this.windowPosition = requested;
    }

    /// <summary>Rejects use after the output was completed.</summary>
    private void EnsureActive()
    {
        if (this.completed)
        {
            throw new ObjectDisposedException(nameof(BufferWriterStream));
        }
    }
}
