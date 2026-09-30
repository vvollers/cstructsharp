namespace CStructSharp.Reading;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Generated;
using CStructSharp.Streams;
using CStructSharp.Values;

/// <summary>
///     Bulk reader for arrays of fixed-width numeric primitives: the whole extent is read in blocks of at most
///     64 KiB and decoded span-wise into a typed <see cref="PrimitiveArray{T}"/> - a copy for host byte order, the
///     vectorized <see cref="BinaryPrimitives.ReverseEndianness(ReadOnlySpan{ushort}, Span{ushort})"/> family for
///     the other, tight loops for <c>bool</c> and 24-bit integers. Memory-backed sources decode straight from their
///     span. Values, element order, the final stream position, and the total charged to the read budget are
///     identical to the per-element path.
/// </summary>
internal static class PrimitiveArrayReader
{
    /// <summary>Decodes one block of source bytes into elements, reversing byte order when <paramref name="littleEndian"/> differs from the host.</summary>
    private delegate void BlockDecoder<T>(ReadOnlySpan<byte> source, Span<T> destination, bool littleEndian);

    /// <summary>Reads <paramref name="count"/> elements into a typed array and returns it as the array value.</summary>
    /// <param name="stream">The budgeted source, positioned at the first element; it advances past the array.</param>
    /// <param name="codec">The fixed-width numeric codec of one element, including its byte order.</param>
    /// <param name="count">The number of elements to read.</param>
    /// <returns>A <see cref="PrimitiveArray{T}"/> of the codec's element type holding the decoded values.</returns>
    /// <exception cref="CStructReadException">The source holds fewer bytes than the elements need.</exception>
    /// <exception cref="InvalidOperationException">The codec is not a fixed-width numeric codec.</exception>
    public static IList<object?> Read(ReadBudgetStream stream, PrimitiveCodec codec, int count)
    {
        var cursor = new StreamReadCursor(stream);
        return Read(ref cursor, codec, count);
    }

