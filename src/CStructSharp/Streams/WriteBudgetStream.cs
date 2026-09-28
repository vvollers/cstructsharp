namespace CStructSharp.Streams;

using System;
using System.IO;
using CStructSharp.Diagnostics;

/// <summary>Counts bounded output through a caller-owned seekable stream without taking ownership of it.</summary>
internal sealed class WriteBudgetStream : Stream
{
    private static readonly byte[] ZeroBuffer = new byte[8192];
    private readonly long initialLength;
    private readonly Stream inner;
    private readonly long maxTotalBytesWritten;
    private long bytesWritten;

    /// <summary>Wraps one write operation and snapshots the pre-operation extent used to charge newly created gaps.</summary>
    /// <param name="inner">The caller-owned seekable destination; it stays open when this wrapper is disposed.</param>
    /// <param name="options">The write options that supply the total and per-string byte limits.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    public WriteBudgetStream(Stream inner, WriteOptions options)
    {
        this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        this.maxTotalBytesWritten = options.MaxTotalBytesWritten;
        this.MaxStringBytes = options.MaxStringBytes;
        this.initialLength = this.GetLengthOrThrow();
    }

    /// <summary>Gets the configured per-string encoded-byte budget.</summary>
    public long MaxStringBytes { get; }

    /// <summary>Identifies an atomic in-place update underneath the output budget wrapper.</summary>
    internal bool IsSparseUpdate => this.inner is SparseUpdateStream;

    /// <summary>Gets the caller-owned stream this wrapper forwards to.</summary>
    internal Stream Inner => this.inner;

    /// <summary>Gets a value indicating whether the wrapped stream can read existing bytes.</summary>
    public override bool CanRead => this.inner.CanRead;

    /// <summary>Gets a value indicating whether the wrapped stream can seek.</summary>
    public override bool CanSeek => this.inner.CanSeek;

    /// <summary>Gets a value indicating whether the wrapped stream can write.</summary>
    public override bool CanWrite => this.inner.CanWrite;

    /// <summary>Gets the wrapped stream's length in bytes; a stream failure becomes a write failure.</summary>
    public override long Length => this.GetLengthOrThrow();

    /// <summary>
    ///     Gets or sets the wrapped stream's position in bytes; moving it charges nothing, and a stream failure
    ///     becomes a write failure.
    /// </summary>
    public override long Position
    {
        get
        {
            try
            {
                return this.inner.Position;
            }
            catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
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
            catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
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
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateWriteFailure("Cannot flush the destination stream.", exception);
        }
    }

