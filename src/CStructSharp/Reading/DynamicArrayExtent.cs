namespace CStructSharp.Reading;

using System;
using System.Buffers;
using System.IO;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     Counts the elements of the two data-sized array kinds (<see cref="CompiledArrayKind.ToEnd"/> and
///     <see cref="CompiledArrayKind.Terminated"/>) so the reader, the address resolver, and the length query share one
///     rule. Both need a fixed element size; the count is computed once per array, never per element.
/// </summary>
internal static class DynamicArrayExtent
{
    /// <summary>Whole elements between <paramref name="start"/> and the end of the input; a trailing partial element is an error.</summary>
    /// <param name="stream">The input, which must be seekable; its position is not changed.</param>
    /// <param name="start">The array's first byte, as an absolute stream position.</param>
    /// <param name="elementSize">The size of one element in bytes; zero yields an empty array.</param>
    /// <param name="maximumElements">The largest element count the read options allow.</param>
    /// <param name="fieldName">The array field, named in failure messages.</param>
    /// <returns>The number of whole elements from <paramref name="start"/> to the end of the input.</returns>
    /// <exception cref="CStructReadException">
    ///     The stream cannot seek, <paramref name="start"/> is past the end, or the remaining bytes are not a whole
    ///     number of elements.
    /// </exception>
    /// <exception cref="CStructReadLimitException">The count exceeds <paramref name="maximumElements"/>.</exception>
    public static int CountToEnd(Stream stream, long start, int elementSize, int maximumElements, string fieldName)
    {
        if (!stream.CanSeek)
        {
            throw new CStructReadException("A read-to-end array needs a seekable input: " + fieldName);
        }

        long remaining = stream.Length - start;
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
    ///     Elements from <paramref name="start"/> up to (not including) the first element whose bytes are all zero;
    ///     that terminator must be present. The stream is left at <paramref name="start"/>.
    /// </summary>
    /// <param name="stream">The seekable input, which is read from <paramref name="start"/>.</param>
    /// <param name="start">The array's first byte, as an absolute stream position.</param>
    /// <param name="elementSize">The size of one element in bytes; zero yields an empty array.</param>
    /// <param name="maximumElements">The largest element count the read options allow.</param>
    /// <param name="fieldName">The array field, named in failure messages.</param>
    /// <returns>The number of elements before the terminator, which is not counted.</returns>
    /// <exception cref="CStructReadException">The input ends before an all-zero element.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds <paramref name="maximumElements"/>.</exception>
    public static int CountTerminated(Stream stream, long start, int elementSize, int maximumElements, string fieldName)
    {
        if (elementSize == 0)
        {
            return 0;
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(elementSize);
        try
        {
            stream.Position = start;
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
            stream.Position = start;
        }
    }
}
