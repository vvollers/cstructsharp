namespace CStructSharp.Reading;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Streams;
using CStructSharp.Values;

/// <summary>
///     The record-sequence enumerators behind <c>CStruct.ParseMany</c> and <c>ParseManyAsync</c>: one root struct
///     after another until the input ends, each parsed on the <c>MoveNext</c> that reaches it. A root with a fixed
///     size is checked against the bytes left before it is read, so trailing bytes shorter than one record fail with
///     the same text a <c>T v[EOF]</c> array uses for a partial element; a runtime-sized root advances by the bytes
///     the previous record consumed and trailing bytes fail as the short read they are. A failure carries the record
///     index before the path (<c>[3].header.length</c>) and an offset in the input's coordinates.
/// </summary>
/// <remarks>
///     Every record is its own read: the read limits apply per record, and in the memory, sequence, and
///     asynchronous forms a stored absolute pointer address counts from the record's own first byte. The
///     synchronous stream form runs the stream reader over the caller's stream, so there an address counts from the
///     stream's first byte, as <c>Parse(Stream)</c> counts.
/// </remarks>
internal static class RecordSequence
{
    /// <summary>The records of <paramref name="source"/>, parsed one per <c>MoveNext</c> from the end of the previous one.</summary>
    public static IEnumerable<StructValue> FromMemory(CStruct layout, ReadOnlyMemory<byte> source, RecordRoot root, LayoutVariableInput variables, ReadOptions? options)
    {
        int offset = 0;
        for (int index = 0; offset < source.Length; index++)
        {
            StructValue record = ParseAt(layout, source, offset, index, root, variables, options, out int consumed);
            offset += consumed;
            yield return record;
        }
    }

