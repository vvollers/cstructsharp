namespace CStructSharp.Tests;

/// <summary>Provides seekable memory storage while deliberately fragmenting every read.</summary>
internal sealed class ChunkedMemoryStream : Stream
{
    private readonly MemoryStream inner;
    private readonly int maximumReadSize;

    /// <summary>Initializes a new instance of the <see cref="ChunkedMemoryStream"/> class.</summary>
    /// <param name="bytes">The initial storage.</param>
    /// <param name="maximumReadSize">The positive maximum number of bytes returned by one read.</param>
    /// <param name="writable">Whether the storage permits writes.</param>
    public ChunkedMemoryStream(byte[] bytes, int maximumReadSize, bool writable)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumReadSize);
        this.inner = new MemoryStream(bytes, writable);
        this.maximumReadSize = maximumReadSize;
    }

    /// <inheritdoc />
    public override bool CanRead => this.inner.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => this.inner.CanSeek;

    /// <inheritdoc />
    public override bool CanWrite => this.inner.CanWrite;

    /// <inheritdoc />
    public override long Length => this.inner.Length;

    /// <inheritdoc />
    public override long Position
    {
        get => this.inner.Position;
        set => this.inner.Position = value;
    }

    /// <inheritdoc />
    public override void Flush()
    {
        this.inner.Flush();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        return this.inner.Read(buffer, offset, Math.Min(count, this.maximumReadSize));
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        return this.inner.Read(buffer[..Math.Min(buffer.Length, this.maximumReadSize)]);
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        return this.inner.Seek(offset, origin);
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        this.inner.SetLength(value);
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        this.inner.Write(buffer, offset, count);
    }

    /// <inheritdoc />
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        this.inner.Write(buffer);
    }

    /// <summary>Returns a copy of the complete backing storage.</summary>
    /// <returns>The current stream bytes.</returns>
    public byte[] ToArray()
    {
        return this.inner.ToArray();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
