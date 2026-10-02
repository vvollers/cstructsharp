namespace CStructSharp.Streams;

using System;
using System.IO;
using System.Runtime.CompilerServices;
using CStructSharp.Diagnostics;

/// <summary>
///     The read state one operation shares between <see cref="ReadBudgetStream"/> and the engine's
///     <c>MemoryReadCursor</c>: the total read budget and, for a memory source, the pinned region or exposed array, the
///     position and the input length. Both readers delegate every memory-mode read, seek and budget charge here, so a
///     byte is served, a position is checked and a failure is worded the same way whichever of them reads it.
/// </summary>
/// <remarks>
///     A mutable struct owned by one operation: keep it in a field and call its members through that field, never
///     through a copy, or the copy's position and charges are lost. A memory region stays valid only while its owner
///     keeps it pinned. A core made by <see cref="ForStream"/> is not memory-backed: it only charges the budget and
///     remembers the largest stream length observed; its memory members then report nothing available.
/// </remarks>
internal unsafe struct MemoryReadCore
{
    private readonly byte* memoryPointer;
    private readonly byte[]? memoryArray;
    private readonly int memoryArrayOffset;
    private readonly bool memoryBacked;
    private readonly long maxTotalBytesRead;
    private long bytesRead;

    // A memory-backed source's length; for a stream source, the largest stream length observed so far (-1 until the
    // first position that needs it), so a position up to it needs no length query.
    private long knownLength;
    private long position;

    /// <summary>Creates a core over a pinned region, an array segment, or neither (a stream source).</summary>
    /// <param name="pointer">The region's first byte, or null.</param>
    /// <param name="array">The array holding the input, or null.</param>
    /// <param name="arrayOffset">The index of the input's byte 0 within <paramref name="array"/>.</param>
    /// <param name="length">The input length in bytes, or -1 for a stream source.</param>
    /// <param name="position">The starting position in bytes from the input's byte 0.</param>
    /// <param name="memoryBacked">Whether <paramref name="pointer"/> or <paramref name="array"/> holds the input.</param>
    /// <param name="maxTotalBytesRead">The largest number of bytes the whole operation may read.</param>
    private MemoryReadCore(byte* pointer, byte[]? array, int arrayOffset, long length, long position, bool memoryBacked, long maxTotalBytesRead)
    {
        this.memoryPointer = pointer;
        this.memoryArray = array;
        this.memoryArrayOffset = arrayOffset;
        this.knownLength = length;
        this.position = position;
        this.memoryBacked = memoryBacked;
        this.maxTotalBytesRead = maxTotalBytesRead;
        this.bytesRead = 0;
    }

    /// <summary>Gets a value indicating whether the input lives in memory this core reads directly.</summary>
    public readonly bool IsMemoryBacked => this.memoryBacked;

    /// <summary>
    ///     Gets or sets the input length in bytes for a memory source; for a stream source, the largest stream length
    ///     observed so far, or -1 before the first one. Only a stream source's owner updates it.
    /// </summary>
    public long KnownLength
    {
        readonly get => this.knownLength;
        set => this.knownLength = value;
    }

    /// <summary>Gets the memory-mode position in bytes from the input's byte 0.</summary>
    public readonly long Position => this.position;

    /// <summary>
    ///     Gets the bytes the operation may still consume before the total read budget fails: a scan that inspects bytes
    ///     before they are consumed (an array's search for its terminator) stops where consuming them would fail.
    /// </summary>
    public readonly long RemainingBudget => this.maxTotalBytesRead - this.bytesRead;

