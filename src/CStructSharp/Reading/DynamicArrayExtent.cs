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
    public static int CountToEnd(Stream stream, long start, int elementSize, int maximumElements, string fieldName)
    {
        if (!stream.CanSeek)
        {
            throw new CStructReadException("A read-to-end array needs a seekable input: " + fieldName);
        }

        long remaining = stream.Length - start;
        if (remaining < 0)
        {
            throw new CStructReadException("Not enough bytes: the array starts beyond the end of the input.");
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
