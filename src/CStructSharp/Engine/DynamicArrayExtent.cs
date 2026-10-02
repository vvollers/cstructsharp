namespace CStructSharp.Engine;

using System;
using System.Buffers;
using System.Runtime.InteropServices;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>
///     Counts the elements of the two data-sized array kinds (<see cref="Compilation.CompiledArrayKind.ToEnd"/> and
///     <see cref="Compilation.CompiledArrayKind.Terminated"/>) so the compiled engine's reader, its path resolver and the
///     length query share one rule. Both need a fixed element size; the count is computed once per array, never per
///     element.
/// </summary>
/// <remarks>
///     Each count is written once, over a read cursor: a stream source is counted through a <see cref="StreamReadCursor"/>
///     over the operation's <see cref="ReadBudgetStream"/>, and a memory source through the memory cursor. A terminated
///     array's scan is not charged to <c>MaxTotalBytesRead</c> (the array's reader charges each byte it consumes once)
///     but stops where the budget would fail: memory is scanned in place (<see cref="ScanSpan"/>), a stream through its
///     reads with their charge given back (<see cref="ScanStream"/>), and both fail at the same element.
/// </remarks>
internal static class DynamicArrayExtent
{
    /// <summary>
    ///     Whole elements between <paramref name="start"/> and the end of a cursor's input (always seekable); nothing is
    ///     read or charged.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor; its position is not changed.</param>
    /// <param name="start">The array's first byte, as an absolute position.</param>
    /// <param name="elementSize">The size of one element in bytes; zero yields an empty array.</param>
    /// <param name="maximumElements">The largest element count the read options allow.</param>
    /// <param name="fieldName">The array field, named in failure messages.</param>
    /// <returns>The number of whole elements from <paramref name="start"/> to the end of the input.</returns>
    /// <exception cref="CStructReadException"><paramref name="start"/> is past the end, or the remaining bytes are not a whole number of elements.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds <paramref name="maximumElements"/>.</exception>
    public static int CountToEnd<TCursor>(ref TCursor cursor, long start, int elementSize, int maximumElements, string fieldName)
        where TCursor : struct, IReadCursor
    {
        long remaining = cursor.Length - start;
        if (remaining < 0)
        {
            throw new CStructReadException(ReadFailures.ArrayStartsPastEnd);
        }

        if (elementSize == 0)
        {
            return 0;
        }

        if (remaining % elementSize != 0)
        {
            throw new CStructReadException(ReadFailures.ToEndRemainder(remaining, elementSize, fieldName));
        }

        long count = remaining / elementSize;
        if (count > maximumElements)
        {
            throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(count, maximumElements));
        }

