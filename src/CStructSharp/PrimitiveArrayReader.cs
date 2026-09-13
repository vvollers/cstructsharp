namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;

/// <summary>
///     Bulk reader for arrays of fixed-width numeric primitives (E2.3): the whole extent is read in blocks into a
///     pooled buffer and each element is decoded from the span, instead of one virtual stream read, one stack
///     buffer, and one delegate call per element. Values, element order, the final stream position, and the total
///     charged to the read budget are identical to the per-element path; only the boxing of each value remains.
/// </summary>
internal static class PrimitiveArrayReader
{
    private const int BlockSize = 64 * 1024;

    /// <summary>Reads <paramref name="count"/> elements into <paramref name="target"/> and returns the last value.</summary>
    public static object? ReadInto(Stream stream, PrimitiveCodec codec, int count, List<object?> target)
    {
        int elementSize = codec.Size;
        object? last = null;
        long remaining = (long)count * elementSize;
        int blockCapacity = (int)Math.Min(remaining, BlockSize) / elementSize * elementSize;
        byte[] block = ArrayPool<byte>.Shared.Rent(Math.Max(blockCapacity, elementSize));
        try
        {
            while (remaining > 0)
            {
                int blockLength = (int)Math.Min(remaining, blockCapacity);
                BinaryPrimitiveIO.ReadExactlyOrThrow(stream, block.AsSpan(0, blockLength));
                for (int offset = 0; offset < blockLength; offset += elementSize)
                {
                    last = codec.ReadNumeric(block.AsSpan(offset, elementSize));
                    target.Add(last);
                }

                remaining -= blockLength;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(block);
        }

        return last;
    }
}