    /// <summary>
    ///     Reads <paramref name="count"/> elements through a read cursor into a typed array: the one implementation
    ///     behind the stream overload and the engine's memory cursor, so both fail and charge at the same blocks.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type, a struct so the reader is compiled per cursor.</typeparam>
    /// <param name="cursor">The cursor, positioned at the first element; it advances past the array.</param>
    /// <param name="codec">The fixed-width numeric codec of one element, including its byte order.</param>
    /// <param name="count">The number of elements to read.</param>
    /// <returns>A <see cref="PrimitiveArray{T}"/> of the codec's element type holding the decoded values.</returns>
    /// <exception cref="CStructReadException">The source holds fewer bytes than the elements need.</exception>
    /// <exception cref="CStructReadLimitException">A block exceeds the total read budget.</exception>
    /// <exception cref="OperationCanceledException">The operation's token is cancelled before a block.</exception>
    /// <exception cref="InvalidOperationException">The codec is not a fixed-width numeric codec.</exception>
    public static IList<object?> Read<TCursor>(ref TCursor cursor, PrimitiveCodec codec, int count)
        where TCursor : struct, IReadCursor
    {
        // A count the data provably cannot back (a hostile or corrupt length prefix) fails here, before the element
        // array is allocated, with the same failure and final position a full short read would produce.
        if (cursor.IsShortBy((long)count * codec.Size))
        {
            string message = ReadFailures.ArrayShortRead(count, codec.Size, Math.Max(0, cursor.Length - cursor.Position));
            cursor.Position = cursor.Length;
            throw new CStructReadException(message);
        }

        bool le = codec.LittleEndian;
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => new PrimitiveArray<byte>(ReadBlocks<TCursor, byte>(ref cursor, codec.Size, count, static (src, dst, _) => src.CopyTo(MemoryMarshal.AsBytes(dst)), le)),
            PrimitiveCodecKind.Int8 => new PrimitiveArray<sbyte>(ReadBlocks<TCursor, sbyte>(ref cursor, codec.Size, count, static (src, dst, _) => src.CopyTo(MemoryMarshal.AsBytes(dst)), le)),
            PrimitiveCodecKind.Bool => new PrimitiveArray<bool>(ReadBlocks<TCursor, bool>(ref cursor, codec.Size, count, static (src, dst, _) => Codec.DecodeBooleans(src, dst), le)),
            PrimitiveCodecKind.Int16 => new PrimitiveArray<short>(ReadBlocks<TCursor, short>(ref cursor, codec.Size, count, static (src, dst, le) => Codec.DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.UInt16 => new PrimitiveArray<ushort>(ReadBlocks<TCursor, ushort>(ref cursor, codec.Size, count, static (src, dst, le) => Codec.DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.Int32 => new PrimitiveArray<int>(ReadBlocks<TCursor, int>(ref cursor, codec.Size, count, static (src, dst, le) => Codec.DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.UInt32 => new PrimitiveArray<uint>(ReadBlocks<TCursor, uint>(ref cursor, codec.Size, count, static (src, dst, le) => Codec.DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.Int64 => new PrimitiveArray<long>(ReadBlocks<TCursor, long>(ref cursor, codec.Size, count, static (src, dst, le) => Codec.DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.UInt64 => new PrimitiveArray<ulong>(ReadBlocks<TCursor, ulong>(ref cursor, codec.Size, count, static (src, dst, le) => Codec.DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.Float32 => new PrimitiveArray<float>(ReadBlocks<TCursor, float>(ref cursor, codec.Size, count, static (src, dst, le) => Codec.DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.Float64 => new PrimitiveArray<double>(ReadBlocks<TCursor, double>(ref cursor, codec.Size, count, static (src, dst, le) => Codec.DecodeIntegers(src, dst, le), le)),
            PrimitiveCodecKind.Int24 => new PrimitiveArray<int>(ReadBlocks<TCursor, int>(ref cursor, codec.Size, count, static (src, dst, le) => Codec.DecodeInt24(src, dst, le), le)),
            PrimitiveCodecKind.UInt24 => new PrimitiveArray<uint>(ReadBlocks<TCursor, uint>(ref cursor, codec.Size, count, static (src, dst, le) => Codec.DecodeUInt24(src, dst, le), le)),
            _ => throw new InvalidOperationException("Codec is not a fixed-width numeric primitive: " + codec.Kind),
        };
    }

    /// <summary>The CLR element type the decoders produce for a fixed-width numeric codec.</summary>
    /// <param name="codec">A fixed-width numeric codec.</param>
    /// <returns>The element type, such as <see cref="int"/> for both 24-bit and 32-bit signed integers.</returns>
    /// <exception cref="InvalidOperationException">The codec is not a fixed-width numeric codec.</exception>
    public static Type GetElementType(PrimitiveCodec codec)
    {
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => typeof(byte),
            PrimitiveCodecKind.Int8 => typeof(sbyte),
            PrimitiveCodecKind.Bool => typeof(bool),
            PrimitiveCodecKind.Int16 => typeof(short),
            PrimitiveCodecKind.UInt16 => typeof(ushort),
            PrimitiveCodecKind.Int24 => typeof(int),
            PrimitiveCodecKind.UInt24 => typeof(uint),
            PrimitiveCodecKind.Int32 => typeof(int),
            PrimitiveCodecKind.UInt32 => typeof(uint),
            PrimitiveCodecKind.Int64 => typeof(long),
            PrimitiveCodecKind.UInt64 => typeof(ulong),
            PrimitiveCodecKind.Float32 => typeof(float),
            PrimitiveCodecKind.Float64 => typeof(double),
            _ => throw new InvalidOperationException("Codec is not a fixed-width numeric: " + codec.Kind),
        };
    }

    /// <summary>An empty <see cref="PrimitiveArray{T}"/> of the codec's element type.</summary>
    /// <param name="codec">A fixed-width numeric codec.</param>
    /// <returns>The empty array value.</returns>
    /// <exception cref="InvalidOperationException">The codec is not a fixed-width numeric codec.</exception>
    public static IList<object?> Empty(PrimitiveCodec codec) => Decode(ReadOnlySpan<byte>.Empty, codec, 0);

    /// <summary>
    ///     Copies boxed elements into a <see cref="PrimitiveArray{T}"/> of <paramref name="elementType"/>: the shape of an
    ///     array whose elements were decoded one at a time (debug parses, union member views, the memory API).
    /// </summary>
    /// <param name="elementType">The element type, as <see cref="GetElementType"/> gives it.</param>
    /// <param name="values">The elements, each of <paramref name="elementType"/>.</param>
    /// <returns>The typed array, or <see langword="null"/> when <paramref name="elementType"/> has no typed array.</returns>
    public static IList<object?>? FromBoxed(Type elementType, IReadOnlyList<object?> values)
    {
        return elementType == typeof(byte) ? Typed<byte>(values)
             : elementType == typeof(sbyte) ? Typed<sbyte>(values)
             : elementType == typeof(bool) ? Typed<bool>(values)
             : elementType == typeof(short) ? Typed<short>(values)
             : elementType == typeof(ushort) ? Typed<ushort>(values)
             : elementType == typeof(int) ? Typed<int>(values)
             : elementType == typeof(uint) ? Typed<uint>(values)
             : elementType == typeof(long) ? Typed<long>(values)
             : elementType == typeof(ulong) ? Typed<ulong>(values)
             : elementType == typeof(float) ? Typed<float>(values)
             : elementType == typeof(double) ? Typed<double>(values)
             : null;

        // Unboxes every element into a new typed array.
        static PrimitiveArray<T> Typed<T>(IReadOnlyList<object?> boxed)
            where T : unmanaged
        {
            var typed = new T[boxed.Count];
            for (int index = 0; index < typed.Length; index++)
            {
                typed[index] = (T)boxed[index]!;
            }

            return new PrimitiveArray<T>(typed);
        }
    }

    /// <summary>Decodes <paramref name="count"/> elements that are already in memory (static read plan).</summary>
    /// <param name="bytes">Exactly <paramref name="count"/> elements' worth of encoded bytes.</param>
    /// <param name="codec">The fixed-width numeric codec of one element, including its byte order.</param>
    /// <param name="count">The number of elements to decode.</param>
    /// <returns>A new <see cref="PrimitiveArray{T}"/> that owns a copy of the decoded values.</returns>
    /// <exception cref="InvalidOperationException">The codec is not a fixed-width numeric codec.</exception>
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
                Codec.DecodeBooleans(bytes, values);
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
                Codec.DecodeIntegers(bytes, values.AsSpan(), le);
                return new PrimitiveArray<float>(values);
            }

        case PrimitiveCodecKind.Float64:
            {
                var values = new double[count];
                Codec.DecodeIntegers(bytes, values.AsSpan(), le);
                return new PrimitiveArray<double>(values);
            }

        case PrimitiveCodecKind.Int24:
            {
                var values = new int[count];
                Codec.DecodeInt24(bytes, values, le);
                return new PrimitiveArray<int>(values);
            }

        case PrimitiveCodecKind.UInt24:
            {
                var values = new uint[count];
                Codec.DecodeUInt24(bytes, values, le);
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
    /// <param name="stream">The operation's source, positioned at the first element; it advances past the array.</param>
    /// <param name="codec">The fixed-width numeric codec of one element, including its byte order.</param>
    /// <param name="count">The number of elements to read.</param>
    /// <param name="target">The list that receives the boxed values, appended in element order.</param>
    /// <returns>The last value read, or null when <paramref name="count"/> is 0.</returns>
    public static object? ReadInto(ReadBudgetStream stream, PrimitiveCodec codec, int count, List<object?> target)
    {
        var cursor = new StreamReadCursor(stream);
        return ReadInto(ref cursor, codec, count, target);
    }

    /// <summary>
    ///     Reads <paramref name="count"/> elements through a read cursor into <paramref name="target"/>, one exact read per
    ///     block of at most 64 KiB (cancellation observed before each block): the one implementation behind the stream
    ///     overload and the engine's multidimensional arrays, so both fail and charge at the same blocks.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The cursor, positioned at the first element; it advances past the array.</param>
    /// <param name="codec">The fixed-width numeric codec of one element, including its byte order.</param>
    /// <param name="count">The number of elements to read.</param>
    /// <param name="target">The list that receives the boxed values, appended in element order.</param>
    /// <returns>The last value read, or null when <paramref name="count"/> is 0.</returns>
    /// <exception cref="CStructReadException">The source ends inside a block; the elements of earlier blocks were appended.</exception>
    /// <exception cref="CStructReadLimitException">A block exceeds the total read budget.</exception>
    /// <exception cref="OperationCanceledException">The operation's token is cancelled before a block.</exception>
    public static object? ReadInto<TCursor>(ref TCursor cursor, PrimitiveCodec codec, int count, List<object?> target)
        where TCursor : struct, IReadCursor
    {
        int elementSize = codec.Size;
        object? last = null;
        long remaining = (long)count * elementSize;
        int blockCapacity = (int)Math.Min(remaining, ReadBlock.Size) / elementSize * elementSize;
        byte[] block = ArrayPool<byte>.Shared.Rent(Math.Max(blockCapacity, elementSize));
        try
        {
            while (remaining > 0)
            {
                cursor.ThrowIfCancellationRequested();
                int blockLength = (int)Math.Min(remaining, blockCapacity);
                cursor.ReadExactly(block.AsSpan(0, blockLength));
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
    ///     Reads the extent in ≤ 64 KiB blocks - from the source span when the input is in memory, through a
    ///     pooled buffer otherwise - and decodes each block into its slice of the result. The block granularity is
    ///     what makes a read-budget failure surface at the same position as the element-by-element reader.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="cursor">The cursor at the first element.</param>
    /// <param name="elementSize">The width of one element in bytes.</param>
    /// <param name="count">The number of elements.</param>
    /// <param name="decode">Decodes one block of bytes into its elements.</param>
    /// <param name="littleEndian">Whether the elements are stored least significant byte first.</param>
    /// <returns>The decoded elements.</returns>
    private static T[] ReadBlocks<TCursor, T>(ref TCursor cursor, int elementSize, int count, BlockDecoder<T> decode, bool littleEndian)
        where TCursor : struct, IReadCursor
        where T : unmanaged
    {
        var result = new T[count];
        long remaining = (long)count * elementSize;
        int blockCapacity = (int)Math.Min(remaining, ReadBlock.Size) / elementSize * elementSize;
        int decoded = 0;
        byte[]? block = null;
        try
        {
            while (remaining > 0)
            {
                cursor.ThrowIfCancellationRequested();
                int blockLength = (int)Math.Min(remaining, blockCapacity);
                int elements = blockLength / elementSize;
                Span<T> destination = result.AsSpan(decoded, elements);
                if (cursor.TryReadSpan(blockLength, out ReadOnlySpan<byte> source))
                {
                    decode(source, destination, littleEndian);
                }
                else
                {
                    block ??= ArrayPool<byte>.Shared.Rent(blockCapacity);
                    cursor.ReadExactly(block.AsSpan(0, blockLength));
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
        Codec.DecodeIntegers(source, values.AsSpan(), littleEndian);
        return values;
    }
}