    /// <summary>Forwards reads needed when an update merges existing bitfield storage.</summary>
    /// <param name="buffer">The array that receives the bytes read.</param>
    /// <param name="offset">The index in <paramref name="buffer"/> where the first byte read is stored.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes read, which is zero at the end of the stream.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        try
        {
            return this.inner.Read(buffer, offset, count);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateReadFailure("Cannot read existing destination bytes.", exception);
        }
    }

    /// <summary>Forwards span reads needed by ordinary stream helpers.</summary>
    /// <param name="buffer">The destination for the bytes read.</param>
    /// <returns>The number of bytes read, which is zero at the end of the stream.</returns>
    public override int Read(Span<byte> buffer)
    {
        try
        {
            return this.inner.Read(buffer);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateReadFailure("Cannot read existing destination bytes.", exception);
        }
    }

    /// <summary>Forwards single-byte reads without charging the write budget.</summary>
    /// <returns>The byte read as a value from 0 to 255, or -1 at the end of the stream.</returns>
    public override int ReadByte()
    {
        try
        {
            return this.inner.ReadByte();
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateReadFailure("Cannot read an existing destination byte.", exception);
        }
    }

    /// <summary>Seeks without resetting the cumulative physical-write or new-output-extent counters.</summary>
    /// <param name="offset">The byte offset relative to <paramref name="origin"/>.</param>
    /// <param name="origin">The reference point for <paramref name="offset"/>.</param>
    /// <returns>The new position in bytes from the start of the stream.</returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        try
        {
            return this.inner.Seek(offset, origin);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateWriteFailure("Cannot seek in the destination stream.", exception);
        }
    }

    /// <summary>Forwards a length change after ensuring it cannot create output beyond the operation budget.</summary>
    /// <param name="value">The new stream length in bytes; growth past the initial length counts as new output.</param>
    public override void SetLength(long value)
    {
        long newExtent = Math.Max(0, checked(value - this.initialLength));
        this.EnsureWithinBudget(this.bytesWritten, newExtent);
        try
        {
            this.inner.SetLength(value);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateWriteFailure("Cannot change the destination stream length.", exception);
        }
    }

    /// <summary>Writes a byte range only after the complete range and any newly created gap fit the budget.</summary>
    /// <param name="buffer">The array holding the bytes to write.</param>
    /// <param name="offset">The index in <paramref name="buffer"/> of the first byte to write.</param>
    /// <param name="count">The number of bytes to write.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        (long nextBytesWritten, _) = this.GetProjectedUsage(count);
        try
        {
            this.inner.Write(buffer, offset, count);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateWriteFailure("Cannot write to the destination stream.", exception);
        }

        this.bytesWritten = nextBytesWritten;
    }

    /// <summary>Applies the same budget to span-based output used by modern stream overloads.</summary>
    /// <param name="buffer">The bytes to write at the current position.</param>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        (long nextBytesWritten, _) = this.GetProjectedUsage(buffer.Length);
        try
        {
            this.inner.Write(buffer);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateWriteFailure("Cannot write to the destination stream.", exception);
        }

        this.bytesWritten = nextBytesWritten;
    }

    /// <summary>
    ///     Whether a block of <paramref name="length"/> bytes written at the current position, charged as
    ///     <paramref name="chargedBytes"/> of physical traffic, fits the budget; a false answer sends the caller to
    ///     the general writer so the limit failure is raised at the field it was always raised at.
    /// </summary>
    /// <param name="length">The block's length in bytes, which determines how far it extends the output.</param>
    /// <param name="chargedBytes">The physical traffic in bytes the block would add to the budget.</param>
    /// <returns><see langword="true"/> when the block fits; <see langword="false"/> otherwise.</returns>
    public bool CanAffordBlock(int length, int chargedBytes)
    {
        try
        {
            long nextBytesWritten = checked(this.bytesWritten + chargedBytes);
            long newExtent = Math.Max(0, checked(checked(this.inner.Position + length) - this.initialLength));
            return Math.Max(nextBytesWritten, newExtent) <= this.maxTotalBytesWritten;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    /// <summary>Writes a block prepared by a static write plan, charging the bytes the general writer would have charged.</summary>
    /// <param name="block">The prepared bytes, written at the current position.</param>
    /// <param name="chargedBytes">The physical traffic in bytes to charge against the budget for this block.</param>
    /// <exception cref="CStructWriteLimitException">The block would exceed the total output budget.</exception>
    public void WriteBlock(ReadOnlySpan<byte> block, int chargedBytes)
    {
        long nextBytesWritten;
        try
        {
            nextBytesWritten = checked(this.bytesWritten + chargedBytes);
            long newExtent = Math.Max(0, checked(checked(this.inner.Position + block.Length) - this.initialLength));
            this.EnsureWithinBudget(nextBytesWritten, newExtent);
        }
        catch (OverflowException exception)
        {
            throw new CStructWriteException("Write output accounting overflowed the supported stream range.", exception);
        }

        try
        {
            this.inner.Write(block);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateWriteFailure("Cannot write to the destination stream.", exception);
        }

        this.bytesWritten = nextBytesWritten;
    }

    /// <summary>Applies the same budget to primitive one-byte codecs.</summary>
    /// <param name="value">The byte to write at the current position.</param>
    public override void WriteByte(byte value)
    {
        (long nextBytesWritten, _) = this.GetProjectedUsage(1);
        try
        {
            this.inner.WriteByte(value);
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateWriteFailure("Cannot write to the destination stream.", exception);
        }

        this.bytesWritten = nextBytesWritten;
    }

    /// <summary>Preflights a zero-filled region, then emits it in bounded reusable chunks.</summary>
    /// <param name="count">The number of zero bytes to write at the current position; zero writes nothing.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="CStructWriteLimitException">The region would exceed the total output budget.</exception>
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
    /// <param name="encodedByteCount">The string's encoded length in bytes.</param>
    /// <exception cref="CStructWriteLimitException">
    ///     <paramref name="encodedByteCount"/> is negative or exceeds <see cref="MaxStringBytes"/>.
    /// </exception>
    public void EnsureStringBytes(long encodedByteCount)
    {
        if (encodedByteCount < 0 || encodedByteCount > this.MaxStringBytes)
        {
            throw new CStructWriteLimitException(WriteFailures.StringBytesLimit);
        }
    }

    /// <summary>Leaves the caller-owned stream open when writer state is released.</summary>
    /// <param name="disposing">Whether the call comes from <see cref="Stream.Dispose()"/>; ignored.</param>
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
            throw new CStructWriteLimitException(WriteFailures.TotalBytesLimit);
        }
    }

    /// <summary>Reads the destination extent while classifying physical stream failures as write failures.</summary>
    private long GetLengthOrThrow()
    {
        try
        {
            return this.inner.Length;
        }
        catch (Exception exception) when (StreamFailureClassification.IsPhysicalStreamFailure(exception))
        {
            throw this.CreateWriteFailure("Cannot read the destination stream length.", exception);
        }
    }

    /// <summary>Creates a physical destination error and records its position when the stream can still report it.</summary>
    private CStructWriteException CreateWriteFailure(string message, Exception exception)
    {
        var result = new CStructWriteException(message, exception);
        result.AttachContext(offset: this.TryGetPosition());
        return result;
    }

    /// <summary>Creates an existing-data read error for update/bitfield operations.</summary>
    private CStructReadException CreateReadFailure(string message, Exception exception)
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
