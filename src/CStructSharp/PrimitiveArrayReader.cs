namespace CStructSharp;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

/// <summary>
///     Bulk reader for arrays of fixed-width numeric primitives (E2.3): the whole extent is read in blocks of at most
///     64 KiB and decoded span-wise into a typed <see cref="PrimitiveArray{T}"/> - a copy for host byte order, the
///     vectorized <see cref="BinaryPrimitives.ReverseEndianness(ReadOnlySpan{ushort}, Span{ushort})"/> family for
///     the other, tight loops for <c>bool</c> and 24-bit integers. Memory-backed sources decode straight from their
///     span. Values, element order, the final stream position, and the total charged to the read budget are
///     identical to the per-element path.
/// </summary>
internal static class PrimitiveArrayReader
{
    private const int BlockSize = 64 * 1024;

    private delegate void BlockDecoder<T>(ReadOnlySpan<byte> source, Span<T> destination, bool littleEndian);

    /// <summary>Reads <paramref name="count"/> elements into a typed array and returns it as the array value.</summary>
    public static IList<object?> Read(ReadBudgetStream stream, PrimitiveCodec codec, int count)
    {
        bool le = codec.LittleEndian;
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => new PrimitiveArray<byte>(ReadBlocks<byte>(stream, codec.Size, count, static (src, dst, _) => src.CopyTo(MemoryMarshal.AsBytes(dst)), le)),
            PrimitiveCodecKind.Int8 => new PrimitiveArray<sbyte>(ReadBlocks<sbyte>(stream, codec.Size, count, static (src, dst, _) => src.CopyTo(MemoryMarshal.AsBytes(dst)), le)),
            PrimitiveCodecKind.Bool => new PrimitiveArray<bool>(ReadBlocks<bool>(stream, codec.Size, count, static (src, dst, _) => DecodeBooleans(src, dst), le)),
            PrimitiveCodecKind.Int16 => new PrimitiveArray<short>(ReadBlocks<short>(stream, codec.Size, count, static (src, dst, le) => DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.UInt16 => new PrimitiveArray<ushort>(ReadBlocks<ushort>(stream, codec.Size, count, static (src, dst, le) => DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.Int32 => new PrimitiveArray<int>(ReadBlocks<int>(stream, codec.Size, count, static (src, dst, le) => DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.UInt32 => new PrimitiveArray<uint>(ReadBlocks<uint>(stream, codec.Size, count, static (src, dst, le) => DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.Int64 => new PrimitiveArray<long>(ReadBlocks<long>(stream, codec.Size, count, static (src, dst, le) => DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.UInt64 => new PrimitiveArray<ulong>(ReadBlocks<ulong>(stream, codec.Size, count, static (src, dst, le) => DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.Float32 => new PrimitiveArray<float>(ReadBlocks<float>(stream, codec.Size, count, static (src, dst, le) => DecodeIntegers(src, MemoryMarshal.Cast<float, uint>(dst), le), le)),
            PrimitiveCodecKind.Float64 => new PrimitiveArray<double>(ReadBlocks<double>(stream, codec.Size, count, static (src, dst, le) => DecodeIntegers(src, MemoryMarshal.Cast<double, ulong>(dst), le), le)),
            PrimitiveCodecKind.Int24 => new PrimitiveArray<int>(ReadBlocks<int>(stream, codec.Size, count, static (src, dst, le) => DecodeInt24(src, dst, le), le)),
            PrimitiveCodecKind.UInt24 => new PrimitiveArray<uint>(ReadBlocks<uint>(stream, codec.Size, count, static (src, dst, le) => DecodeUInt24(src, dst, le), le)),
            _ => throw new InvalidOperationException("Codec is not a fixed-width numeric primitive: " + codec.Kind),
        };
    }

    /// <summary>Decodes <paramref name="count"/> elements that are already in memory (static read plan, E2.5).</summary>
    public static IList<object?> Decode(ReadOnlySpan<byte> bytes, PrimitiveCodec codec, int count)
    {
        bool le = codec.LittleEndian;
        switch (codec.Kind)
        {
        case PrimitiveCodecKind.UInt8:
            return new PrimitiveArray<byte>(bytes.ToArray());
        case PrimitiveCodecKind.Int8:
            {
                var values = new sbyte[count];
                bytes.CopyTo(MemoryMarshal.AsBytes(values.AsSpan()));
                return new PrimitiveArray<sbyte>(values);
            }

        case PrimitiveCodecKind.Bool:
            {
                var values = new bool[count];
                DecodeBooleans(bytes, values);
                return new PrimitiveArray<bool>(values);
            }

        case PrimitiveCodecKind.Int16:
            return new PrimitiveArray<short>(DecodeIntegers<short>(bytes, count, le));
        case PrimitiveCodecKind.UInt16:
            return new PrimitiveArray<ushort>(DecodeIntegers<ushort>(bytes, count, le));
        case PrimitiveCodecKind.Int32:
            return new PrimitiveArray<int>(DecodeIntegers<int>(bytes, count, le));
        case PrimitiveCodecKind.UInt32:
            return new PrimitiveArray<uint>(DecodeIntegers<uint>(bytes, count, le));
        case PrimitiveCodecKind.Int64:
            return new PrimitiveArray<long>(DecodeIntegers<long>(bytes, count, le));
        case PrimitiveCodecKind.UInt64:
            return new PrimitiveArray<ulong>(DecodeIntegers<ulong>(bytes, count, le));
        case PrimitiveCodecKind.Float32:
            {
                var values = new float[count];
                DecodeIntegers(bytes, MemoryMarshal.Cast<float, uint>(values.AsSpan()), le);
                return new PrimitiveArray<float>(values);
            }

        case PrimitiveCodecKind.Float64:
            {
                var values = new double[count];
                DecodeIntegers(bytes, MemoryMarshal.Cast<double, ulong>(values.AsSpan()), le);
                return new PrimitiveArray<double>(values);
            }

        case PrimitiveCodecKind.Int24:
            {
                var values = new int[count];
                DecodeInt24(bytes, values, le);
                return new PrimitiveArray<int>(values);
            }

        case PrimitiveCodecKind.UInt24:
            {
                var values = new uint[count];
                DecodeUInt24(bytes, values, le);
                return new PrimitiveArray<uint>(values);
            }

        default:
            throw new InvalidOperationException("Codec is not a fixed-width numeric primitive: " + codec.Kind);
        }
    }

    /// <summary>
    ///     Boxed variant for multidimensional arrays, which are reshaped into nested lists after the read: appends
    ///     <paramref name="count"/> elements to <paramref name="target"/> and returns the last value.
    /// </summary>
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

    /// <summary>
    ///     Reads the extent in ≤ 64 KiB blocks - from the source span when the stream is memory-backed, through a
    ///     pooled buffer otherwise - and decodes each block into its slice of the result. The block granularity is
    ///     what makes a read-budget failure surface at the same position as the stage 1 reader.
    /// </summary>
    private static T[] ReadBlocks<T>(ReadBudgetStream stream, int elementSize, int count, BlockDecoder<T> decode, bool littleEndian)
        where T : unmanaged
    {
        var result = new T[count];
        long remaining = (long)count * elementSize;
        int blockCapacity = (int)Math.Min(remaining, BlockSize) / elementSize * elementSize;
        int decoded = 0;
        byte[]? block = null;
        try
        {
            while (remaining > 0)
            {
                int blockLength = (int)Math.Min(remaining, blockCapacity);
                int elements = blockLength / elementSize;
                Span<T> destination = result.AsSpan(decoded, elements);
                if (stream.TryReadSpan(blockLength, out ReadOnlySpan<byte> source))
                {
                    decode(source, destination, littleEndian);
                }
                else
                {
                    block ??= ArrayPool<byte>.Shared.Rent(blockCapacity);
                    BinaryPrimitiveIO.ReadExactlyOrThrow(stream, block.AsSpan(0, blockLength));
                    decode(block.AsSpan(0, blockLength), destination, littleEndian);
                }

                decoded += elements;
                remaining -= blockLength;
            }
        }
        finally
        {
            if (block is not null)
            {
                ArrayPool<byte>.Shared.Return(block);
            }
        }

        return result;
    }

    private static T[] DecodeIntegers<T>(ReadOnlySpan<byte> source, int count, bool littleEndian)
        where T : unmanaged
    {
        var values = new T[count];
        DecodeIntegers(source, values.AsSpan(), littleEndian);
        return values;
    }

    private static void DecodeBooleans(ReadOnlySpan<byte> source, Span<bool> destination)
    {
        for (int index = 0; index < destination.Length; index++)
        {
            destination[index] = source[index] != 0;
        }
    }

    /// <summary>Host byte order is one copy; the other order copies then reverses in place (vectorized on .NET 8+).</summary>
    private static void DecodeIntegers<T>(ReadOnlySpan<byte> source, Span<T> destination, bool littleEndian)
        where T : unmanaged
    {
        source.CopyTo(MemoryMarshal.AsBytes(destination));
        if (littleEndian == BitConverter.IsLittleEndian)
        {
            return;
        }

        if (typeof(T) == typeof(ushort) || typeof(T) == typeof(short))
        {
            Span<ushort> view = MemoryMarshal.Cast<T, ushort>(destination);
            BinaryPrimitives.ReverseEndianness(view, view);
        }
        else if (typeof(T) == typeof(uint) || typeof(T) == typeof(int))
        {
            Span<uint> view = MemoryMarshal.Cast<T, uint>(destination);
            BinaryPrimitives.ReverseEndianness(view, view);
        }
        else
        {
            Span<ulong> view = MemoryMarshal.Cast<T, ulong>(destination);
            BinaryPrimitives.ReverseEndianness(view, view);
        }
    }

    private static void DecodeInt24(ReadOnlySpan<byte> source, Span<int> destination, bool littleEndian)
    {
        for (int index = 0, offset = 0; index < destination.Length; index++, offset += 3)
        {
            uint raw = littleEndian
                           ? (uint)(source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16))
                           : (uint)(source[offset + 2] | (source[offset + 1] << 8) | (source[offset] << 16));
            destination[index] = unchecked((int)(raw << 8)) >> 8;
        }
    }

    private static void DecodeUInt24(ReadOnlySpan<byte> source, Span<uint> destination, bool littleEndian)
    {
        for (int index = 0, offset = 0; index < destination.Length; index++, offset += 3)
        {
            destination[index] = littleEndian
                                     ? (uint)(source[offset] | (source[offset + 1] << 8) | (source[offset + 2] << 16))
                                     : (uint)(source[offset + 2] | (source[offset + 1] << 8) | (source[offset] << 16));
        }
    }
}
