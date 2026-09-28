namespace CStructSharp.Streams;

using System;
using System.Buffers;
using System.IO;
using CStructSharp.Diagnostics;

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
    /// <param name="writer">
    ///     The caller-owned writer that receives the bytes; it is advanced but never completed.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public BufferWriterStream(IBufferWriter<byte> writer)
    {
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <summary>
    ///     Gets a value indicating whether the stream can read; always <see langword="true"/>, although only bytes in
    ///     the active window can be read back.
    /// </summary>
    public override bool CanRead => true;

    /// <summary>
    ///     Gets a value indicating whether the stream can seek; always <see langword="true"/>, although committed
    ///     output cannot be revisited.
    /// </summary>
    public override bool CanSeek => true;

    /// <summary>Gets a value indicating whether the stream can write; always <see langword="true"/>.</summary>
    public override bool CanWrite => true;

    /// <summary>Gets the number of bytes written so far: the committed bytes plus the active window's length.</summary>
    public override long Length => checked(this.committedLength + this.windowLength);

    /// <summary>
    ///     Gets or sets the position in bytes from the first byte this stream wrote. Setting it may not move into
    ///     committed output and may commit the active window when moving forward past it.
    /// </summary>
    /// <exception cref="CStructWriteException">
    ///     The value lies in committed output or crosses a window boundary.
    /// </exception>
    public override long Position
    {
        get => checked(this.committedLength + this.windowPosition);
        set => this.SetPosition(value);
    }

    /// <summary>Advances the final active writer window and returns this operation's appended length.</summary>
    /// <returns>
    ///     The total number of bytes this stream advanced the writer by; repeated calls return the same value.
    /// </returns>
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
    /// <param name="destination">The array that receives the bytes.</param>
    /// <param name="offset">The index in <paramref name="destination"/> where the first byte is stored.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes copied, or 0 at or past the end of the active window.</returns>
    public override int Read(byte[] destination, int offset, int count)
    {
        StreamArgumentValidation.ValidateRange(destination, offset, count, nameof(destination));
        return this.Read(destination.AsSpan(offset, count));
    }

    /// <summary>Reads bytes retained in the active window for bitfield merging.</summary>
    /// <param name="destination">The span to fill; at most its length in bytes is copied.</param>
    /// <returns>The number of bytes copied, or 0 at or past the end of the active window.</returns>
    /// <exception cref="ObjectDisposedException"><see cref="Complete"/> was already called.</exception>
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
    /// <param name="offset">The signed byte distance from <paramref name="origin"/>.</param>
    /// <param name="origin">
    ///     The reference point: the first byte this stream wrote, the current position, or the current length.
    /// </param>
    /// <returns>The new position in bytes from the first byte this stream wrote.</returns>
    /// <exception cref="CStructWriteException">
    ///     The target overflows or lies in output that was already committed to the writer.
    /// </exception>
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
            throw new CStructWriteException("The requested writer position overflowed.", exception);
        }

        this.Position = target;
        return target;
    }

    /// <summary>Supports length changes only inside the current uncommitted window.</summary>
    /// <param name="value">
    ///     The new total length in bytes since this stream was created; growth zero-fills the new bytes.
    /// </param>
    /// <exception cref="ObjectDisposedException"><see cref="Complete"/> was already called.</exception>
    /// <exception cref="CStructWriteException">
    ///     The length would truncate committed output or cannot be held in the active window.
    /// </exception>
    public override void SetLength(long value)
    {
        this.EnsureActive();
        if (value < this.committedLength)
        {
            throw new CStructWriteException("Committed IBufferWriter output cannot be truncated.");
        }

        long relative = value - this.committedLength;
        if (relative > int.MaxValue)
        {
            throw new CStructWriteException("The requested writer length is too large for one active window.");
        }

        int required = (int)relative;
        if (required > this.window.Length)
        {
            if (this.windowLength != 0 || this.windowPosition != 0)
            {
                throw new CStructWriteException("The requested length crosses an active writer-window boundary.");
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
    /// <param name="source">The array holding the bytes to write.</param>
    /// <param name="offset">The index in <paramref name="source"/> of the first byte to write.</param>
    /// <param name="count">The number of bytes to write.</param>
    public override void Write(byte[] source, int offset, int count)
    {
        StreamArgumentValidation.ValidateRange(source, offset, count, nameof(source));
        this.Write(source.AsSpan(offset, count));
    }

    /// <summary>Appends or rewrites bytes in the active uncommitted window.</summary>
    /// <param name="source">
    ///     The bytes to copy at the current position; a gap left by seeking forward is zero-filled first.
    /// </param>
    /// <exception cref="ObjectDisposedException"><see cref="Complete"/> was already called.</exception>
    /// <exception cref="CStructWriteException">
    ///     The bytes would overwrite part of the window and then cross into output that must be committed first.
    /// </exception>
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
    /// <param name="disposing">Whether the call comes from <see cref="Stream.Dispose()"/>; ignored.</param>
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
            throw new CStructWriteException(
                "The shared serializer attempted to cross a committed writer-window boundary.");
        }

        int forwardGap = this.windowPosition - this.windowLength;
        this.CommitWindow();
        long requiredWindow = checked((long)forwardGap + count);
        if (requiredWindow > int.MaxValue)
        {
            throw new CStructWriteException("The requested writer range is too large for one active window.");
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
            throw new CStructWriteException("IBufferWriter returned less memory than the requested size hint.");
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
            throw new CStructWriteException("Committed IBufferWriter output cannot be revisited.");
        }

        long relative = value - this.committedLength;
        if (relative > int.MaxValue)
        {
            throw new CStructWriteException("The requested writer position is too large for one active window.");
        }

        int requested = (int)relative;
        if (requested > this.window.Length)
        {
            if (this.windowPosition < this.windowLength)
            {
                throw new CStructWriteException(
                    "The shared serializer attempted to seek across a writer-window boundary.");
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
