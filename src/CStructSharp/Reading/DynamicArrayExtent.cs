namespace CStructSharp.Reading;

using System;
using System.Buffers;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Streams;

/// <summary>
///     Counts the elements of the two data-sized array kinds (<see cref="Compilation.CompiledArrayKind.ToEnd"/> and
///     <see cref="Compilation.CompiledArrayKind.Terminated"/>) so the compiled engine's reader, its path resolver and the
///     length query share one rule. Both need a fixed element size; the count is computed once per array, never per
///     element.
/// </summary>
/// <remarks>
///     Each count is written once, over a read cursor: a stream source is counted through a <see cref="StreamReadCursor"/>
///     over the operation's <see cref="ReadBudgetStream"/>, and a memory source through the memory cursor, which counts,
///     charges and leaves the position where a memory-backed <see cref="ReadBudgetStream"/> would.
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
    ///     elements before it. Every byte the scan reads is charged to the read budget - the elements and the terminator
    ///     are charged again when the array is read - and the position is back at <paramref name="start"/> afterwards,
    ///     after a failure too.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="start">The array's first byte, as an absolute position.</param>
    /// <param name="elementSize">The size of one element in bytes; zero yields an empty array.</param>
    /// <param name="maximumElements">The largest element count the read options allow.</param>
    /// <param name="fieldName">The array field, named in failure messages.</param>
    /// <returns>The number of elements before the terminator, which is not counted.</returns>
    /// <exception cref="CStructReadException">The input ends before an all-zero element.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds <paramref name="maximumElements"/>, or the scan exceeds the read budget.</exception>
    public static int CountTerminated<TCursor>(ref TCursor cursor, long start, int elementSize, int maximumElements, string fieldName)
        where TCursor : struct, IReadCursor
    {
        if (elementSize == 0)
        {
            return 0;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(elementSize);
        try
        {
            cursor.Position = start;
            int count = 0;
            while (true)
            {
                // One element per iteration, in as many partial reads as the source returns: a chunked stream charges
                // (and can exhaust the budget) at its own read granularity.
                int read = 0;
                while (read < elementSize)
                {
                    int chunk = cursor.Read(buffer, read, elementSize - read);
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
            cursor.Position = start;
        }
    }
}
