namespace CStructSharp;

using System;
using System.Buffers;
using System.Buffers.Binary;
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

    private static readonly Dictionary<string, ElementDecoder> Decoders = new(StringComparer.Ordinal)
    {
        ["byte"] = static bytes => bytes[0],
        ["uint8"] = static bytes => bytes[0],
        ["int8"] = static bytes => unchecked((sbyte)bytes[0]),
        ["bool"] = static bytes => bytes[0] != 0,
        ["int16<"] = static bytes => BinaryPrimitives.ReadInt16LittleEndian(bytes),
        ["int16>"] = static bytes => BinaryPrimitives.ReadInt16BigEndian(bytes),
        ["uint16<"] = static bytes => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
        ["uint16>"] = static bytes => BinaryPrimitives.ReadUInt16BigEndian(bytes),
        ["int32<"] = static bytes => BinaryPrimitives.ReadInt32LittleEndian(bytes),
        ["int32>"] = static bytes => BinaryPrimitives.ReadInt32BigEndian(bytes),
        ["uint32<"] = static bytes => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
        ["uint32>"] = static bytes => BinaryPrimitives.ReadUInt32BigEndian(bytes),
        ["int64<"] = static bytes => BinaryPrimitives.ReadInt64LittleEndian(bytes),
        ["int64>"] = static bytes => BinaryPrimitives.ReadInt64BigEndian(bytes),
        ["uint64<"] = static bytes => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
        ["uint64>"] = static bytes => BinaryPrimitives.ReadUInt64BigEndian(bytes),
        ["float32<"] = static bytes => BinaryPrimitives.ReadSingleLittleEndian(bytes),
        ["float32>"] = static bytes => BinaryPrimitives.ReadSingleBigEndian(bytes),
        ["float64<"] = static bytes => BinaryPrimitives.ReadDoubleLittleEndian(bytes),
        ["float64>"] = static bytes => BinaryPrimitives.ReadDoubleBigEndian(bytes),
    };

    /// <summary>Decodes one element from exactly its bytes into the boxed value the per-element codec would produce.</summary>
    internal delegate object ElementDecoder(ReadOnlySpan<byte> bytes);

    public static bool TryGetDecoder(string codecName, out ElementDecoder decoder)
    {
        return Decoders.TryGetValue(codecName, out decoder!);
    }

    /// <summary>Reads <paramref name="count"/> elements into <paramref name="target"/> and returns the last value.</summary>
    public static object? ReadInto(Stream stream, ElementDecoder decoder, int elementSize, int count, List<object?> target)
    {
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
                    last = decoder(block.AsSpan(offset, elementSize));
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
