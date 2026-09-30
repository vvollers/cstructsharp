namespace CStructSharp.Engine;

using System;
using System.Buffers;
using System.IO;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>
///     The compiled engine's destination for <c>Serialize</c>: the caller's pinned span, a growable pooled buffer for a new
///     array, or a union's staged storage, together with the operation's output budget. It behaves exactly as a
///     <see cref="WriteBudgetStream"/> over a <see cref="FixedBufferStream"/> (a span), an
///     <see cref="OwnedMemoryStream"/> (a new array) or a fixed <see cref="MemoryStream"/> over the staging array (a union
///     member) would, byte for byte and failure for failure, so a write reports the same outcome into memory as into a
///     stream, without the two wrapper calls per write.
/// </summary>
/// <remarks>
///     <para>
///         <b>Extent.</b> The buffer starts empty (a staging buffer: full of its union's zero or preserved bytes, which it
///         cannot grow past). <see cref="Length"/> is the high-water mark of the bytes written; a read
///         returns nothing past it (a bitfield unit or a static plan's preserved bytes read back zero there), and a write
///         that lands past it first fills the gap with zeroes, as a memory stream does. The caller's span beyond the
///         bytes written is never touched.
///     </para>
///     <para>
///         <b>Budget.</b> Each write is checked before any byte moves: the larger of the physical bytes written so far
///         (plus this write) and the new extent must stay within <see cref="WriteOptions.MaxTotalBytesWritten"/>; seeking
///         charges nothing. Only then is a span destination's capacity checked, so a write that fails both reports the
///         budget, as <see cref="WriteBudgetStream"/> does.
///     </para>
///     <para>
///         It is a <see cref="Stream"/> so the stream-based codec writers (terminated text, LEB128, wide integers) write
///         through it unchanged, and an <see cref="IWriteBudget"/> so they check the per-string limit as they do on a
///         <see cref="WriteBudgetStream"/>. One operation owns it on one thread; a growable buffer returns its array to the pool
///         when disposed.
///     </para>
/// </remarks>
internal sealed unsafe class MemoryWriteBuffer : Stream, IWriteBudget
{
    /// <summary>The largest zero-fill chunk <see cref="WriteZeroes"/> writes at once.</summary>
    private const int ZeroChunk = 8192;

    /// <summary>The capacity a growable buffer rents first; it then doubles.</summary>
    private const int InitialCapacity = 256;

    /// <summary>The largest array a released buffer keeps for the thread's next operation; a larger one goes back to the pool.</summary>
    private const int RetainedCapacity = 64 * 1024;

    // The thread's released buffer, reused by its next operation with the array it had grown (up to RetainedCapacity); an
    // operation that starts while another on the thread holds it (a nested one from a callback) creates its own.
    [ThreadStatic]
    private static MemoryWriteBuffer? spare;

    /// <summary>The caller's first byte for a span destination (which may be <see langword="null"/> for an empty span).</summary>
    private byte* region;

    /// <summary>Whether the destination is the caller's span rather than a growable buffer.</summary>
    private bool span;

    /// <summary>Whether the destination is a union's staged storage: initialized, of a fixed capacity, and not the caller's.</summary>
    private bool staging;

    /// <summary>The destination's length before the operation, which the budget's extent is counted from (a staging buffer's capacity).</summary>
    private long initialLength;

    /// <summary>The span destination's length in bytes; unused for a growable buffer.</summary>
    private int capacity;

    /// <summary>The operation's <see cref="WriteOptions.MaxTotalBytesWritten"/>.</summary>
    private long maxTotalBytesWritten;

    /// <summary>The growable buffer's rented array, or <see langword="null"/> for a span or before the first write.</summary>
    private byte[]? array;

    /// <summary>The position in bytes from the destination's start.</summary>
    private long position;

    /// <summary>The high-water mark: the number of leading bytes that hold written data.</summary>
    private long length;

    /// <summary>The physical bytes written so far, counted as <see cref="WriteBudgetStream"/> counts them.</summary>
    private long bytesWritten;

    /// <summary>Creates a buffer; <see cref="ForSpan"/>, <see cref="ForNewArray"/> and <see cref="ForStaging"/> name the three forms.</summary>
    /// <param name="span">Whether the destination is the caller's span.</param>
    /// <param name="region">The caller's pinned first byte; unused for any other buffer.</param>
    /// <param name="staging">The staging array of a union, or <see langword="null"/>.</param>
    /// <param name="capacity">The span's or staging storage's length in bytes.</param>
    /// <param name="options">The operation's snapshotted options, which supply the byte limits.</param>
    private MemoryWriteBuffer(bool span, byte* region, byte[]? staging, int capacity, WriteOptions options)
    {
        this.Start(span, region, staging, capacity, options);
    }