        return (int)count;
    }

    /// <summary>
    ///     Scans a cursor's input from <paramref name="start"/> for the first all-zero element and returns the number of
    ///     elements before it. The scan charges nothing: the reader that consumes the array charges its elements and its
    ///     terminator, each byte once. It looks no further than the read budget lets the array consume, so an array whose
    ///     elements and terminator exceed the budget fails with the read-limit exception here. The position is back at
    ///     <paramref name="start"/> afterwards, after a failure too.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="start">The array's first byte, as an absolute position.</param>
    /// <param name="elementSize">The size of one element in bytes; zero yields an empty array.</param>
    /// <param name="maximumElements">The largest element count the read options allow.</param>
    /// <param name="fieldName">The array field, named in failure messages.</param>
    /// <param name="chargeScan">
    ///     Whether the scanned elements and terminator are charged once after the count: a path walk that only passes the
    ///     array, or only asks for its length, consumes those bytes through the scan alone.
    /// </param>
    /// <returns>The number of elements before the terminator, which is not counted.</returns>
    /// <exception cref="CStructReadException">The input ends before an all-zero element.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds <paramref name="maximumElements"/>, or the array exceeds the read budget.</exception>
    public static int CountTerminated<TCursor>(ref TCursor cursor, long start, int elementSize, int maximumElements, string fieldName, bool chargeScan)
        where TCursor : struct, IReadCursor
    {
        if (elementSize == 0)
        {
            return 0;
        }

        cursor.Position = start;
        try
        {
            int count = cursor.ScanTerminated(elementSize, maximumElements, fieldName);
            if (chargeScan)
            {
                // The scan proved the elements and the terminator fit the budget, so this charge cannot fail.
                cursor.Charge(((long)count + 1) * elementSize);
            }

            return count;
        }
        finally
        {
            cursor.Position = start;
        }
    }

    /// <summary>
    ///     Scans input in memory for the first all-zero element of <paramref name="elementSize"/> bytes, inspecting
    ///     elements in order and failing at the first element whose bytes the input or the budget cannot supply, or whose
    ///     count passes <paramref name="maximumElements"/> - where reading the elements one by one would fail. Nothing is
    ///     charged or consumed.
    /// </summary>
    /// <param name="input">The bytes from the array's first byte to the end of the input.</param>
    /// <param name="elementSize">The positive size of one element in bytes.</param>
    /// <param name="maximumElements">The largest element count the read options allow.</param>
    /// <param name="budget">The bytes the operation may still consume.</param>
    /// <param name="fieldName">The array field, named in failure messages.</param>
    /// <param name="continuesFrom">
    ///     -1 when <paramref name="input"/> runs to the end of the input; otherwise the position of its first byte in a
    ///     partly buffered input (<see cref="Streams.BufferedInput"/>) that may continue after it.
    /// </param>
    /// <returns>The number of elements before the terminator.</returns>
    /// <exception cref="CStructReadException">The input ends before an all-zero element.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds <paramref name="maximumElements"/>, or the array exceeds <paramref name="budget"/>.</exception>
    /// <exception cref="Streams.BufferedInputShortfallException">The outcome depends on bytes past a partly buffered <paramref name="input"/>.</exception>
    public static int ScanSpan(ReadOnlySpan<byte> input, int elementSize, int maximumElements, long budget, string fieldName, long continuesFrom)
    {
        // The elements the scan may inspect: whole ones in the input, within the budget (terminator included), and one
        // past the element limit, whose nonzero value is the limit failure.
        long fitting = Math.Min(input.Length / elementSize, Math.Max(budget, 0) / elementSize);
        int inspected = (int)Math.Min(fitting, (long)maximumElements + 1);
        int count = FindZeroElement(input[..(inspected * elementSize)], elementSize);
        if (count >= 0)
        {
            return count;
        }

        if (inspected > maximumElements)
        {
            throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit((long)maximumElements + 1, maximumElements));
        }

        // Whether the next element is complete, and the failure below, depend on where the input ends: past a partly
        // buffered input that is decided over more of it.
        long nextEnd = ((long)inspected + 1) * elementSize;
        if (continuesFrom >= 0 && nextEnd > input.Length)
        {
            throw Streams.BufferedInput.Shortfall(continuesFrom + nextEnd);
        }

        // The next element is missing or incomplete, or lies past the budget. Reading it would consume the rest of the
        // input when it is incomplete, so that is a budget failure only when the rest exceeds the budget.
        bool incomplete = nextEnd > input.Length;
        if (incomplete && input.Length <= budget)
        {
            throw new CStructReadException(ReadFailures.TerminatedArrayUnterminated(fieldName));
        }

        throw new CStructReadLimitException(ReadFailures.TotalBytesLimit);
    }

    /// <summary>
    ///     Scans a stream source from its position for the first all-zero element, one element at a time in as many
    ///     partial reads as the source returns. The reads are charged while the scan runs - so a scan past the budget
    ///     fails with the read-limit exception at the same element as <see cref="ScanSpan"/> - and refunded afterwards:
    ///     the stream has physically read the bytes, but the array's reader charges them when it consumes them.
    /// </summary>
    /// <param name="stream">The operation's budget stream; it is left somewhere after its position.</param>
    /// <param name="elementSize">The positive size of one element in bytes.</param>
    /// <param name="maximumElements">The largest element count the read options allow.</param>
    /// <param name="fieldName">The array field, named in failure messages.</param>
    /// <returns>The number of elements before the terminator.</returns>
    /// <exception cref="CStructReadException">The input ends before an all-zero element, or the stream fails.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds <paramref name="maximumElements"/>, or the array exceeds the read budget.</exception>
    public static int ScanStream(ReadBudgetStream stream, int elementSize, int maximumElements, string fieldName)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(elementSize);
        long budgetBefore = stream.RemainingReadBudget;
        try
        {
            int count = 0;
            while (true)
            {
                int read = 0;
                while (read < elementSize)
                {
                    int chunk = stream.Read(buffer, read, elementSize - read);
                    if (chunk <= 0)
                    {
                        throw new CStructReadException(ReadFailures.TerminatedArrayUnterminated(fieldName));
                    }

                    read += chunk;
                }

                if (buffer.AsSpan(0, elementSize).IndexOfAnyExcept((byte)0) < 0)
                {
                    return count;
                }

                if (++count > maximumElements)
                {
                    throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(count, maximumElements));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
            stream.Refund(budgetBefore - stream.RemainingReadBudget);
        }
    }

    /// <summary>
    ///     Returns the index of the first all-zero element of <paramref name="elementSize"/> bytes in
    ///     <paramref name="elements"/>, or -1. The common widths compare whole elements as one integer each, which the
    ///     runtime vectorises; zero is zero in either byte order and the reads need no alignment.
    /// </summary>
    /// <param name="elements">Whole elements.</param>
    /// <param name="elementSize">The positive size of one element in bytes.</param>
    /// <returns>The element index, or -1 when every element holds a nonzero byte.</returns>
    private static int FindZeroElement(ReadOnlySpan<byte> elements, int elementSize)
    {
        switch (elementSize)
        {
        case 1:
            return elements.IndexOf((byte)0);
        case 2:
            return MemoryMarshal.Cast<byte, ushort>(elements).IndexOf((ushort)0);
        case 4:
            return MemoryMarshal.Cast<byte, uint>(elements).IndexOf(0u);
        case 8:
            return MemoryMarshal.Cast<byte, ulong>(elements).IndexOf(0ul);
        default:
            for (int index = 0, offset = 0; offset < elements.Length; index++, offset += elementSize)
            {
                if (elements.Slice(offset, elementSize).IndexOfAnyExcept((byte)0) < 0)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