    /// <summary>
    ///     Selects the backing of <paramref name="source"/>: a read-only pinned <see cref="FixedBufferStream"/> region
    ///     or an exposable, seekable <see cref="MemoryStream"/> buffer is read directly from memory, starting at the
    ///     stream's current position; any other stream gets a budget-only core (<see cref="ForStream"/>).
    /// </summary>
    /// <param name="source">The caller-owned source; its position is read, never changed.</param>
    /// <param name="maxTotalBytesRead">The largest number of bytes the whole operation may read.</param>
    /// <returns>The core for the operation.</returns>
    public static MemoryReadCore Over(Stream source, long maxTotalBytesRead)
    {
        // Exactly one memory backing is chosen; any other source is read through the stream itself, and the core only
        // charges the budget.
        if (source is FixedBufferStream fixedBuffer && fixedBuffer.TryGetReadOnlyRegion(out byte* region, out long regionLength))
        {
            return OverRegion(region, regionLength, fixedBuffer.Position, maxTotalBytesRead);
        }

        if (source is MemoryStream memoryStream && memoryStream.CanSeek && memoryStream.TryGetBuffer(out ArraySegment<byte> segment))
        {
            return OverArray(segment.Array!, segment.Offset, memoryStream.Length, memoryStream.Position, maxTotalBytesRead);
        }

        return ForStream(maxTotalBytesRead);
    }

    /// <summary>Creates a memory-backed core over a region its owner keeps pinned for the core's lifetime.</summary>
    /// <param name="region">The input's byte 0.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="position">The starting position in bytes from byte 0; at most <paramref name="length"/>.</param>
    /// <param name="maxTotalBytesRead">The largest number of bytes the whole operation may read.</param>
    /// <returns>The core.</returns>
    public static MemoryReadCore OverRegion(byte* region, long length, long position, long maxTotalBytesRead)
        => new(region, null, 0, length, position, memoryBacked: true, maxTotalBytesRead);

    /// <summary>Creates a memory-backed core over part of an array, such as an exposed <see cref="MemoryStream"/> buffer.</summary>
    /// <param name="array">The array holding the input.</param>
    /// <param name="offset">The index of the input's byte 0 within <paramref name="array"/>.</param>
    /// <param name="length">The input length in bytes; the input ends within the array.</param>
    /// <param name="position">The starting position in bytes from byte 0.</param>
    /// <param name="maxTotalBytesRead">The largest number of bytes the whole operation may read.</param>
    /// <returns>The core.</returns>
    public static MemoryReadCore OverArray(byte[] array, int offset, long length, long position, long maxTotalBytesRead)
        => new(null, array, offset, length, position, memoryBacked: true, maxTotalBytesRead);

    /// <summary>Creates a core that only charges the budget and tracks the observed length of a stream source.</summary>
    /// <param name="maxTotalBytesRead">The largest number of bytes the whole operation may read.</param>
    /// <returns>The core, not memory-backed, with no length observed yet.</returns>
    public static MemoryReadCore ForStream(long maxTotalBytesRead)
        => new(null, null, 0, -1, 0, memoryBacked: false, maxTotalBytesRead);

    /// <summary>
    ///     Moves the memory-mode position. The position may reach the end of the input but not pass it, because the
    ///     input then provably lacks bytes a layout places there.
    /// </summary>
    /// <param name="value">The new position in bytes from the input's byte 0.</param>
    /// <exception cref="CStructReadException">The position is negative or lies past the end of the input.</exception>
    public void SetPosition(long value)
    {
        if (value < 0 || value > this.knownLength)
        {
            throw new CStructReadException(ReadFailures.OutsideRegion);
        }

        this.position = value;
    }