    /// <summary>Gets the configured per-string encoded-byte budget.</summary>
    public long MaxStringBytes { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether a block may be written in one piece instead of element by element (a typed
    ///     array, narrow text): only into a span or a growable buffer, never into a union's staging, where the golden
    ///     outcomes pin element-by-element writes.
    /// </summary>
    public bool AllowsBlocks => !this.staging;

    /// <summary>Gets a value indicating whether the buffer can read back what it holds; always <see langword="true"/>.</summary>
    public override bool CanRead => true;

    /// <summary>Gets a value indicating whether the buffer can seek; always <see langword="true"/>.</summary>
    public override bool CanSeek => true;

    /// <summary>Gets a value indicating whether the buffer can write; always <see langword="true"/>.</summary>
    public override bool CanWrite => true;

    /// <summary>Gets the high-water mark in bytes: the length of the serialized output.</summary>
    public override long Length => this.length;

    /// <summary>
    ///     Gets or sets the position in bytes; moving it charges nothing. A span rejects a position outside it as a
    ///     <see cref="FixedBufferStream"/> does; a growable buffer rejects what a <see cref="MemoryStream"/> rejects.
    /// </summary>
    public override long Position
    {
        get => this.position;
        set
        {
            if (this.span)
            {
                if (value < 0 || value > this.capacity)
                {
                    throw new CStructWriteException("The requested position is outside the supplied memory region.");
                }
            }
            else if (value < 0 || value > int.MaxValue)
            {
                // A probe memory stream's own check throws the exact exception a memory stream reports for this position.
                using var probe = new MemoryStream();
                probe.Position = value;
            }

            this.position = value;
        }
    }

    /// <summary>Creates the destination of <c>Serialize(Span)</c> over the caller's pinned span.</summary>
    /// <param name="region">The span's first byte; the caller keeps it pinned for the buffer's lifetime.</param>
    /// <param name="capacity">The span's length in bytes.</param>
    /// <param name="options">The operation's snapshotted options.</param>
    /// <returns>An empty buffer over the span.</returns>
    public static MemoryWriteBuffer ForSpan(byte* region, int capacity, WriteOptions options) => Take(true, region, capacity, options);

    /// <summary>Creates the growable destination of <c>Serialize</c> to a new array.</summary>
    /// <param name="options">The operation's snapshotted options.</param>
    /// <returns>An empty buffer.</returns>
    public static MemoryWriteBuffer ForNewArray(WriteOptions options) => Take(false, null, 0, options);

    /// <summary>
    ///     Creates the destination a union member is staged into: <paramref name="size"/> initialized bytes of
    ///     <paramref name="storage"/> (the caller clears or fills them and keeps the array), with a budget of its own whose
    ///     extent counts only growth past them.
    /// </summary>
    /// <param name="storage">The staging array, at least <paramref name="size"/> bytes; not returned to any pool here.</param>
    /// <param name="size">The union's size in bytes.</param>
    /// <param name="options">The operation's snapshotted options.</param>
    /// <returns>The staging buffer, positioned at the union's first byte.</returns>
    public static MemoryWriteBuffer ForStaging(byte[] storage, int size, WriteOptions options) => new(false, null, storage, size, options);

    /// <summary>Has nothing to flush.</summary>
    public override void Flush()
    {
    }

    /// <summary>Returns the bytes written, <see cref="Length"/> of them, as a new array.</summary>
    /// <returns>The serialized output.</returns>
    public byte[] ToArray()
    {
        if (this.length == 0)
        {
            return [];
        }

        return this.span
                   ? new ReadOnlySpan<byte>(this.region, (int)this.length).ToArray()
                   : this.array.AsSpan(0, (int)this.length).ToArray();
    }

    /// <summary>
    ///     Whether <paramref name="size"/> bytes at the position fit the budget, charged as <paramref name="chargedBytes"/> of
    ///     physical traffic, and - for a span - its capacity: the checks made before a block is written.
    /// </summary>
    /// <param name="size">The block's length in bytes, which decides how far it extends the output.</param>
    /// <param name="chargedBytes">The physical traffic in bytes the block adds to the budget.</param>
    /// <returns>Whether the block can be written without a failure.</returns>
    public bool CanAffordBlock(int size, int chargedBytes)
    {
        try
        {
            long next = checked(this.bytesWritten + chargedBytes);
            long extent = Math.Max(0, checked(checked(this.position + size) - this.initialLength));
            if (Math.Max(next, extent) > this.maxTotalBytesWritten)
            {
                return false;
            }
        }
        catch (OverflowException)
        {
            return false;
        }

        return !this.span || this.position + size <= this.capacity;
    }

    /// <summary>Writes a block prepared in advance (a static plan, a typed array, text), charging <paramref name="chargedBytes"/>.</summary>
    /// <param name="block">The bytes, written at the position.</param>
    /// <param name="chargedBytes">The physical traffic in bytes to charge, which may differ from the block's length.</param>
    /// <exception cref="CStructWriteLimitException">The block would exceed the total output budget.</exception>
    /// <exception cref="CStructWriteException">A span destination cannot hold the block.</exception>
    public void WriteBlock(ReadOnlySpan<byte> block, int chargedBytes)
    {
        long next;
        try
        {
            next = checked(this.bytesWritten + chargedBytes);
            this.EnsureWithinBudget(next, Math.Max(0, checked(checked(this.position + block.Length) - this.initialLength)));
        }
        catch (OverflowException exception)
        {
            throw new CStructWriteException("Write output accounting overflowed the supported stream range.", exception);
        }

        this.Store(block);
        this.bytesWritten = next;
    }

    /// <summary>Writes bytes at the position after the budget and the destination's room are checked.</summary>
    /// <param name="buffer">The bytes to write.</param>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        // The common write - at or before the high-water mark, with room, within the budget - copies straight in. Positions
        // and counts stay far below the range where the budget's checked arithmetic could overflow: a span or staging
        // position is bounded by its capacity, a growable one by the largest array.
        long position = this.position;
        long end = position + buffer.Length;
        long next = this.bytesWritten + buffer.Length;
        if (position <= this.length && Math.Max(next, end - this.initialLength) <= this.maxTotalBytesWritten &&
            (this.span ? end <= this.capacity : !this.staging && this.array is { } array && end <= array.Length))
        {
            buffer.CopyTo(this.span ? new Span<byte>(this.region + position, buffer.Length) : this.array.AsSpan((int)position, buffer.Length));
            this.position = end;
            if (end > this.length)
            {
                this.length = end;
            }

            this.bytesWritten = next;
            return;
        }

        this.WriteChecked(buffer);
    }

    /// <summary>Writes bytes at the position through the full checks: the budget, the destination's room, the gap, then the data.</summary>
    /// <param name="buffer">The bytes to write.</param>
    private void WriteChecked(ReadOnlySpan<byte> buffer)
    {
        long next = this.ProjectUsage(buffer.Length);
        this.Store(buffer);
        this.bytesWritten = next;
    }

    /// <summary>Writes a byte range at the position after the budget and the destination's room are checked.</summary>
    /// <param name="buffer">The array holding the bytes.</param>
    /// <param name="offset">The index of the first byte to write.</param>
    /// <param name="count">The number of bytes to write.</param>
    public override void Write(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        this.Write(buffer.AsSpan(offset, count));
    }

    /// <summary>Writes one byte at the position after the budget and the destination's room are checked.</summary>
    /// <param name="value">The byte.</param>
    public override void WriteByte(byte value) => this.Write(new ReadOnlySpan<byte>(in value));

    /// <summary>
    ///     Writes <paramref name="count"/> zero bytes, checking the whole region against the budget first and then writing
    ///     it in chunks (a span can run out of room between chunks).
    /// </summary>
    /// <param name="count">The number of zero bytes; zero writes nothing.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
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

        _ = this.ProjectUsage(count);
        while (count > 0)
        {
            int chunk = Math.Min(count, ZeroChunk);
            long next = this.ProjectUsage(chunk);
            this.StoreZeroes(chunk);
            this.bytesWritten = next;
            count -= chunk;
        }
    }

