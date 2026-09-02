namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Presents one existing stream as a non-extending sparse copy-on-write view, then commits validated ranges explicitly.
/// </summary>
internal sealed class SparseUpdateStream : Stream
{
    private const int ChunkSize = 1024;
    private readonly Stream baseline;
    private readonly long baselineLength;
    private readonly SortedDictionary<long, StagedChunk> chunks = new();
    private bool disposed;
    private long position;

    /// <summary>Creates a virtual writer at an absolute target while retaining the caller stream as a read-only baseline.</summary>
    public SparseUpdateStream(Stream baseline, long initialPosition)
    {
        this.baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
        this.baselineLength = baseline.Length;
        if (initialPosition > this.baselineLength)
        {
            throw new CStructWriteException("Update target starts beyond the existing destination stream.");
        }

        this.Position = initialPosition;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => true;

    public override long Length => this.baselineLength;

    public override long Position
    {
        get => this.position;
        set
        {
            if (value < 0)
            {
                throw new CStructWriteException("An update cannot seek before the start of the destination.");
            }

            this.position = value;
        }
    }

    /// <summary>Commits the final non-overlapping write set in ascending address order without flushing the destination.</summary>
    public void CommitTo(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);

        foreach (StagedRange range in this.GetStagedRanges())
        {
            try
            {
                destination.Position = range.Start;
            }
            catch (Exception exception) when (IsPhysicalStreamFailure(exception))
            {
                throw CreateCommitFailure(
                    "Cannot seek to a validated update range in the destination stream.",
                    exception,
                    destination,
                    range.Start);
            }

            try
            {
                destination.Write(range.Bytes, 0, range.Length);
            }
            catch (Exception exception) when (IsPhysicalStreamFailure(exception))
            {
                throw CreateCommitFailure(
                    "Cannot commit a validated update range to the destination stream.",
                    exception,
                    destination,
                    range.Start);
            }
        }
    }

    /// <summary>Does not forward a flush because preparation owns no external output.</summary>
    public override void Flush()
    {
    }

