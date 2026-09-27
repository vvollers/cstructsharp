namespace CStructSharp.Tests;

/// <summary>
///     A seekable, expandable memory stream over a copy of the given bytes that records where each read and write
///     starts, the write lengths and the flushes, and can fail a chosen write call or any position access.
/// </summary>
internal sealed class RecordingStream : Stream
{
    private readonly MemoryStream inner = new();

    /// <summary>Creates the stream over a copy of the bytes, positioned at the start.</summary>
    /// <param name="bytes">The initial content.</param>
    public RecordingStream(byte[] bytes)
    {
        this.inner.Write(bytes, 0, bytes.Length);
        this.inner.Position = 0;
    }

    public Exception? Failure { get; init; }

    public int FailWriteCall { get; init; } = -1;

    public Exception? PositionFailure { get; init; }

    public Exception? PositionReadFailure { get; init; }

    public int FlushCalls { get; private set; }

    public List<int> WriteLengths { get; } = [];

    public List<long> WriteStarts { get; } = [];

    public List<long> ReadStarts { get; } = [];

    public int WriteCalls { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    /// <inheritdoc/>
    public override long Length => this.inner.Length;

    public override long Position
    {
        get => this.PositionReadFailure is null ? this.inner.Position : throw this.PositionReadFailure;
        set
        {
            if (this.PositionFailure is not null)
            {
                throw this.PositionFailure;
            }

            this.inner.Position = value;
        }
    }

    /// <summary>Counts the flush.</summary>
    public override void Flush()
    {
        this.FlushCalls++;
    }

    /// <summary>Records where the read starts, then reads.</summary>
    /// <param name="buffer">The destination.</param>
    /// <param name="offset">The first index to fill.</param>
    /// <param name="count">The most bytes to read.</param>
    /// <returns>The bytes read.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        this.ReadStarts.Add(this.inner.Position);
        return this.inner.Read(buffer, offset, count);
    }

    /// <summary>Records where the read starts, then reads.</summary>
    /// <param name="buffer">The destination.</param>
    /// <returns>The bytes read.</returns>
    public override int Read(Span<byte> buffer)
    {
        this.ReadStarts.Add(this.inner.Position);
        return this.inner.Read(buffer);
    }

    /// <summary>Records where the read starts, then reads one byte.</summary>
    /// <returns>The byte, or -1 at the end.</returns>
    public override int ReadByte()
    {
        this.ReadStarts.Add(this.inner.Position);
        return this.inner.ReadByte();
    }

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        return this.inner.Seek(offset, origin);
    }

    /// <inheritdoc/>
    public override void SetLength(long value)
    {
        this.inner.SetLength(value);
    }

    /// <summary>Records the write's start and length, fails if it is the chosen call, and otherwise writes.</summary>
    /// <param name="buffer">The source.</param>
    /// <param name="offset">The first index to write.</param>
    /// <param name="count">The byte count.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        this.WriteStarts.Add(this.inner.Position);
        this.WriteLengths.Add(count);
        this.WriteCalls++;
        if (this.WriteCalls == this.FailWriteCall)
        {
            throw this.Failure ?? new IOException("injected commit failure");
        }

        this.inner.Write(buffer, offset, count);
    }

    /// <summary>Writes through the array overload, so every write is recorded once.</summary>
    /// <param name="buffer">The source.</param>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        byte[] copy = buffer.ToArray();
        this.Write(copy, 0, copy.Length);
    }

    /// <summary>Copies the stream's bytes.</summary>
    /// <returns>The bytes.</returns>
    public byte[] Snapshot()
    {
        return this.inner.ToArray();
    }
}
