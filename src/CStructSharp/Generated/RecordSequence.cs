namespace CStructSharp.Generated;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>
///     The one set of record-sequence rules the generated <c>Records</c>/<c>RecordsAsync</c> forms and the runtime's
///     <c>ParseMany</c>/<c>ParseManyAsync</c> share, over a <see cref="RecordReader{T}"/> that reads one record: one
///     record after another until the input ends, each read on the step that reaches it; a fixed-size record is
///     checked against the bytes left before it is read, so trailing bytes shorter than one record fail with the
///     text a <c>T v[EOF]</c> array uses for a partial element; a failure carries the record's index before the path
///     (<c>[3].header.length</c>) and an offset in the input's coordinates. A stream is read either exactly one
///     fixed-size record at a time (any readable stream, byte-exact) or through a pooled window of the bytes left,
///     first at most <see cref="ReadOptions.MaxTotalBytesRead"/> plus one, that refills from the start of a record it
///     could not hold and grows when a record that starts the window needs bytes past it (a seekable stream), so each
///     record reads as it would from the whole input. Support for generated code; the documented entry points are the
///     generated members and <c>CStruct.ParseMany</c>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RecordSequence
{
    /// <summary>The records of <paramref name="source"/>, each read from the end of the previous one.</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="source">The bytes of the records, with nothing else after them.</param>
    /// <param name="size">The record's fixed size, or <see langword="null"/> for a runtime-sized record.</param>
    /// <param name="layoutName">The record's layout name, for the trailing-bytes failure.</param>
    /// <param name="options">The read options each record is read with.</param>
    /// <param name="read">Reads one record.</param>
    /// <returns>The records, read as they are enumerated.</returns>
    public static IEnumerable<T> FromMemory<T>(ReadOnlyMemory<byte> source, int? size, string layoutName, ReadOptions? options, RecordReader<T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Enumerate(source, size, layoutName, 0, options, read);
    }

    /// <summary>The records of a sequence of segments: one segment is read in place, several are copied once into a pooled array that is returned when the enumeration ends or is disposed.</summary>
    /// <inheritdoc cref="FromMemory{T}(ReadOnlyMemory{byte}, int?, string, ReadOptions?, RecordReader{T})"/>
    public static IEnumerable<T> FromSequence<T>(ReadOnlySequence<byte> source, int? size, string layoutName, ReadOptions? options, RecordReader<T> read)
    {
        ArgumentNullException.ThrowIfNull(read);
        return source.IsSingleSegment ? Enumerate(source.First, size, layoutName, 0, options, read) : Copied(source, size, layoutName, options, read);
    }

    /// <summary>The records of a stream, read from its current position: exactly one record at a time when <paramref name="size"/> is known (any readable stream), else through a window that refills (a seekable stream, which the caller checks).</summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="stream">The readable stream whose current position is the first record's start.</param>
    /// <param name="size">The record's fixed size, or <see langword="null"/> for a runtime-sized record.</param>
    /// <param name="layoutName">The record's layout name, for the trailing-bytes failure.</param>
    /// <param name="options">The read options each record is read with.</param>
    /// <param name="read">Reads one record.</param>
    /// <returns>The records, read as they are enumerated.</returns>
    public static IEnumerable<T> FromStream<T>(Stream stream, int? size, string layoutName, ReadOptions? options, RecordReader<T> read)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(read);
        return size is { } fixedSize ? FixedFromStream(stream, fixedSize, layoutName, options, read) : Windowed(stream, layoutName, options, read);
    }

    /// <summary>The records of a stream read with <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>; the rules of <see cref="FromStream{T}(Stream, int?, string, ReadOptions?, RecordReader{T})"/>, with <paramref name="cancellationToken"/> linked to the options' token.</summary>
    /// <param name="stream">The readable stream whose current position is the first record's start.</param>
    /// <param name="size">The record's fixed size, or <see langword="null"/> for a runtime-sized record.</param>
    /// <param name="layoutName">The record's layout name, for the trailing-bytes failure.</param>
    /// <param name="options">The read options each record is read with.</param>
    /// <param name="read">Reads one record.</param>
    /// <param name="cancellationToken">Ends the enumeration while it waits for bytes, between records, or at the next boundary the reader checks.</param>
    /// <typeparam name="T">The record type.</typeparam>
    /// <returns>The records, read as they are enumerated.</returns>
    public static IAsyncEnumerable<T> FromStreamAsync<T>(Stream stream, int? size, string layoutName, ReadOptions? options, RecordReader<T> read, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(read);
        return size is { } fixedSize ? FixedFromStreamAsync(stream, fixedSize, layoutName, options, read, cancellationToken) : WindowedAsync(stream, layoutName, options, read, cancellationToken);
    }

    /// <summary>Trailing bytes shorter than one fixed-size record: the partial-element text of a read-to-end array, at the record's index.</summary>
    /// <param name="remaining">The bytes left.</param>
    /// <param name="size">The record's size.</param>
    /// <param name="layoutName">The record's layout name.</param>
    /// <param name="index">The record's index.</param>
    /// <param name="offset">Where the bytes start, in the input's coordinates.</param>
    /// <returns>The exception to throw.</returns>
    public static CStructReadException Partial(long remaining, int size, string layoutName, int index, long offset)
    {
        var failure = new CStructReadException(ReadFailures.ToEndRemainder(remaining, size, layoutName));
        failure.AttachContext(Prefix(index) + "." + layoutName, offset);
        return failure;
    }

    /// <summary>A record that occupies no bytes would repeat forever; the sequence stops with a failure instead.</summary>
    /// <param name="layoutName">The record's layout name.</param>
    /// <param name="index">The record's index.</param>
    /// <param name="offset">Where the record starts, in the input's coordinates.</param>
    /// <returns>The exception to throw.</returns>
    public static CStructReadException Empty(string layoutName, int index, long offset)
    {
        var failure = new CStructReadException("Record " + index.ToString(CultureInfo.InvariantCulture) + " of '" + layoutName + "' occupies no bytes; a sequence of records needs a root that reads at least one byte.");
        failure.AttachContext(Prefix(index) + "." + layoutName, offset);
        return failure;
    }

    /// <summary>Qualifies a failure raised inside record <paramref name="index"/>: the index before the path, the offset moved into the input's coordinates.</summary>
    /// <param name="failure">The failure, with the record's own path and offset attached.</param>
    /// <param name="index">The record's index.</param>
    /// <param name="offset">Where the record starts, in the input's coordinates.</param>
    public static void Complete(CStructException failure, int index, long offset)
    {
        ArgumentNullException.ThrowIfNull(failure);
        failure.PrefixPath(Prefix(index));
        failure.ShiftOffset(offset);
    }

    private static string Prefix(int index) => "[" + index.ToString(CultureInfo.InvariantCulture) + "]";

    /// <summary>One record at <paramref name="offset"/>: the trailing-bytes check for a fixed size, the read, the zero-length guard.</summary>
    private static T ReadOne<T>(ReadOnlyMemory<byte> source, int offset, int index, long shift, int? size, string layoutName, ReadOptions? options, RecordReader<T> read, out int consumed)
    {
        if (size is { } fixedSize && source.Length - offset < fixedSize)
        {
            throw Partial(source.Length - offset, fixedSize, layoutName, index, shift + offset);
        }

        T record = read(source, offset, index, shift, options, out consumed);
        if (consumed <= 0)
        {
            throw Empty(layoutName, index, shift + offset);
        }

        return record;
    }

    private static IEnumerable<T> Enumerate<T>(ReadOnlyMemory<byte> source, int? size, string layoutName, long shift, ReadOptions? options, RecordReader<T> read)
    {
        int offset = 0;
        for (int index = 0; offset < source.Length; index++)
        {
            T record = ReadOne(source, offset, index, shift, size, layoutName, options, read, out int consumed);
            offset += consumed;
            yield return record;
        }
    }

    private static IEnumerable<T> Copied<T>(ReadOnlySequence<byte> source, int? size, string layoutName, ReadOptions? options, RecordReader<T> read)
    {
        int length = checked((int)source.Length);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        try
        {
            source.CopyTo(buffer);
            foreach (T record in Enumerate(new ReadOnlyMemory<byte>(buffer, 0, length), size, layoutName, 0, options, read))
            {
                yield return record;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static IEnumerable<T> FixedFromStream<T>(Stream stream, int size, string layoutName, ReadOptions? options, RecordReader<T> read)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(size, 1));
        try
        {
            long origin = stream.CanSeek ? stream.Position : 0;
            long consumedTotal = 0;
            for (int index = 0; ; index++)
            {
                options?.CancellationToken.ThrowIfCancellationRequested();
                int got = Fill(stream, buffer, size);
                if (got == 0)
                {
                    yield break;
                }

                if (got < size)
                {
                    throw Partial(got, size, layoutName, index, origin + consumedTotal);
                }

                T record = ReadOne(new ReadOnlyMemory<byte>(buffer, 0, size), 0, index, origin + consumedTotal, size, layoutName, options, read, out _);
                consumedTotal += size;
                yield return record;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async IAsyncEnumerable<T> FixedFromStreamAsync<T>(Stream stream, int size, string layoutName, ReadOptions? options, RecordReader<T> read, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CancellationToken token = AsyncStreamBuffer.Link(options, cancellationToken, out CancellationTokenSource? linked);
        using (linked)
        {
            ReadOptions? effective = Effective(options, token);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(size, 1));
            try
            {
                long origin = stream.CanSeek ? stream.Position : 0;
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
                        throw Partial(got, size, layoutName, index, origin + consumedTotal);
                    }

                    T record = ReadOne(new ReadOnlyMemory<byte>(buffer, 0, size), 0, index, origin + consumedTotal, size, layoutName, effective, read, out _);
                    consumedTotal += size;
                    yield return record;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    ///     Runtime-sized records through a window of the bytes left, first at most the budget plus one. A window that
    ///     reached the end holds every remaining record. A shorter one is read with options that say the input
    ///     continues - by the stream's remaining length for the record at the window's start, by an unknown length for a
    ///     later one - so a record that needs bytes past the window raises the buffered-input signal instead of failing
    ///     (<see cref="BufferedInput"/>): a later record is read again from a window that starts at it, and a record that
    ///     starts the window makes the window grow. Any failure is therefore the record's own, as from the whole input.
    /// </summary>
    private static IEnumerable<T> Windowed<T>(Stream stream, string layoutName, ReadOptions? options, RecordReader<T> read)
    {
        long origin = stream.Position;
        long consumedTotal = 0;
        int index = 0;
        while (origin + consumedTotal < stream.Length)
        {
            options?.CancellationToken.ThrowIfCancellationRequested();
            long start = origin + consumedTotal;
            stream.Position = start;
            int capacity = AsyncStreamBuffer.Capacity(stream, options);
            byte[] window = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                int length = AsyncStreamBuffer.Fill(stream, window, 0, capacity);
                long left = stream.Length - start;
                bool complete = BufferedInput.IsComplete(length, capacity, left);
                ReadOptions? head = null;
                ReadOptions? partial = null;
                int offset = 0;
                while (offset < length)
                {
                    T record;
                    int consumed;
                    long needed = 0;
                    try
                    {
                        // The options record that the input continues past the window, once per window: a record at the window's
                        // start knows how far (the stream's remaining length), a later one does not, and refills from its start.
                        record = ReadOne(new ReadOnlyMemory<byte>(window, 0, length), offset, index, start, null, layoutName, complete ? options : offset == 0 ? head ??= BufferedInput.Continue(options, left) : partial ??= Continued(options), read, out consumed);
                    }
                    catch (BufferedInputShortfallException shortfall) when (!complete)
                    {
                        needed = shortfall.NeededLength;
                        record = default!;
                        consumed = 0;
                    }

                    if (needed > 0)
                    {
                        if (offset > 0)
                        {
                            // Refill from this record's start.
                            break;
                        }

                        capacity = BufferedInput.NextLength(length, needed, left);
                        stream.Position = start + length;
                        window = AsyncStreamBuffer.Grow(window, length, capacity, ArrayPool<byte>.Shared);
                        length = AsyncStreamBuffer.Fill(stream, window, length, capacity);
                        complete = BufferedInput.IsComplete(length, capacity, left);
                        continue;
                    }

                    offset += consumed;
                    index++;
                    stream.Position = start + offset;
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

    /// <summary>
    ///     The awaitable form of <see cref="Windowed{T}"/>: the same windows, read with
    ///     <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>.
    /// </summary>
    /// <typeparam name="T">The record type.</typeparam>
    /// <param name="stream">The seekable stream whose current position is the first record's start.</param>
    /// <param name="layoutName">The root's layout name, reported by failures.</param>
    /// <param name="options">The read options applied to every record, or <see langword="null"/>.</param>
    /// <param name="read">The one-record reader.</param>
    /// <param name="cancellationToken">Linked with the options' token; ends the enumeration while it waits for bytes.</param>
    /// <returns>The records, read as they are enumerated.</returns>
    private static async IAsyncEnumerable<T> WindowedAsync<T>(Stream stream, string layoutName, ReadOptions? options, RecordReader<T> read, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        CancellationToken token = AsyncStreamBuffer.Link(options, cancellationToken, out CancellationTokenSource? linked);
        using (linked)
        {
            ReadOptions? effective = Effective(options, token);
            long origin = stream.Position;
            long consumedTotal = 0;
            int index = 0;
            while (origin + consumedTotal < stream.Length)
            {
                token.ThrowIfCancellationRequested();
                long start = origin + consumedTotal;
                stream.Position = start;
                int capacity = AsyncStreamBuffer.Capacity(stream, effective);
                byte[] window = ArrayPool<byte>.Shared.Rent(capacity);
                try
                {
                    int length = await AsyncStreamBuffer.FillAsync(stream, window, 0, capacity, token).ConfigureAwait(false);
                    long left = stream.Length - start;
                    bool complete = BufferedInput.IsComplete(length, capacity, left);
                    ReadOptions? head = null;
                    ReadOptions? partial = null;
                    int offset = 0;
                    while (offset < length)
                    {
                        T record;
                        int consumed;
                        long needed = 0;
                        try
                        {
                            // The options record that the input continues past the window, once per window: a record at the window's
                            // start knows how far (the stream's remaining length), a later one does not, and refills from its start.
                            record = ReadOne(new ReadOnlyMemory<byte>(window, 0, length), offset, index, start, null, layoutName, complete ? effective : offset == 0 ? head ??= BufferedInput.Continue(effective, left) : partial ??= Continued(effective), read, out consumed);
                        }
                        catch (BufferedInputShortfallException shortfall) when (!complete)
                        {
                            needed = shortfall.NeededLength;
                            record = default!;
                            consumed = 0;
                        }

                        if (needed > 0)
                        {
                            if (offset > 0)
                            {
                                // Refill from this record's start.
                                break;
                            }

                            capacity = BufferedInput.NextLength(length, needed, left);
                            stream.Position = start + length;
                            window = AsyncStreamBuffer.Grow(window, length, capacity, ArrayPool<byte>.Shared);
                            length = await AsyncStreamBuffer.FillAsync(stream, window, length, capacity, token).ConfigureAwait(false);
                            complete = BufferedInput.IsComplete(length, capacity, left);
                            continue;
                        }

                        offset += consumed;
                        index++;
                        stream.Position = start + offset;
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
    }

    /// <summary>
    ///     The options a record after the start of a window that does not reach the end is read with: the input
    ///     continues past the window by an amount the record's region does not know (<see cref="BufferedInput.UnknownLength"/>).
    ///     A record that needs bytes past the window is read again from a window that starts at it, which knows the length.
    /// </summary>
    /// <param name="options">The options records are read with.</param>
    /// <returns>A copy that records the continuation.</returns>
    private static ReadOptions? Continued(ReadOptions? options) => BufferedInput.Continue(options, BufferedInput.UnknownLength);

    /// <summary>The options a record is read with: the linked token in place of the options' own when one can cancel.</summary>
    private static ReadOptions? Effective(ReadOptions? options, CancellationToken token)
        => token.CanBeCanceled ? (options ?? new ReadOptions()) with { CancellationToken = token, } : options;

    /// <summary>Reads up to <paramref name="count"/> bytes; fewer means the stream ended.</summary>
    private static int Fill(Stream stream, byte[] buffer, int count)
    {
        int got = 0;
        while (got < count)
        {
            int read = stream.Read(buffer, got, count - got);
            if (read <= 0)
            {
                break;
            }

            got += read;
        }

        return got;
    }

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
}