    /// <summary>Reads existing bytes with staged writes overlaid using last-write-wins behavior.</summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The offset and count exceed the destination buffer.", nameof(count));
        }

        return this.Read(buffer.AsSpan(offset, count));
    }

    /// <summary>Reads existing bytes with staged writes overlaid using last-write-wins behavior.</summary>
    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty || this.position >= this.baselineLength)
        {
            return 0;
        }

        int available = (int)Math.Min(buffer.Length, this.baselineLength - this.position);
        int completed = 0;
        while (completed < available)
        {
            long address = this.position + completed;
            if (this.TryGetStagedByte(address, out byte staged))
            {
                buffer[completed] = staged;
                completed++;
                continue;
            }

            int missingLength = 1;
            while (completed + missingLength < available &&
                   !this.TryGetStagedByte(address + missingLength, out _))
            {
                missingLength++;
            }

            this.baseline.Position = address;
            int read = this.baseline.Read(buffer.Slice(completed, missingLength));
            completed += read;
            if (read < missingLength)
            {
                break;
            }
        }

        this.position += completed;
        return completed;
    }

    /// <summary>Reads one staged or baseline byte without exposing writes to the caller stream.</summary>
    public override int ReadByte()
    {
        if (this.position >= this.baselineLength)
        {
            return -1;
        }

        long address = this.position;
        int result;
        if (this.TryGetStagedByte(address, out byte staged))
        {
            result = staged;
        }
        else
        {
            this.baseline.Position = address;
            result = this.baseline.ReadByte();
            if (result < 0)
            {
                return -1;
            }
        }

        this.position++;
        return result;
    }

    /// <summary>Moves only the virtual cursor and preserves absolute coordinates used by the compiled writer.</summary>
    public override long Seek(long offset, SeekOrigin origin)
    {
        try
        {
            long start = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => this.position,
                SeekOrigin.End => this.baselineLength,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            this.Position = checked(start + offset);
            return this.position;
        }
        catch (OverflowException exception)
        {
            throw new CStructWriteException("Update staging position exceeded the supported stream range.", exception);
        }
    }

    /// <summary>Rejects structural resizing; update may replace only bytes already present in the destination.</summary>
    public override void SetLength(long value)
    {
        if (value != this.baselineLength)
        {
            throw new CStructWriteException("Update operations cannot change the destination stream length.");
        }
    }

    /// <summary>Retains one byte range in sparse chunks after proving it cannot extend the caller stream.</summary>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (offset > buffer.Length - count)
        {
            throw new ArgumentException("The offset and count exceed the source buffer.", nameof(count));
        }

        this.Write(buffer.AsSpan(offset, count));
    }

    /// <summary>Retains one byte range in sparse chunks after proving it cannot extend the caller stream.</summary>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        long end;
        try
        {
            end = checked(this.position + buffer.Length);
        }
        catch (OverflowException exception)
        {
            throw new CStructWriteException("Update output exceeded the supported stream range.", exception);
        }

        if (end > this.baselineLength)
        {
            throw new CStructWriteException("Update output would extend beyond the existing destination stream.");
        }

        int sourceOffset = 0;
        while (sourceOffset < buffer.Length)
        {
            long address = this.position + sourceOffset;
            long chunkIndex = address / ChunkSize;
            int chunkOffset = (int)(address % ChunkSize);
            int length = Math.Min(buffer.Length - sourceOffset, ChunkSize - chunkOffset);
            if (!this.chunks.TryGetValue(chunkIndex, out StagedChunk? chunk))
            {
                chunk = new StagedChunk();
                this.chunks.Add(chunkIndex, chunk);
            }

            chunk.Write(chunkOffset, buffer.Slice(sourceOffset, length));
            sourceOffset += length;
        }

        this.position = end;
    }

    /// <summary>Retains one byte without forwarding it to the caller stream.</summary>
    public override void WriteByte(byte value)
    {
        Span<byte> buffer = stackalloc byte[1];
        buffer[0] = value;
        this.Write(buffer);
    }

    /// <summary>Leaves the caller-owned baseline open when the staging view is released.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.disposed)
        {
            foreach (StagedChunk chunk in this.chunks.Values)
            {
                chunk.Dispose();
            }

            this.chunks.Clear();
            this.disposed = true;
        }

        // Public CStruct operations do not take baseline stream ownership.
        base.Dispose(disposing);
    }

    private static bool IsPhysicalStreamFailure(Exception exception)
    {
        return exception is IOException or NotSupportedException or ObjectDisposedException;
    }

    private static CStructWriteException CreateCommitFailure(
        string message,
        Exception exception,
        Stream destination,
        long attemptedOffset)
    {
        var result = new CStructWriteException(message, exception);
        result.AttachContext(offset: TryGetPosition(destination) ?? attemptedOffset);
        return result;
    }

    private static long? TryGetPosition(Stream stream)
    {
        try
        {
            return stream.Position;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static StagedRange CreateStagedRange(long start, MemoryStream bytes)
    {
        if (!bytes.TryGetBuffer(out ArraySegment<byte> segment) || segment.Array is null)
        {
            throw new InvalidOperationException("Internal update staging buffer is not publicly visible.");
        }

        return new StagedRange(start, segment.Array, checked((int)bytes.Length));
    }

    /// <summary>Enumerates maximal contiguous staged ranges without cloning untouched destination bytes.</summary>
    private IEnumerable<StagedRange> GetStagedRanges()
    {
        long rangeStart = -1;
        long expectedAddress = -1;
        MemoryStream? rangeBytes = null;

        foreach (KeyValuePair<long, StagedChunk> entry in this.chunks)
        {
            long chunkStart = checked(entry.Key * ChunkSize);
            foreach ((int offset, int length) in entry.Value.GetWrittenRuns())
            {
                long start = checked(chunkStart + offset);
                if (rangeBytes is null || start != expectedAddress)
                {
                    if (rangeBytes is not null)
                    {
                        yield return CreateStagedRange(rangeStart, rangeBytes);
                        rangeBytes.Dispose();
                    }

                    rangeStart = start;
                    rangeBytes = new MemoryStream(length);
                }

                rangeBytes.Write(entry.Value.Bytes, offset, length);
                expectedAddress = checked(start + length);
            }
        }

        if (rangeBytes is not null)
        {
            yield return CreateStagedRange(rangeStart, rangeBytes);
            rangeBytes.Dispose();
        }
    }

    private bool TryGetStagedByte(long address, out byte value)
    {
        long chunkIndex = address / ChunkSize;
        int chunkOffset = (int)(address % ChunkSize);
        if (this.chunks.TryGetValue(chunkIndex, out StagedChunk? chunk) &&
            chunk.IsWritten(chunkOffset))
        {
            value = chunk.Bytes[chunkOffset];
            return true;
        }

        value = 0;
        return false;
    }

    private readonly record struct StagedRange(long Start, byte[] Bytes, int Length);

    /// <summary>Stores one fixed sparse page and a compact bitmap that identifies its final written bytes.</summary>
    private sealed class StagedChunk : IDisposable
    {
        private readonly byte[] bytes = ArrayPool<byte>.Shared.Rent(ChunkSize);
        private readonly ulong[] written = new ulong[ChunkSize / 64];
        private bool disposed;
        private int maximumWrittenExclusive;
        private int minimumWritten = ChunkSize;

        public byte[] Bytes => this.bytes;

        public void Dispose()
        {
            if (this.disposed)
            {
                return;
            }

            ArrayPool<byte>.Shared.Return(this.bytes, clearArray: true);
            this.disposed = true;
        }

        public IEnumerable<(int Offset, int Length)> GetWrittenRuns()
        {
            int offset = this.minimumWritten;
            while (offset < this.maximumWrittenExclusive)
            {
                while (offset < this.maximumWrittenExclusive && !this.IsWritten(offset))
                {
                    offset++;
                }

                if (offset == this.maximumWrittenExclusive)
                {
                    yield break;
                }

                int start = offset;
                while (offset < this.maximumWrittenExclusive && this.IsWritten(offset))
                {
                    offset++;
                }

                yield return (start, offset - start);
            }
        }

        public bool IsWritten(int offset)
        {
            int word = offset / 64;
            int bit = offset % 64;
            return (this.written[word] & (1UL << bit)) != 0;
        }

        public void Write(int offset, ReadOnlySpan<byte> source)
        {
            source.CopyTo(this.Bytes.AsSpan(offset, source.Length));
            this.minimumWritten = Math.Min(this.minimumWritten, offset);
            this.maximumWrittenExclusive = Math.Max(this.maximumWrittenExclusive, offset + source.Length);
            for (int index = offset; index < offset + source.Length; index++)
            {
                int word = index / 64;
                int bit = index % 64;
                this.written[word] |= 1UL << bit;
            }
        }
    }
}