    /// <summary>A multi-segment sequence is copied once into a pooled array, returned when the enumeration ends or is disposed.</summary>
    public static IEnumerable<StructValue> FromSegments(CStruct layout, ReadOnlySequence<byte> source, RecordRoot root, LayoutVariableInput variables, ReadOptions? options)
    {
        int length = checked((int)source.Length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            source.CopyTo(buffer);
            foreach (StructValue record in FromMemory(layout, new ReadOnlyMemory<byte>(buffer, 0, length), root, variables, options))
            {
                yield return record;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>The records of a seekable stream, read with the stream reader from the current position to the end; the stream is left after the last record read, or where a failed read stopped.</summary>
    public static IEnumerable<StructValue> FromStream(CStruct layout, Stream stream, RecordRoot root, LayoutVariableInput variables, ReadOptions? options)
    {
        for (int index = 0; stream.Position < stream.Length; index++)
        {
            long start = stream.Position;
            long remaining = stream.Length - start;
            if (root.Size is { } size && remaining < size)
            {
                throw Partial(remaining, size, root, index, start);
            }

            StructValue record;
            try
            {
                record = layout.ParseRecordCore(stream, root.Name, variables, options);
            }
            catch (CStructException failure)
            {
                failure.PrefixPath(RecordPrefix(index));
                throw;
            }

            if (stream.Position == start)
            {
                throw Empty(root, index, start);
            }

            yield return record;
        }
    }

    /// <summary>
    ///     The records of a stream read with <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>: a
    ///     fixed-size root is read exactly one record at a time, so any readable stream serves; a runtime-sized root
    ///     is read through a pooled window (the bytes left, at most <see cref="ReadOptions.MaxTotalBytesRead"/> plus
    ///     one) refilled from the start of the record that outgrows it, which needs a seekable stream. After each
    ///     record a seekable stream is positioned at the record's end.
    /// </summary>
    public static async IAsyncEnumerable<StructValue> FromStreamAsync(
        CStruct layout,
        Stream stream,
        RecordRoot root,
        LayoutVariableInput variables,
        ReadOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CancellationToken token = AsyncStreamBuffer.Link(options, cancellationToken, out CancellationTokenSource? linked);
        using (linked)
        {
            ReadOptions? effective = token.CanBeCanceled ? (options ?? new ReadOptions()) with { CancellationToken = token, } : options;
            long origin = stream.CanSeek ? stream.Position : 0;
            if (root.Size is { } size)
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(size, 1));
                try
                {
                    long consumedTotal = 0;
                    for (int index = 0; ; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        int got = await FillAsync(stream, buffer, size, token).ConfigureAwait(false);
                        if (got == 0)
                        {
                            yield break;
                        }

                        if (got < size)
                        {
                            throw Partial(got, size, root, index, origin + consumedTotal);
                        }

                        StructValue record = ParseAt(layout, new ReadOnlyMemory<byte>(buffer, 0, size), 0, index, root, variables, effective, out _, shift: origin + consumedTotal);
                        consumedTotal += size;
                        yield return record;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
            else
            {
                await foreach (StructValue record in WindowedAsync(layout, stream, root, variables, effective, origin, token).ConfigureAwait(false))
                {
                    yield return record;
                }
            }
        }
    }

    /// <summary>Records of a runtime-sized root over a seekable stream, through a window that refills from the record it could not hold.</summary>
    private static async IAsyncEnumerable<StructValue> WindowedAsync(
        CStruct layout,
        Stream stream,
        RecordRoot root,
        LayoutVariableInput variables,
        ReadOptions? effective,
        long origin,
        [EnumeratorCancellation] CancellationToken token)
    {
        long consumedTotal = 0;
        int index = 0;
        while (origin + consumedTotal < stream.Length)
        {
            token.ThrowIfCancellationRequested();
            stream.Position = origin + consumedTotal;
            (byte[] window, int length) = await AsyncStreamBuffer.RentAsync(stream, effective, token).ConfigureAwait(false);
            try
            {
                // A window that reached the end holds every remaining record; a shorter one holds the budget plus
                // one byte, so a record that starts it and still fails has failed for real.
                bool complete = origin + consumedTotal + length >= stream.Length;
                int offset = 0;
                while (offset < length)
                {
                    StructValue record;
                    int consumed;
                    try
                    {
                        record = ParseAt(layout, new ReadOnlyMemory<byte>(window, 0, length), offset, index, root, variables, effective, out consumed, shift: origin + consumedTotal);
                    }
                    catch (CStructException) when (!complete && offset > 0)
                    {
                        // The record may only have outgrown the window: refill from its start and read it again.
                        break;
                    }

                    offset += consumed;
                    index++;
                    stream.Position = origin + consumedTotal + offset;
                    yield return record;
                }

                consumedTotal += offset;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(window);
            }
        }
    }

    /// <summary>Reads up to <paramref name="count"/> bytes; fewer means the stream ended.</summary>
    private static async ValueTask<int> FillAsync(Stream stream, byte[] buffer, int count, CancellationToken token)
    {
        int got = 0;
        while (got < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(got, count - got), token).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            got += read;
        }

        return got;
    }

    /// <summary>
    ///     Parses record <paramref name="index"/> from <paramref name="offset"/> in <paramref name="source"/> as its
    ///     own region; <paramref name="consumed"/> is the record's encoded length. A failure names the record and
    ///     carries the offset in the input's coordinates (<paramref name="shift"/> is added for a windowed stream).
    /// </summary>
    private static unsafe StructValue ParseAt(CStruct layout, ReadOnlyMemory<byte> source, int offset, int index, RecordRoot root, LayoutVariableInput variables, ReadOptions? options, out int consumed, long shift = 0)
    {
        int remaining = source.Length - offset;
        if (root.Size is { } size && remaining < size)
        {
            throw Partial(remaining, size, root, index, shift + offset);
        }

        try
        {
            fixed (byte* pointer = &MemoryMarshal.GetReference(source.Span))
            {
                using var region = new FixedBufferStream(pointer + offset, remaining, writable: false);
                StructValue record = layout.ParseRecordCore(region, root.Name, variables, options);
                consumed = (int)region.Position;
                if (consumed == 0)
                {
                    throw Empty(root, index, shift + offset);
                }

                return record;
            }
        }
        catch (CStructException failure)
        {
            failure.PrefixPath(RecordPrefix(index));
            failure.ShiftOffset(shift + offset);
            throw;
        }
    }

    private static string RecordPrefix(int index) => "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>Trailing bytes shorter than one fixed-size record: the partial-element text of a read-to-end array.</summary>
    private static CStructReadException Partial(long remaining, int size, RecordRoot root, int index, long offset)
    {
        var failure = new CStructReadException(ReadFailures.ToEndRemainder(remaining, size, root.Name));
        failure.AttachContext(RecordPrefix(index) + "." + root.Name, offset);
        return failure;
    }

    /// <summary>A record that occupies no bytes would repeat forever; the sequence stops with a failure instead.</summary>
    private static CStructReadException Empty(RecordRoot root, int index, long offset)
    {
        var failure = new CStructReadException("Record " + index.ToString(CultureInfo.InvariantCulture) + " of '" + root.Name + "' occupies no bytes; a sequence of records needs a root that reads at least one byte.");
        failure.AttachContext(RecordPrefix(index) + "." + root.Name, offset);
        return failure;
    }
}
