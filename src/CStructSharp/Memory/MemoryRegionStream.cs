namespace CStructSharp.Memory;

/// <summary>A read-only <see cref="Stream"/> view of one <see cref="MemoryRegion"/>, translating local positions to unsigned source addresses.</summary>
/// <remarks>
/// <para>
/// The core CStructSharp codecs read from ordinary streams with signed, zero-based positions. Memory regions use
/// unsigned addresses that may not fit a stream position. This adapter performs the single translation between
/// the two: position <c>p</c> in the view is address <c>Region.Address + p</c> in the source. Every scalar decode
/// in <see cref="MemorySession"/> goes through it, which is how the memory APIs reuse the core codecs instead of
/// implementing a second integer decoder.
/// </para>
/// <para>
/// Reads are clipped to the region, so reaching its end is ordinary end of file. A source that returns zero
/// bytes before that end, however, means the capture is missing data, and the view reports
/// <see cref="MemoryFailure.MissingBytes"/> rather than a shorter stream. The view owns only its position and
/// disposed flag; disposing it never closes the source. It does not reinterpret pointer bytes: a core parser
/// following a pointer through this view uses core stream rules, and unsigned resolution stays with the session.
/// </para>
/// </remarks>
internal sealed class MemoryRegionStream : Stream
{
    private readonly MemoryRegion region;
    private readonly MemoryAccessContext context;
    private long position;
    private bool disposed;

    /// <summary>Creates a non-owning view positioned at zero.</summary>
    /// <param name="region">Finite region to expose; its source is not owned by the view.</param>
    /// <param name="context">Budget charged by every source read the view performs.</param>
    internal MemoryRegionStream(MemoryRegion region, MemoryAccessContext context)
    {
        this.region = region;
        this.context = context;
    }

    /// <summary>Gets whether the view is open; a disposed view can no longer read.</summary>
    public override bool CanRead => !this.disposed;

    /// <summary>Gets whether the view is open; a disposed view can no longer seek.</summary>
    public override bool CanSeek => !this.disposed;

    /// <summary>Gets false: the view never writes; use a <see cref="MemoryPatch"/> and a writable source instead.</summary>
    public override bool CanWrite => false;

    /// <summary>Gets the region length; the view cannot see beyond the region even if the source continues.</summary>
    public override long Length => this.region.Length;

    /// <summary>Gets or sets the local position, from zero (the region's start) to the region length (end of file).</summary>
    public override long Position
    {
        get => this.position;
        set
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);
            if (value < 0 || value > this.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            this.position = value;
        }
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
    {
        return this.Read(buffer.AsSpan(offset, count));
    }

    /// <summary>Reads from the current position, clipping to the region and translating the position to a source address.</summary>
    /// <param name="buffer">Destination; at most the remaining region length is filled.</param>
    /// <returns>The bytes copied, or zero at the region's end.</returns>
    public override int Read(Span<byte> buffer)
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
        this.context.CancellationToken.ThrowIfCancellationRequested();

        // Clip to the region: the count is zero only at the end, which is ordinary EOF for the caller.
        int count = (int)Math.Min(buffer.Length, this.Length - this.position);
        if (count == 0)
        {
            return 0;
        }

        ulong address = checked(this.region.Address + (ulong)this.position);
        int read = this.region.Source.Read(address, buffer[..count], this.context);
        if (read < 0 || read > count)
        {
            throw new MemoryAccessException(MemoryFailure.SourceFailure, this.region.Source.Id, address, count, "Backing source returned an invalid read count.");
        }

        // Inside the region a zero from the source is not EOF; it means the bytes were never captured.
        if (read == 0)
        {
            throw new MemoryAccessException(MemoryFailure.MissingBytes, this.region.Source.Id, address, count, "Backing bytes are unavailable.");
        }

        this.position += read;
        return read;
    }

    /// <summary>Moves the position within the region; seeking outside it throws.</summary>
    /// <param name="offset">Byte offset relative to <paramref name="origin"/>.</param>
    /// <param name="origin">Reference point for the offset.</param>
    /// <returns>The new local position.</returns>
    public override long Seek(long offset, SeekOrigin origin)
    {
        long originPosition = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => this.position,
            SeekOrigin.End => this.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        this.Position = checked(offset + originPosition);
        return this.position;
    }

    /// <summary>Does nothing beyond checking that the view is open; there is no write buffer to flush.</summary>
    public override void Flush()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);
    }

    /// <summary>Rejects changing the caller-selected region extent.</summary>
    /// <param name="value">Requested stream length in bytes; resizing is not supported.</param>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>Rejects writes through a read view; use a patch and an explicit writable source.</summary>
    /// <param name="buffer">Caller-owned byte buffer.</param>
    /// <param name="offset">Byte offset within <paramref name="buffer"/>.</param>
    /// <param name="count">Number of bytes requested for writing; this view rejects all writes.</param>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>Closes only this view; the region's source stays open for its owner.</summary>
    /// <param name="disposing">True when called from <see cref="Stream.Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        this.disposed = true;
        base.Dispose(disposing);
    }
}
