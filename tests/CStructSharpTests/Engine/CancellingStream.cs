namespace CStructSharp.Tests;

/// <summary>A seekable stream that hides its buffer and cancels a token when a read first covers a given byte.</summary>
internal sealed class CancellingStream : MemoryStream
{
    /// <summary>The byte whose read cancels the token.</summary>
    private readonly int trigger;

    /// <summary>The token source to cancel.</summary>
    private readonly CancellationTokenSource cancellation;

    /// <summary>Creates the stream over a copy of the data.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="trigger">The byte whose read cancels the token.</param>
    /// <param name="cancellation">The token source to cancel.</param>
    public CancellingStream(byte[] data, int trigger, CancellationTokenSource cancellation)
        : base((byte[])data.Clone(), writable: false)
    {
        this.trigger = trigger;
        this.cancellation = cancellation;
    }

    /// <summary>Reads, then cancels the token when the bytes read covered the trigger byte.</summary>
    /// <param name="buffer">The destination.</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(Span<byte> buffer)
    {
        // MemoryStream's own span overload calls the array overload in a derived type, so it goes through there.
        byte[] copy = new byte[buffer.Length];
        int read = this.Read(copy, 0, copy.Length);
        copy.AsSpan(0, read).CopyTo(buffer);
        return read;
    }

    /// <summary>Reads, then cancels the token when the bytes read covered the trigger byte.</summary>
    /// <param name="buffer">The destination array.</param>
    /// <param name="offset">The first index to fill.</param>
    /// <param name="count">The largest number of bytes to read.</param>
    /// <returns>The number of bytes read.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        long start = this.Position;
        int read = base.Read(buffer, offset, count);
        this.CancelIfCovered(start, read);
        return read;
    }

    /// <summary>Reads one byte, then cancels the token when it was the trigger byte.</summary>
    /// <returns>The byte, or -1 at the end.</returns>
    public override int ReadByte()
    {
        long start = this.Position;
        int value = base.ReadByte();
        this.CancelIfCovered(start, value < 0 ? 0 : 1);
        return value;
    }

    /// <summary>Hides the buffer, so the reader streams the bytes rather than reading them from memory.</summary>
    /// <param name="buffer">Always the default segment.</param>
    /// <returns><see langword="false"/>.</returns>
    public override bool TryGetBuffer(out ArraySegment<byte> buffer)
    {
        buffer = default;
        return false;
    }

    /// <summary>Cancels the token when the read range covered the trigger byte.</summary>
    /// <param name="start">The read's first byte.</param>
    /// <param name="read">The number of bytes read.</param>
    private void CancelIfCovered(long start, int read)
    {
        if (start <= this.trigger && this.trigger < start + read)
        {
            this.cancellation.Cancel();
        }
    }
}