    /// <summary>Rejects a string before its encoded payload is allocated or written.</summary>
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

    /// <summary>Reads written bytes from the position; nothing past the high-water mark.</summary>
    /// <param name="buffer">The span to fill.</param>
    /// <returns>The number of bytes copied, or 0 at or past <see cref="Length"/>.</returns>
    public override int Read(Span<byte> buffer)
    {
        int count = (int)Math.Min(buffer.Length, Math.Max(0, this.length - this.position));
        if (count == 0)
        {
            return 0;
        }

        this.Bytes(this.position, count).CopyTo(buffer);
        this.position += count;
        return count;
    }

    /// <summary>Reads written bytes from the position; nothing past the high-water mark.</summary>
    /// <param name="buffer">The array receiving the bytes.</param>
    /// <param name="offset">The index of the first byte stored.</param>
    /// <param name="count">The maximum number of bytes to read.</param>
    /// <returns>The number of bytes copied.</returns>
    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return this.Read(buffer.AsSpan(offset, count));
    }

    /// <summary>
    ///     Not used: the engine and the codec writers it calls move the position through <see cref="Position"/>, which
    ///     checks it.
    /// </summary>
    /// <param name="offset">The signed distance in bytes.</param>
    /// <param name="origin">The reference point.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("The compiled engine's write buffer moves only through its position.");

    /// <summary>Not used: nothing the engine writes changes the destination's length other than by writing.</summary>
    /// <param name="value">The new length in bytes.</param>
    /// <exception cref="NotSupportedException">Always.</exception>
    public override void SetLength(long value) => throw new NotSupportedException("The compiled engine's write buffer grows only by writing.");

    /// <summary>
    ///     Releases the buffer: a span or growable buffer becomes the thread's spare for its next operation, keeping an array
    ///     of up to <see cref="RetainedCapacity"/> bytes (a larger one goes back to the pool); a staging array stays its
    ///     owner's. The buffer must not be used afterwards.
    /// </summary>
    /// <param name="disposing">Whether the call comes from <see cref="Stream.Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (this.staging)
        {
            this.array = null;
        }
        else
        {
            if (this.array is { Length: > RetainedCapacity, } rented)
            {
                this.array = null;
                ArrayPool<byte>.Shared.Return(rented);
            }

            this.region = null;
            spare = this;
        }

        base.Dispose(disposing);
    }

    /// <summary>Returns the thread's spare buffer started for an operation, or a new one.</summary>
    /// <param name="span">Whether the destination is the caller's span.</param>
    /// <param name="region">The span's pinned first byte.</param>
    /// <param name="capacity">The span's length in bytes.</param>
    /// <param name="options">The operation's snapshotted options.</param>
    /// <returns>An empty buffer.</returns>
    private static MemoryWriteBuffer Take(bool span, byte* region, int capacity, WriteOptions options)
    {
        MemoryWriteBuffer? buffer = spare;
        if (buffer is null)
        {
            return new MemoryWriteBuffer(span, region, null, capacity, options);
        }

        spare = null;
        buffer.Start(span, region, null, capacity, options);
        return buffer;
    }

    /// <summary>Resets every field for a new operation; a growable buffer keeps the array it already holds.</summary>
    /// <param name="span">Whether the destination is the caller's span.</param>
    /// <param name="region">The span's pinned first byte.</param>
    /// <param name="staging">The staging array of a union, or <see langword="null"/>.</param>
    /// <param name="capacity">The span's or staging storage's length in bytes.</param>
    /// <param name="options">The operation's snapshotted options.</param>
    private void Start(bool span, byte* region, byte[]? staging, int capacity, WriteOptions options)
    {
        this.span = span;
        this.region = region;
        this.capacity = capacity;
        this.staging = staging is not null;
        this.array = staging ?? this.array;
        this.length = this.staging ? capacity : 0;
        this.initialLength = this.length;
        this.position = 0;
        this.bytesWritten = 0;
        this.maxTotalBytesWritten = options.MaxTotalBytesWritten;
        this.MaxStringBytes = options.MaxStringBytes;
    }

    /// <summary>
    ///     The budget check of one write of <paramref name="count"/> bytes at the position, as
    ///     <see cref="WriteBudgetStream"/> projects it; nothing changes.
    /// </summary>
    /// <param name="count">The bytes about to be written.</param>
    /// <returns>The physical byte count after the write.</returns>
    private long ProjectUsage(int count)
    {
        try
        {
            long next = checked(this.bytesWritten + count);
            this.EnsureWithinBudget(next, Math.Max(0, checked(checked(this.position + count) - this.initialLength)));
            return next;
        }
        catch (OverflowException exception)
        {
            throw new CStructWriteException("Write output accounting overflowed the supported stream range.", exception);
        }
    }

    /// <summary>Uses the larger of physical traffic and new extent, so neither repeated writes nor gaps bypass the limit.</summary>
    /// <param name="physicalBytes">The physical bytes written, including the write being checked.</param>
    /// <param name="newExtent">The output's extent after the write.</param>
    /// <exception cref="CStructWriteLimitException">The larger of the two exceeds the limit.</exception>
    private void EnsureWithinBudget(long physicalBytes, long newExtent)
    {
        if (Math.Max(physicalBytes, newExtent) > this.maxTotalBytesWritten)
        {
            throw new CStructWriteLimitException(WriteFailures.TotalBytesLimit);
        }
    }

    /// <summary>Copies bytes to the position once the budget allowed them: the destination's room, the gap, then the data.</summary>
    /// <param name="source">The bytes.</param>
    private void Store(ReadOnlySpan<byte> source)
    {
        this.Room(source.Length);
        this.FillGap();
        source.CopyTo(this.Bytes(this.position, source.Length));
        this.Advance(source.Length);
    }

    /// <summary>Writes zero bytes at the position once the budget allowed them.</summary>
    /// <param name="count">The number of zero bytes.</param>
    private void StoreZeroes(int count)
    {
        this.Room(count);
        this.FillGap();
        this.Bytes(this.position, count).Clear();
        this.Advance(count);
    }

    /// <summary>
    ///     Makes room for <paramref name="count"/> bytes at the position: a span that cannot hold them fails as a
    ///     <see cref="FixedBufferStream"/> does, a growable buffer grows (and fails past the largest array as a memory
    ///     stream), and a union's staging buffer refuses to grow past its union as a fixed memory stream does.
    /// </summary>
    /// <param name="count">The bytes about to be written.</param>
    private void Room(int count)
    {
        if (this.span)
        {
            if (count > this.capacity - this.position)
            {
                throw new CStructWriteException(WriteFailures.DestinationCapacity);
            }

            return;
        }

        // A memory stream (growable, or fixed over a union's staging) first refuses a write that would end past the
        // largest array with an I/O error, which a budget wrapper reports as a write failure at the position; the probe
        // raises the same error.
        long end = this.position + count;
        if (end > int.MaxValue)
        {
            try
            {
                using var probe = new MemoryStream();
                probe.Position = this.position;
                probe.Write(new byte[count], 0, count);
            }
            catch (IOException exception)
            {
                var failure = new CStructWriteException("Cannot write to the destination stream.", exception);
                failure.AttachContext(offset: this.position);
                throw failure;
            }
        }

        if (this.staging && end > this.capacity)
        {
            // A staging stream cannot grow past its union either; the probe raises its refusal.
            try
            {
                using var probe = new MemoryStream([], writable: true);
                probe.WriteByte(0);
            }
            catch (NotSupportedException exception)
            {
                var failure = new CStructWriteException("Cannot write to the destination stream.", exception);
                failure.AttachContext(offset: this.position);
                throw failure;
            }
        }

        this.Reserve(end);
    }

    /// <summary>Grows a growable buffer's array to hold at least <paramref name="size"/> bytes, keeping the bytes written.</summary>
    /// <param name="size">The bytes the array must hold.</param>
    private void Reserve(long size)
    {
        if (this.span || this.staging || (this.array is { } current && current.Length >= size))
        {
            return;
        }

        int grown = (int)Math.Min(int.MaxValue, Math.Max(size, Math.Max(InitialCapacity, (long)(this.array?.Length ?? 0) * 2)));
        byte[] replacement = ArrayPool<byte>.Shared.Rent(grown);
        if (this.array is { } old)
        {
            old.AsSpan(0, (int)this.length).CopyTo(replacement);
            ArrayPool<byte>.Shared.Return(old);
        }

        this.array = replacement;
    }

    /// <summary>Clears the bytes between the high-water mark and a position past it, which a write there turns into data.</summary>
    private void FillGap()
    {
        if (this.position > this.length)
        {
            this.Bytes(this.length, (int)(this.position - this.length)).Clear();
        }
    }

    /// <summary>Moves the position past bytes just stored and raises the high-water mark to it.</summary>
    /// <param name="count">The bytes stored.</param>
    private void Advance(int count)
    {
        this.position += count;
        if (this.position > this.length)
        {
            this.length = this.position;
        }
    }

    /// <summary>The destination's bytes from <paramref name="start"/>; the caller has made room for them.</summary>
    /// <param name="start">The first byte's offset.</param>
    /// <param name="count">The number of bytes.</param>
    /// <returns>The bytes.</returns>
    private Span<byte> Bytes(long start, int count)
        => this.span ? new Span<byte>(this.region + start, count) : this.array.AsSpan((int)start, count);
}
