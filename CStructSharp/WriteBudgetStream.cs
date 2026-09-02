namespace CStructSharp;

using System;
using System.IO;

/// <summary>Counts bounded output through a caller-owned seekable stream without taking ownership of it.</summary>
internal sealed class WriteBudgetStream : Stream
{
    private static readonly byte[] ZeroBuffer = new byte[8192];
    private readonly long initialLength;
    private readonly Stream inner;
    private readonly long maxTotalBytesWritten;
    private long bytesWritten;

    /// <summary>Wraps one write operation and snapshots the pre-operation extent used to charge newly created gaps.</summary>
    public WriteBudgetStream(Stream inner, WriteOptions options)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.maxTotalBytesWritten = options.MaxTotalBytesWritten;
        this.MaxStringBytes = options.MaxStringBytes;
        this.initialLength = this.GetLengthOrThrow();
    }

    /// <summary>Gets the configured per-string encoded-byte budget.</summary>
    public long MaxStringBytes { get; }

    public override bool CanRead => this.inner.CanRead;

    public override bool CanSeek => this.inner.CanSeek;

    public override bool CanWrite => this.inner.CanWrite;

    public override long Length => this.GetLengthOrThrow();

    public override long Position
    {
        get
        {
            try
            {
                return this.inner.Position;
            }
            catch (IOException exception)
            {
                throw this.CreateWriteFailure("Cannot read the destination stream position.", exception);
            }
        }

        set
        {
            try
            {
                this.inner.Position = value;
            }
            catch (IOException exception)
            {
                throw this.CreateWriteFailure("Cannot change the destination stream position.", exception);
            }
        }
    }

    /// <summary>Forwards flushing without taking ownership of the caller's stream.</summary>
    public override void Flush()
    {
        try
        {
            this.inner.Flush();
        }
        catch (IOException exception)
        {
            throw this.CreateWriteFailure("Cannot flush the destination stream.", exception);
        }
    }

    /// <summary>Forwards reads needed when an update merges existing bitfield storage.</summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            return this.inner.Read(buffer, offset, count);
        }
        catch (IOException exception)
        {
            throw this.CreateReadFailure("Cannot read existing destination bytes.", exception);
        }
    }

    /// <summary>Forwards span reads needed by ordinary stream helpers.</summary>
    public override int Read(Span<byte> buffer)
    {
        try
        {
            return this.inner.Read(buffer);
        }
        catch (IOException exception)
        {
            throw this.CreateReadFailure("Cannot read existing destination bytes.", exception);
        }
    }

    /// <summary>Forwards single-byte reads without charging the write budget.</summary>
    public override int ReadByte()
    {
        try
        {
            return this.inner.ReadByte();
        }
        catch (IOException exception)
        {
            throw this.CreateReadFailure("Cannot read an existing destination byte.", exception);
        }
    }

    /// <summary>Seeks without resetting the cumulative physical-write or new-output-extent counters.</summary>
    public override long Seek(long offset, SeekOrigin origin)
    {
        try
        {
            return this.inner.Seek(offset, origin);
        }
        catch (IOException exception)
        {
            throw this.CreateWriteFailure("Cannot seek in the destination stream.", exception);
        }
    }

    /// <summary>Forwards a length change after ensuring it cannot create output beyond the operation budget.</summary>
    public override void SetLength(long value)
    {
        long newExtent = Math.Max(0, checked(value - this.initialLength));
        this.EnsureWithinBudget(this.bytesWritten, newExtent);
        try
        {
            this.inner.SetLength(value);
        }
        catch (IOException exception)
        {
            throw this.CreateWriteFailure("Cannot change the destination stream length.", exception);
        }
    }

    /// <summary>Writes a byte range only after the complete range and any newly created gap fit the budget.</summary>
    public override void Write(byte[] buffer, int offset, int count)
    {
        (long nextBytesWritten, _) = this.GetProjectedUsage(count);
        try
        {
            this.inner.Write(buffer, offset, count);
        }
        catch (IOException exception)
        {
            throw this.CreateWriteFailure("Cannot write to the destination stream.", exception);
        }

        this.bytesWritten = nextBytesWritten;
    }

    /// <summary>Applies the same budget to span-based output used by modern stream overloads.</summary>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        (long nextBytesWritten, _) = this.GetProjectedUsage(buffer.Length);
        try
        {
            this.inner.Write(buffer);
        }
        catch (IOException exception)
        {
            throw this.CreateWriteFailure("Cannot write to the destination stream.", exception);
        }

        this.bytesWritten = nextBytesWritten;
    }

    /// <summary>Applies the same budget to primitive one-byte codecs.</summary>
    public override void WriteByte(byte value)
    {
        (long nextBytesWritten, _) = this.GetProjectedUsage(1);
        try
        {
            this.inner.WriteByte(value);
        }
        catch (IOException exception)
        {
            throw this.CreateWriteFailure("Cannot write to the destination stream.", exception);
        }

        this.bytesWritten = nextBytesWritten;
    }

    /// <summary>Preflights a zero-filled region, then emits it in bounded reusable chunks.</summary>
    public void WriteZeroes(int count)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        if (count == 0)
        {
            return;
        }

        // Check the complete region before the first chunk so a budget failure cannot partially clear caller data.
        _ = this.GetProjectedUsage(count);
        while (count > 0)
        {
            int chunkLength = Math.Min(count, ZeroBuffer.Length);
            this.Write(ZeroBuffer, 0, chunkLength);
            count -= chunkLength;
        }
    }

    /// <summary>Rejects a string before its encoded payload is allocated or submitted to the stream.</summary>
    public void EnsureStringBytes(long encodedByteCount)
    {
        if (encodedByteCount < 0 || encodedByteCount > this.MaxStringBytes)
        {
            throw new CStructWriteLimitException("String field exceeded the configured encoded-byte write limit.");
        }
    }

    /// <summary>Leaves the caller-owned stream open when writer state is released.</summary>
    protected override void Dispose(bool disposing)
    {
        // Intentionally do not dispose this.inner; public CStruct methods do not take stream ownership.
    }

    /// <summary>Calculates cumulative physical output and extension before allowing one stream write.</summary>
    private (long BytesWritten, long NewExtent) GetProjectedUsage(int count)
    {
        try
        {
            long nextBytesWritten = checked(this.bytesWritten + count);
            long writeEnd = checked(this.inner.Position + count);
            long newExtent = Math.Max(0, checked(writeEnd - this.initialLength));
            this.EnsureWithinBudget(nextBytesWritten, newExtent);
            return (nextBytesWritten, newExtent);
        }
        catch (OverflowException exception)
        {
            throw new CStructWriteException("Write output accounting overflowed the supported stream range.", exception);
        }
    }

    /// <summary>Uses the larger of physical traffic and new extent so neither repeated writes nor seek gaps bypass the limit.</summary>
    private void EnsureWithinBudget(long physicalBytes, long newExtent)
    {
        if (Math.Max(physicalBytes, newExtent) > this.maxTotalBytesWritten)
        {
            throw new CStructWriteLimitException("Write operation exceeded the configured total byte limit.");
        }
    }

    /// <summary>Reads the destination extent while classifying physical stream failures as write failures.</summary>
    private long GetLengthOrThrow()
    {
        try
        {
            return this.inner.Length;
        }
        catch (IOException exception)
        {
            throw this.CreateWriteFailure("Cannot read the destination stream length.", exception);
        }
    }

    /// <summary>Creates a physical destination error and records its position when the stream can still report it.</summary>
    private CStructWriteException CreateWriteFailure(string message, IOException exception)
    {
        var result = new CStructWriteException(message, exception);
        result.AttachContext(offset: this.TryGetPosition());
        return result;
    }

    /// <summary>Creates an existing-data read error for update/bitfield operations.</summary>
    private CStructReadException CreateReadFailure(string message, IOException exception)
    {
        var result = new CStructReadException(message, exception);
        result.AttachContext(offset: this.TryGetPosition());
        return result;
    }

    /// <summary>Obtains diagnostic position context without allowing a secondary stream failure to hide the first.</summary>
    private long? TryGetPosition()
    {
        try
        {
            return this.inner.Position;
        }
        catch (Exception)
        {
            // Preserve the physical I/O failure even if the stream can no longer report diagnostic context.
            return null;
        }
    }
}
