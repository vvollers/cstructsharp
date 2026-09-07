namespace CStructSharp.Tests;

/// <summary>Provides seekable memory storage while counting how many times a read method was invoked.</summary>
internal sealed class ReadCallCountingStream : Stream
{
    private readonly MemoryStream inner;

    /// <summary>Initializes a new instance of the <see cref="ReadCallCountingStream"/> class.</summary>
    /// <param name="bytes">The initial storage.</param>
    public ReadCallCountingStream(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        this.inner = new MemoryStream(bytes, writable: false);
    }

    /// <summary>Gets the number of completed calls to <see cref="Read(byte[], int, int)"/> or <see cref="Read(Span{byte})"/>.</summary>
    public int ReadCallCount { get; private set; }

    /// <inheritdoc />
    public override bool CanRead => this.inner.CanRead;

    /// <inheritdoc />
    public override bool CanSeek => this.inner.CanSeek;

    /// <inheritdoc />
    public override bool CanWrite => false;

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
        this.ReadCallCount++;
        return this.inner.Read(buffer, offset, count);
    }

    /// <inheritdoc />
    public override int Read(Span<byte> buffer)
    {
        this.ReadCallCount++;
        return this.inner.Read(buffer);
    }

    /// <inheritdoc />
    public override int ReadByte()
    {
        this.ReadCallCount++;
        return this.inner.ReadByte();
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin)
    {
        return this.inner.Seek(offset, origin);
    }

    /// <inheritdoc />
    public override void SetLength(long value)
    {
        throw new NotSupportedException("This stream is read-only.");
    }

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException("This stream is read-only.");
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
