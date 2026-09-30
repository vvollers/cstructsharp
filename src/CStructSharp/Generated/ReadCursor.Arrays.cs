namespace CStructSharp.Generated;

using System;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;

/// <summary>Consuming arrays: element runs charged against the byte budget, element counts to the end or to a terminator, and the element limit.</summary>
public ref partial struct ReadCursor
{
    /// <summary>
    ///     Consumes a whole numeric array as the runtime's bulk array reader does: the extent is checked before any
    ///     byte is read, and a short read names the element count and size.
    /// </summary>
    /// <param name="count">The element count.</param>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The array's bytes.</returns>
    /// <exception cref="CStructReadException">The array does not fit in the remaining bytes.</exception>
    /// <exception cref="CStructReadLimitException">The total read budget is exceeded.</exception>
    public ReadOnlySpan<byte> TakeArray(int count, int elementSize, string member, string? memberType)
    {
        long total = (long)count * elementSize;
        if (total > this.Remaining)
        {
            int available = this.Remaining;
            this.position = this.source.Length;
            throw this.Fail(ReadFailures.ArrayShortRead(count, elementSize, available), member, memberType);
        }

        return this.Take((int)total, member, memberType);
    }

    /// <summary>
    ///     Consumes a multidimensional numeric array as the runtime's block reader does: the extent is read in
    ///     blocks of at most 64 KiB, and a short read names the block that did not fit.
    /// </summary>
    /// <param name="count">The total element count.</param>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The array's bytes.</returns>
    public ReadOnlySpan<byte> TakeInBlocks(int count, int elementSize, string member, string? memberType)
    {
        long total = (long)count * elementSize;
        if (total <= 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        int blockCapacity = (int)Math.Min(total, BlockSize.Bytes) / elementSize * elementSize;
        long remainingElementsBytes = total;
        int start = this.position;
        while (remainingElementsBytes > 0)
        {
            this.settings.CancellationToken.ThrowIfCancellationRequested();
            int blockLength = (int)Math.Min(remainingElementsBytes, blockCapacity);
            if (blockLength > this.Remaining)
            {
                int available = this.Remaining;
                this.position = this.source.Length;
                throw this.Fail(ReadFailures.ShortRead(blockLength, available), member, memberType);
            }

            this.Charge(blockLength, member, memberType);
            this.position += blockLength;
            remainingElementsBytes -= blockLength;
        }

        return this.source.Slice(start, (int)total);
    }

    /// <summary>
    ///     Consumes <paramref name="count"/> elements of <paramref name="elementSize"/> bytes as the runtime's
    ///     per-element loop does: a short read reports the element size as the need and what was left after the
    ///     last whole element, and a budget failure surfaces after the element that crossed the limit.
    /// </summary>
    /// <param name="count">The element count.</param>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The elements' bytes.</returns>
    /// <exception cref="CStructReadException">An element does not fit in the remaining bytes.</exception>
    /// <exception cref="CStructReadLimitException">The total read budget is exceeded.</exception>
    public ReadOnlySpan<byte> TakeElements(int count, int elementSize, string member, string? memberType)
    {
        this.settings.CancellationToken.ThrowIfCancellationRequested();
        if (count <= 0 || elementSize <= 0)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        long total = (long)count * elementSize;
        long available = this.Remaining;
        long wholeElements = available / elementSize;
        long allowed = Math.Max(0, this.settings.MaxTotalBytesRead - this.bytesRead);
        long affordableElements = allowed / elementSize;
        if (total > available && wholeElements <= affordableElements)
        {
            int leftover = (int)(available - (wholeElements * elementSize));
            this.position = this.source.Length;
            throw this.Fail(ReadFailures.ShortRead(elementSize, leftover), member, memberType);
        }

        if (total > allowed)
        {
            // The runtime charged element by element and failed after reading the element that crossed the limit.
            this.position += (int)Math.Min(available, (affordableElements + 1) * elementSize);
            throw this.FailLimit(ReadFailures.TotalBytesLimit, member, memberType);
        }

        return this.Take((int)total, member, memberType);
    }

    /// <summary>
    ///     Counts the elements of a <c>T values[EOF]</c> array: the whole elements from the position to the end of
    ///     the input, with the runtime's checks and texts.
    /// </summary>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <param name="fieldName">The layout field name, named in the messages.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The element count.</returns>
    public readonly int CountToEnd(int elementSize, string fieldName, string member, string? memberType)
    {
        long remaining = this.Remaining;
        if (elementSize == 0)
        {
            return 0;
        }

        if (remaining % elementSize != 0)
        {
            throw this.Fail(ReadFailures.ToEndRemainder(remaining, elementSize, fieldName), member, memberType);
        }

        long count = remaining / elementSize;
        if (count > this.settings.MaxArrayElements)
        {
            throw this.FailLimit(ReadFailures.ArrayLengthLimit(count, this.settings.MaxArrayElements), member, memberType);
        }

        return (int)count;
    }

    /// <summary>
    ///     Counts the elements of a <c>T values[]</c> array up to (not including) its all-zero terminator element,
    ///     leaving the position where it was; the runtime's texts report a missing terminator or too many elements.
    /// </summary>
    /// <param name="elementSize">The element size in bytes.</param>
    /// <param name="fieldName">The layout field name, named in the messages.</param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The element count.</returns>
    public readonly int CountTerminated(int elementSize, string fieldName, string member, string? memberType)
    {
        if (elementSize == 0)
        {
            return 0;
        }

        int count = 0;
        int offset = this.position;
        while (true)
        {
            if (offset + elementSize > this.source.Length)
            {
                throw this.Fail(ReadFailures.TerminatedArrayUnterminated(fieldName), member, memberType);
            }

            if (this.source.Slice(offset, elementSize).IndexOfAnyExcept((byte)0) < 0)
            {
                return count;
            }

            if (++count > this.settings.MaxArrayElements)
            {
                throw this.FailLimit(ReadFailures.ArrayLengthLimit(count, this.settings.MaxArrayElements), member, memberType);
            }

            offset += elementSize;
        }
    }

    /// <summary>Validates an array length against <c>MaxArrayElements</c> and returns it as an <see cref="int"/>.</summary>
    /// <param name="count">
    ///     The element count, in the layout expression domain: a count evaluated from a <c>uint64</c> field keeps its
    ///     exact value, so the limit failure names it.
    /// </param>
    /// <param name="member">The array field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The validated count.</returns>
    /// <exception cref="CStructReadLimitException">The length is negative or exceeds the limit.</exception>
    public readonly int RequireArrayLength(Int128 count, string member, string? memberType)
    {
        if (count < 0 || count > this.settings.MaxArrayElements)
        {
            throw this.FailLimit(ReadFailures.ArrayLengthLimit(count, this.settings.MaxArrayElements), member, memberType);
        }

        return (int)count;
    }
}