    /// <summary>Moves the memory-mode position relative to the start, the current position, or the end of the input.</summary>
    /// <param name="offset">The byte offset relative to <paramref name="origin"/>.</param>
    /// <param name="origin">The reference point.</param>
    /// <returns>The new position in bytes from the input's byte 0.</returns>
    /// <exception cref="CStructReadException">The target lies outside the input.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="origin"/> is not a defined value.</exception>
    /// <exception cref="OverflowException">The target position overflows.</exception>
    public long Seek(long offset, SeekOrigin origin)
    {
        long basis = origin switch
        {
            SeekOrigin.Begin => 0,
            SeekOrigin.Current => this.position,
            SeekOrigin.End => this.knownLength,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        this.SetPosition(checked(basis + offset));
        return this.position;
    }

    /// <summary>Reports whether the memory input provably cannot supply <paramref name="count"/> more bytes.</summary>
    /// <param name="count">The nonnegative number of requested bytes.</param>
    /// <returns>Whether fewer than <paramref name="count"/> bytes remain after the position.</returns>
    public readonly bool IsShortBy(long count)
    {
        // Subtraction cannot overflow for nonnegative lengths and positions; adding the requested count can.
        return count > this.knownLength - this.position;
    }

    /// <summary>
    ///     Serves <paramref name="count"/> bytes straight from memory at the position, advancing and then charging the
    ///     budget exactly like a read would. False for a stream source or when the bytes are not all available.
    /// </summary>
    /// <param name="count">The nonnegative number of requested bytes.</param>
    /// <param name="bytes">The borrowed input bytes on success, or an empty span on failure.</param>
    /// <returns>Whether the bytes were available in memory; false leaves the position and budget unchanged.</returns>
    /// <exception cref="CStructReadLimitException">The bytes exceed the total read budget (they are consumed).</exception>
    public bool TryReadSpan(int count, out ReadOnlySpan<byte> bytes)
    {
        if (this.memoryBacked && count <= this.knownLength - this.position)
        {
            bytes = this.memoryArray is null
                        ? new ReadOnlySpan<byte>(this.memoryPointer + this.position, count)
                        : new ReadOnlySpan<byte>(this.memoryArray, this.memoryArrayOffset + (int)this.position, count);
            this.position += count;
            this.Charge(count);
            return true;
        }

        bytes = default;
        return false;
    }

    /// <summary>
    ///     The bytes from the position to the end of a memory input, without consuming or charging them; false for a
    ///     stream source. Pair with <see cref="Advance"/> once the consumer knows how many it used.
    /// </summary>
    /// <param name="bytes">The borrowed remaining bytes (at most <see cref="int.MaxValue"/>), or an empty span.</param>
    /// <returns>Whether the input is in memory and the remaining bytes were exposed.</returns>
    public readonly bool TryPeekRemaining(out ReadOnlySpan<byte> bytes)
    {
        if (this.memoryBacked && this.position <= this.knownLength)
        {
            int count = (int)Math.Min(this.knownLength - this.position, int.MaxValue);
            bytes = this.memoryArray is null
                        ? new ReadOnlySpan<byte>(this.memoryPointer + this.position, count)
                        : new ReadOnlySpan<byte>(this.memoryArray, this.memoryArrayOffset + (int)this.position, count);
            return true;
        }

        bytes = default;
        return false;
    }

    /// <summary>Consumes <paramref name="count"/> bytes that <see cref="TryPeekRemaining"/> exposed, charging them like a read.</summary>
    /// <param name="count">The number of bytes consumed from the peeked span.</param>
    /// <exception cref="CStructReadLimitException">The advance exceeds the operation's total read budget.</exception>
    public void Advance(int count)
    {
        this.position += count;
        this.Charge(count);
    }

    /// <summary>
    ///     Like <see cref="TryReadSpan"/> but also fails, without charging or throwing, when the read would exceed the
    ///     total read budget - so a caller can fall back to a path that reports the limit failure at its usual place.
    /// </summary>
    /// <param name="count">The nonnegative number of requested bytes.</param>
    /// <param name="bytes">The borrowed input bytes on success, or an empty span on failure.</param>
    /// <returns>Whether the bytes were available within the budget; false consumes neither bytes nor budget.</returns>
    public bool TryReadSpanWithinBudget(int count, out ReadOnlySpan<byte> bytes)
    {
        if (this.memoryBacked && count <= this.knownLength - this.position && count <= this.maxTotalBytesRead - this.bytesRead)
        {
            return this.TryReadSpan(count, out bytes);
        }

        bytes = default;
        return false;
    }

    /// <summary>Reports whether charging <paramref name="count"/> more bytes stays within the total read budget.</summary>
    /// <param name="count">The nonnegative number of bytes a read would charge.</param>
    /// <returns>Whether the charge would not exceed the budget.</returns>
    public readonly bool IsWithinBudget(long count) => count <= this.maxTotalBytesRead - this.bytesRead;

    /// <summary>
    ///     Gives back the charge of <paramref name="count"/> bytes a reader took but did not consume - the look-ahead of a
    ///     terminated string after its terminator - so the total counts each consumed byte once.
    /// </summary>
    /// <param name="count">The nonnegative number of bytes, at most the bytes charged so far.</param>
    public void Refund(long count) => this.bytesRead -= count;

    /// <summary>
    ///     Copies up to <c>buffer.Length</c> bytes from memory at the position, advancing and charging only the bytes
    ///     copied - the memory-mode form of <see cref="Stream.Read(Span{byte})"/>.
    /// </summary>
    /// <param name="buffer">The span that receives the bytes.</param>
    /// <returns>The number of bytes copied, which is 0 at the end of the input.</returns>
    /// <exception cref="CStructReadLimitException">The copied bytes exceed the total read budget (they are consumed).</exception>
    public int Read(Span<byte> buffer)
    {
        int available = (int)Math.Min(buffer.Length, Math.Max(0, this.knownLength - this.position));
        if (available > 0)
        {
            ReadOnlySpan<byte> source = this.memoryArray is null
                                            ? new ReadOnlySpan<byte>(this.memoryPointer + this.position, available)
                                            : new ReadOnlySpan<byte>(this.memoryArray, this.memoryArrayOffset + (int)this.position, available);
            source.CopyTo(buffer);
            this.position += available;
            this.Charge(available);
        }

        return available;
    }

    /// <summary>Reads one byte from memory at the position, charging it like any read.</summary>
    /// <returns>The byte value from 0 to 255, or -1 at the end of the input (nothing charged).</returns>
    /// <exception cref="CStructReadLimitException">The byte exceeds the total read budget (it is consumed).</exception>
    public int ReadByte()
    {
        if (this.position >= this.knownLength)
        {
            return -1;
        }

        byte result = this.memoryArray is null
                          ? this.memoryPointer[this.position]
                          : this.memoryArray[this.memoryArrayOffset + (int)this.position];
        this.position++;
        this.Charge(1);
        return result;
    }

    /// <summary>
    ///     Adds <paramref name="count"/> bytes to the operation's total. The total counts every byte consumed, once per
    ///     read of it (a pointer target read twice is charged twice); a read is charged after it happened, so the failing
    ///     read has already consumed its bytes.
    /// </summary>
    /// <param name="count">The number of bytes just read; zero or less charges nothing.</param>
    /// <exception cref="CStructReadLimitException">The total exceeds the budget or the accounting range.</exception>
    /// <remarks>
    ///     This runs on every memory and budget-stream read, so the common case is one comparison against the remaining
    ///     budget; everything else goes to <see cref="ChargeSlow"/>. The subtraction cannot overflow because the operation
    ///     boundary rejects a negative budget and <see cref="Refund"/> only gives back bytes that were charged, so both
    ///     operands are nonnegative.
    /// </remarks>
    public void Charge(long count)
    {
        if (count > this.maxTotalBytesRead - this.bytesRead)
        {
            this.ChargeSlow(count);
        }
        else if (count > 0)
        {
            this.bytesRead += count;
        }
    }

    /// <summary>
    ///     Handles a <see cref="Charge"/> that does not fit the remaining budget: it records the overshoot and throws, or
    ///     reports the accounting-range failure without changing the total, or charges nothing for a count of zero or less
    ///     (an empty read after an earlier charge already went over the budget).
    /// </summary>
    /// <param name="count">The number of bytes just read.</param>
    /// <remarks>
    ///     Kept out of line so the hot <see cref="Charge"/> stays small. The overshoot stays recorded on purpose: a caller
    ///     that refunds the budget it observed changing (a terminated array's scan) gives back exactly what was charged.
    /// </remarks>
    /// <exception cref="CStructReadLimitException">The total exceeds the budget or the accounting range.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ChargeSlow(long count)
    {
        if (count <= 0)
        {
            return;
        }

        try
        {
            this.bytesRead = checked(this.bytesRead + count);
        }
        catch (OverflowException exception)
        {
            throw new CStructReadLimitException(
                "Read operation exceeded the supported read-byte accounting range.",
                exception);
        }

        throw new CStructReadLimitException(ReadFailures.TotalBytesLimit);
    }
}
