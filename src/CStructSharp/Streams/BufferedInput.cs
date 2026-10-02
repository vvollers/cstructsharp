namespace CStructSharp.Streams;

using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Reading;

/// <summary>
///     The grow-and-rerun rule every buffered input form follows - a multi-segment <see cref="ReadOnlySequence{T}"/>,
///     the runtime's <c>*Async</c> operations, the generated <c>Parse(Stream)</c> and <c>ParseAsync</c>, and the
///     windows of a record sequence - so that each returns exactly what the span form returns for the whole input.
/// </summary>
/// <remarks>
///     <para>
///         A buffered form first copies at most <see cref="ReadOptions.MaxTotalBytesRead"/> plus one byte (and never
///         more than the input), then runs the span reader over that copy. The budget charges the bytes a read
///         consumes, not the position it reaches: alignment padding, pointer targets, path selection and a
///         <c>T v[EOF]</c> array's count move or look past bytes without charging them. So a value can need bytes past
///         the copy while staying within the budget.
///     </para>
///     <para>
///         The copy therefore records that the input continues (<see cref="ReadOptions.ContinuedInputLength"/>). A
///         reader that needs a byte past the copy - a read, a position, a scan to a terminator, a count to the end -
///         raises <see cref="BufferedInputShortfallException"/> instead of a failure, and the form doubles the copy (at
///         least to the byte the reader needed, never past the input's end) and runs the operation again. A read that
///         stays inside the copy pays nothing: the checks sit on the branches that would otherwise fail. A run that
///         raises no signal touched only bytes the copy holds (at the latest, once the copy holds the whole input), so
///         its values, failures, texts and offsets are the span form's.
///     </para>
///     <para>
///         A stream that cannot seek is consumed by what was buffered, which exceeds the budget plus one byte only
///         when the value addresses bytes past it. A copy never exceeds <see cref="Array.MaxLength"/> bytes; a value
///         that needs more fails with <see cref="CStructReadLimitException"/>.
///     </para>
/// </remarks>
internal static class BufferedInput
{
    /// <summary>The <see cref="ReadOptions.ContinuedInputLength"/> of bytes that are the whole input.</summary>
    public const long WholeInput = 0;

    /// <summary>The <see cref="ReadOptions.ContinuedInputLength"/> of bytes that continue into an input of unknown length (a stream that cannot seek).</summary>
    public const long UnknownLength = -1;

    /// <summary>The signal a reader raises for a byte past its buffered input; see the class remarks.</summary>
    /// <param name="neededLength">
    ///     The input length in bytes, from the buffer's first byte, the reader needs buffered; <see cref="long.MaxValue"/>
    ///     when it needs the whole input (to find its end).
    /// </param>
    /// <returns>The signal to throw.</returns>
    /// <remarks>
    ///     A length that overflowed (a far pointer target plus one byte, a position plus a huge count) is reported as
    ///     <see cref="long.MaxValue"/>, so every signal names a positive length and the forms that read on never mistake
    ///     it for success.
    /// </remarks>
    public static BufferedInputShortfallException Shortfall(long neededLength) => new(neededLength > 0 ? neededLength : long.MaxValue);

    /// <summary>
    ///     Throws <see cref="Shortfall"/>(<paramref name="neededLength"/>). The readers' hot methods call this rather
    ///     than constructing the signal inline, so the rarely taken branch adds only a call to them and they stay small
    ///     enough to inline.
    /// </summary>
    /// <param name="neededLength">The input length in bytes, from the buffer's first byte, the reader needs buffered.</param>
    /// <exception cref="BufferedInputShortfallException">Always.</exception>
    [DoesNotReturn]
    public static void ThrowShortfall(long neededLength) => throw Shortfall(neededLength);

    /// <summary>The options a run over a buffer that may not hold the whole input uses.</summary>
    /// <param name="options">The caller's options, or <see langword="null"/>.</param>
    /// <param name="continuedLength">
    ///     <see cref="WholeInput"/>, the whole input's length in bytes, or <see cref="UnknownLength"/>.
    /// </param>
    /// <returns><paramref name="options"/> itself for a whole input; otherwise a copy that records the continuation.</returns>
    public static ReadOptions? Continue(ReadOptions? options, long continuedLength)
        => continuedLength == WholeInput ? options : (options ?? ReadOptions.Default) with { ContinuedInputLength = continuedLength, };

    /// <summary>The first buffer length: the budget plus one byte, but never more than the input or one array.</summary>
    /// <param name="options">The read options that supply the budget, or <see langword="null"/>.</param>
    /// <param name="inputLength">The input's length in bytes, or <see cref="UnknownLength"/>.</param>
    /// <returns>The number of bytes to buffer first.</returns>
    public static int InitialLength(ReadOptions? options, long inputLength)
    {
        // Only the budget is needed, so it is read directly rather than through a snapshot of every setting.
        long length = Math.Min(Math.Max((options ?? ReadOptions.Default).MaxTotalBytesRead, 0), Array.MaxLength - 1) + 1;
        return (int)(inputLength >= 0 ? Math.Min(length, inputLength) : length);
    }

    /// <summary>
    ///     The next buffer length after a shortfall: twice the current one, at least the length the reader needed, at
    ///     most the input's length and one array.
    /// </summary>
    /// <param name="length">The bytes buffered now.</param>
    /// <param name="neededLength">The length the reader needed, from <see cref="BufferedInputShortfallException.NeededLength"/>.</param>
    /// <param name="inputLength">The input's length in bytes, or <see cref="UnknownLength"/>.</param>
    /// <returns>A length greater than <paramref name="length"/>.</returns>
    /// <exception cref="CStructReadLimitException">The buffer already holds the largest array while the input continues.</exception>
    public static int NextLength(int length, long neededLength, long inputLength)
    {
        long cap = inputLength >= 0 ? Math.Min(inputLength, Array.MaxLength) : Array.MaxLength;
        if (length >= cap)
        {
            throw new CStructReadLimitException(ReadFailures.BufferedInputLimit);
        }

        long target = Math.Max(Math.Max(length * 2L, neededLength), length + 1L);
        return (int)Math.Min(target, cap);
    }

    /// <summary>
    ///     The next buffer length for one growth step of a stream: <see cref="NextLength"/> when the stream's length is
    ///     known; otherwise twice the current length (at most one array), even when the reader needed more. A stream that
    ///     cannot seek may end long before a far pointer target or the end a <c>T v[EOF]</c> count asks for
    ///     (<see cref="long.MaxValue"/>), so the buffer doubles while the stream still gives bytes rather than renting one
    ///     array of the needed length up front.
    /// </summary>
    /// <param name="length">The bytes buffered now.</param>
    /// <param name="neededLength">The length the reader needed, from <see cref="BufferedInputShortfallException.NeededLength"/>.</param>
    /// <param name="inputLength">The input's length in bytes, or <see cref="UnknownLength"/>.</param>
    /// <returns>A length greater than <paramref name="length"/>.</returns>
    /// <exception cref="CStructReadLimitException">The buffer already holds the largest array while the input continues.</exception>
    public static int NextStreamLength(int length, long neededLength, long inputLength)
        => NextLength(length, inputLength >= 0 ? neededLength : Math.Min(neededLength, length * 2L), inputLength);

    /// <summary>
    ///     Copies the first part of a multi-segment sequence into a pooled array for the first run: at most the budget
    ///     plus one byte (<see cref="InitialLength"/>). The caller runs the span reader over a whole copy directly, hands
    ///     a partial one to <see cref="ReadPartialSequence"/>, and returns the array to <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <param name="source">The sequence; its first byte is coordinate zero.</param>
    /// <param name="options">The caller's read options, or <see langword="null"/>.</param>
    /// <param name="length">The bytes copied.</param>
    /// <param name="continuation">
    ///     <see cref="WholeInput"/> when the copy holds the whole sequence, otherwise the sequence's length.
    /// </param>
    /// <returns>The rented array.</returns>
    public static byte[] CopySequence(ReadOnlySequence<byte> source, ReadOptions? options, out int length, out long continuation)
    {
        long total = source.Length;
        length = InitialLength(options, total);
        continuation = length >= total ? WholeInput : total;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        try
        {
            source.Slice(0, length).CopyTo(buffer);
            return buffer;
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>
    ///     Continues a sequence read whose first run (over <see cref="CopySequence"/>'s copy) raised a shortfall: the
    ///     sequence is copied again from its start into larger pooled arrays (see <see cref="NextLength"/>) and the span
    ///     reader runs over each until a run needs no byte past its copy. Every copy is returned to the pool before the
    ///     method returns; the first copy stays the caller's.
    /// </summary>
    /// <typeparam name="TReader">The span reader's struct type, so each run is a direct call.</typeparam>
    /// <typeparam name="TResult">The value type.</typeparam>
    /// <param name="source">The sequence; its first byte is coordinate zero.</param>
    /// <param name="options">The caller's read options, or <see langword="null"/>.</param>
    /// <param name="reader">The span reader, with the caller's state.</param>
    /// <param name="length">The bytes the run that came up short had.</param>
    /// <param name="needed">The length that run needed, from <see cref="BufferedInputShortfallException.NeededLength"/>.</param>
    /// <param name="consumed">Where the successful read ended, in bytes from the sequence's start.</param>
    /// <returns>The value the span form returns for the whole sequence.</returns>
    /// <exception cref="CStructReadLimitException">The value needs more of the sequence than one array can hold.</exception>
    public static TResult ResumeSequence<TReader, TResult>(ReadOnlySequence<byte> source, ReadOptions? options, TReader reader, int length, long needed, out long consumed)
        where TReader : struct, IBufferedReader<TResult>
    {
        long total = source.Length;
        while (true)
        {
            // A segmented sequence is copied again from its start: growth is rare, and the copy stays one array.
            length = NextLength(length, needed, total);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                source.Slice(0, length).CopyTo(buffer);
                bool complete = length >= total;
                try
                {
                    return reader.Read(new ReadOnlySpan<byte>(buffer, 0, length), complete ? options : Continue(options, total), out consumed);
                }
                catch (BufferedInputShortfallException shortfall) when (!complete)
                {
                    needed = shortfall.NeededLength;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    ///     Reads a multi-segment sequence through a pooled copy that grows while the reader needs bytes past it; see the
    ///     class remarks. The copies are returned to the pool before the method returns.
    /// </summary>
    /// <typeparam name="TReader">The span reader's struct type, so each run is a direct call.</typeparam>
    /// <typeparam name="TResult">The value type.</typeparam>
    /// <param name="source">The sequence; its first byte is coordinate zero.</param>
    /// <param name="options">The caller's read options, or <see langword="null"/>.</param>
    /// <param name="reader">The span reader, with the caller's state.</param>
    /// <returns>The value the span form returns for the whole sequence.</returns>
    public static TResult ReadSequence<TReader, TResult>(ReadOnlySequence<byte> source, ReadOptions? options, TReader reader)
        where TReader : struct, IBufferedReader<TResult>
    {
        byte[] buffer = CopySequence(source, options, out int length, out long continuation);
        try
        {
            // A copy of the whole sequence cannot come up short, so only a partial one pays for the growth path.
            return continuation == WholeInput
                       ? reader.Read(new ReadOnlySpan<byte>(buffer, 0, length), options, out _)
                       : ReadPartialSequence<TReader, TResult>(source, options, reader, buffer, length, continuation);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>
    ///     The first run over a copy that holds only part of a multi-segment sequence, then <see cref="ResumeSequence"/>
    ///     when that run needs bytes past the copy. Kept apart from <see cref="ReadSequence"/> so a whole copy's read
    ///     carries no exception handler for the growth signal.
    /// </summary>
    /// <typeparam name="TReader">The span reader's struct type, so each run is a direct call.</typeparam>
    /// <typeparam name="TResult">The value type.</typeparam>
    /// <param name="source">The sequence; its first byte is coordinate zero.</param>
    /// <param name="options">The caller's read options, or <see langword="null"/>.</param>
    /// <param name="reader">The span reader, with the caller's state.</param>
    /// <param name="buffer">The first copy, which stays the caller's.</param>
    /// <param name="length">The bytes the first copy holds.</param>
    /// <param name="continuation">The sequence's length, as <see cref="CopySequence"/> reported it.</param>
    /// <returns>The value the span form returns for the whole sequence.</returns>
    public static TResult ReadPartialSequence<TReader, TResult>(ReadOnlySequence<byte> source, ReadOptions? options, TReader reader, byte[] buffer, int length, long continuation)
        where TReader : struct, IBufferedReader<TResult>
    {
        try
        {
            return reader.Read(new ReadOnlySpan<byte>(buffer, 0, length), Continue(options, continuation), out _);
        }
        catch (BufferedInputShortfallException shortfall)
        {
            return ResumeSequence<TReader, TResult>(source, options, reader, length, shortfall.NeededLength, out _);
        }
    }

    /// <summary>
    ///     What a stream buffer filled from <paramref name="origin"/> records for its run (<see cref="Continue"/>):
    ///     <see cref="WholeInput"/> when the stream ended inside the buffer or the buffer reached a seekable stream's
    ///     end, otherwise the seekable stream's remaining length or <see cref="UnknownLength"/>. The common case - a
    ///     fill that came up short of its capacity - queries nothing.
    /// </summary>
    /// <param name="stream">The stream the buffer was filled from.</param>
    /// <param name="origin">The stream position of the buffer's first byte (any value for a stream that cannot seek).</param>
    /// <param name="length">The bytes buffered.</param>
    /// <param name="capacity">The bytes the fill asked for.</param>
    /// <returns>The continuation to record.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Continuation(Stream stream, long origin, int length, int capacity)
        => length < capacity ? WholeInput : FilledContinuation(stream, origin, length);

    /// <summary>
    ///     <see cref="Continuation(Stream, long, int, int)"/> for a caller that holds the rented buffer outside any
    ///     handler (an async read loop between its await and its run): when the stream's length query throws, the buffer
    ///     goes back to <see cref="ArrayPool{T}.Shared"/> before the failure escapes.
    /// </summary>
    /// <param name="stream">The stream the buffer was filled from.</param>
    /// <param name="origin">The stream position of the buffer's first byte (any value for a stream that cannot seek).</param>
    /// <param name="length">The bytes buffered.</param>
    /// <param name="capacity">The bytes the fill asked for.</param>
    /// <param name="buffer">The rented buffer; ownership stays with the caller unless the query throws.</param>
    /// <returns>The continuation to record.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Continuation(Stream stream, long origin, int length, int capacity, byte[] buffer)
        => length < capacity ? WholeInput : FilledContinuationReturning(stream, origin, length, buffer);

    /// <summary>
    ///     <see cref="FilledContinuation"/> that returns <paramref name="buffer"/> to the pool when the stream query
    ///     throws. Kept out of line so the handler stays off the read forms' common path.
    /// </summary>
    /// <param name="stream">The stream the buffer was filled from.</param>
    /// <param name="origin">The stream position of the buffer's first byte (any value for a stream that cannot seek).</param>
    /// <param name="length">The bytes buffered.</param>
    /// <param name="buffer">The rented buffer to return on failure.</param>
    /// <returns>The continuation to record.</returns>
    private static long FilledContinuationReturning(Stream stream, long origin, int length, byte[] buffer)
    {
        try
        {
            return FilledContinuation(stream, origin, length);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>
    ///     <see cref="Continuation(Stream, long, int, int)"/> for a buffer the fill filled to its capacity: whether the input goes on depends on
    ///     the stream. Kept out of line so the common check inlines into the read forms.
    /// </summary>
    /// <param name="stream">The stream the buffer was filled from.</param>
    /// <param name="origin">The stream position of the buffer's first byte (any value for a stream that cannot seek).</param>
    /// <param name="length">The bytes buffered.</param>
    /// <returns>The continuation to record.</returns>
    private static long FilledContinuation(Stream stream, long origin, int length)
    {
        if (!stream.CanSeek)
        {
            return UnknownLength;
        }

        long total = Math.Max(0, stream.Length - origin);
        return length >= total ? WholeInput : total;
    }

    /// <summary>
    ///     Runs the span reader over a stream buffer that holds only the first part of the input, growing the buffer
    ///     (<see cref="GrowStream"/>) and running again while the reader needs bytes past it. Kept apart from the
    ///     callers' common path, so a buffer that holds the whole input carries no exception handler for the signal.
    /// </summary>
    /// <typeparam name="TReader">The span reader's struct type, so each run is a direct call.</typeparam>
    /// <typeparam name="TResult">The value type.</typeparam>
    /// <param name="stream">The stream, positioned just after the buffered bytes.</param>
    /// <param name="origin">The stream position of the buffer's first byte (any value for a stream that cannot seek).</param>
    /// <param name="reader">The span reader, with the caller's state.</param>
    /// <param name="options">The read options, or <see langword="null"/>.</param>
    /// <param name="buffer">The buffer, rented from <see cref="ArrayPool{T}.Shared"/>; ownership moves to this method, which returns it.</param>
    /// <param name="length">The bytes it holds.</param>
    /// <param name="continuation">What <see cref="Continuation(Stream, long, int, int)"/> reported for the buffer.</param>
    /// <param name="consumed">Where the successful read ended, in bytes from the buffer's first byte.</param>
    /// <returns>The value the span form returns for the stream's remaining bytes.</returns>
    public static TResult ReadPartialStream<TReader, TResult>(Stream stream, long origin, TReader reader, ReadOptions? options, byte[] buffer, int length, long continuation, out long consumed)
        where TReader : struct, IBufferedReader<TResult>
    {
        byte[]? owned = buffer;
        try
        {
            while (true)
            {
                long needed;
                try
                {
                    return reader.Read(new ReadOnlySpan<byte>(owned, 0, length), Continue(options, continuation), out consumed);
                }
                catch (BufferedInputShortfallException shortfall) when (continuation != WholeInput)
                {
                    needed = shortfall.NeededLength;
                }

                // The array moves to the growth step, which returns a larger one (or its own on failure).
                byte[] current = owned;
                owned = null;
                owned = GrowStream(stream, origin, current, length, needed, out length, out int capacity);
                continuation = Continuation(stream, origin, length, capacity);
            }
        }
        finally
        {
            if (owned is not null)
            {
                ArrayPool<byte>.Shared.Return(owned);
            }
        }
    }

    /// <summary>
    ///     One run of the span reader over an awaitable stream buffer that holds only the first part of the input: the
    ///     synchronous half of the async forms' growth loop, kept apart from their common path. The buffer's ownership
    ///     moves to this method.
    /// </summary>
    /// <typeparam name="TReader">The span reader's struct type, so each run is a direct call.</typeparam>
    /// <typeparam name="TResult">The value type.</typeparam>
    /// <param name="stream">The stream, positioned just after the buffered bytes.</param>
    /// <param name="origin">The stream position of the buffer's first byte (any value for a stream that cannot seek).</param>
    /// <param name="reader">The span reader, with the caller's state.</param>
    /// <param name="options">The read options with the operation's token, or <see langword="null"/>.</param>
    /// <param name="buffer">The buffer, rented from <see cref="ArrayPool{T}.Shared"/>.</param>
    /// <param name="length">The bytes it holds.</param>
    /// <param name="continuation">What <see cref="Continuation(Stream, long, int, int)"/> reported for the buffer.</param>
    /// <param name="value">The value when the run succeeded.</param>
    /// <param name="consumed">Where the successful read ended, in bytes from the buffer's first byte.</param>
    /// <param name="growth">
    ///     When the run needed bytes past the buffer: the growth step (<see cref="GrowStreamAsync"/>), which now owns
    ///     the buffer; the caller awaits it and runs again over the larger buffer.
    /// </param>
    /// <returns>Whether the run succeeded; on success and on failure the buffer has been returned to the pool.</returns>
    public static bool TryReadPartialStream<TReader, TResult>(Stream stream, long origin, TReader reader, ReadOptions? options, byte[] buffer, int length, long continuation, [MaybeNullWhen(false)] out TResult value, out long consumed, out ValueTask<(byte[] Buffer, int Length, int Capacity)> growth)
        where TReader : struct, IBufferedReader<TResult>
    {
        bool owned = true;
        try
        {
            value = reader.Read(new ReadOnlySpan<byte>(buffer, 0, length), Continue(options, continuation), out consumed);
            growth = default;
            return true;
        }
        catch (BufferedInputShortfallException shortfall) when (continuation != WholeInput)
        {
            owned = false;
            growth = GrowStreamAsync(stream, origin, buffer, length, shortfall.NeededLength, options?.CancellationToken ?? default);
            value = default;
            consumed = 0;
            return false;
        }
        finally
        {
            if (owned)
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    /// <summary>
    ///     One growth step of a stream buffer after a shortfall: the buffered bytes move into a larger pooled array (see
    ///     <see cref="NextStreamLength"/>) that is filled from the stream; a stream of unknown length keeps doubling while
    ///     it fills every array and still falls short of <paramref name="needed"/>.
    /// </summary>
    /// <param name="stream">The stream, positioned just after the buffered bytes.</param>
    /// <param name="origin">The stream position of the buffer's first byte (any value for a stream that cannot seek).</param>
    /// <param name="buffer">The current array, rented from <see cref="ArrayPool{T}.Shared"/>; ownership moves to this method.</param>
    /// <param name="length">The bytes it holds.</param>
    /// <param name="needed">The length the reader needed, from <see cref="BufferedInputShortfallException.NeededLength"/>.</param>
    /// <param name="grownLength">The bytes the returned array holds.</param>
    /// <param name="capacity">The bytes the last fill asked for, for <see cref="Continuation(Stream, long, int, int)"/>.</param>
    /// <returns>The larger array, which the caller returns to the pool.</returns>
    /// <exception cref="CStructReadLimitException">The buffer already holds the largest array while the input continues.</exception>
    /// <remarks>On failure the method returns whichever array it holds to the pool.</remarks>
    public static byte[] GrowStream(Stream stream, long origin, byte[] buffer, int length, long needed, out int grownLength, out int capacity)
    {
        try
        {
            // The length query is inside the handler too: a stream that fails it still gets its array back to the pool.
            long total = stream.CanSeek ? Math.Max(0, stream.Length - origin) : UnknownLength;
            do
            {
                capacity = NextStreamLength(length, needed, total);
                buffer = AsyncStreamBuffer.Grow(buffer, length, capacity, ArrayPool<byte>.Shared);
                length = AsyncStreamBuffer.Fill(stream, buffer, length, capacity);
            }
            while (total < 0 && length == capacity && length < needed);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }

        grownLength = length;
        return buffer;
    }

    /// <summary>The awaitable form of <see cref="GrowStream"/>, with <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>.</summary>
    /// <param name="stream">The stream, positioned just after the buffered bytes.</param>
    /// <param name="origin">The stream position of the buffer's first byte (any value for a stream that cannot seek).</param>
    /// <param name="buffer">The current array, rented from <see cref="ArrayPool{T}.Shared"/>; ownership moves to this method.</param>
    /// <param name="length">The bytes it holds.</param>
    /// <param name="needed">The length the reader needed, from <see cref="BufferedInputShortfallException.NeededLength"/>.</param>
    /// <param name="cancellationToken">The token observed during each read.</param>
    /// <returns>
    ///     The larger array, which the caller returns to the pool, the bytes it holds, and the bytes the last fill asked
    ///     for - the shape <see cref="AsyncStreamBuffer.RentAsync(Stream, ReadOptions?, CancellationToken)"/> returns, so
    ///     a caller awaits both through one awaiter.
    /// </returns>
    /// <exception cref="CStructReadLimitException">The buffer already holds the largest array while the input continues.</exception>
    /// <remarks>On failure the method returns whichever array it holds to the pool.</remarks>
    public static async ValueTask<(byte[] Buffer, int Length, int Capacity)> GrowStreamAsync(Stream stream, long origin, byte[] buffer, int length, long needed, CancellationToken cancellationToken)
    {
        try
        {
            // The length query is inside the handler too: a stream that fails it still gets its array back to the pool.
            long total = stream.CanSeek ? Math.Max(0, stream.Length - origin) : UnknownLength;
            int capacity;
            do
            {
                capacity = NextStreamLength(length, needed, total);
                buffer = AsyncStreamBuffer.Grow(buffer, length, capacity, ArrayPool<byte>.Shared);
                length = await AsyncStreamBuffer.FillAsync(stream, buffer, length, capacity, cancellationToken).ConfigureAwait(false);
            }
            while (total < 0 && length == capacity && length < needed);

            return (buffer, length, capacity);
        }
        catch
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }

    /// <summary>
    ///     Whether a buffer holds the whole remaining input: the stream ended before the buffer filled, or the buffer
    ///     reached the length a seekable stream reported.
    /// </summary>
    /// <param name="length">The bytes buffered.</param>
    /// <param name="capacity">The bytes the fill asked for.</param>
    /// <param name="inputLength">The remaining input length, or <see cref="UnknownLength"/>.</param>
    /// <returns>Whether the input ends inside the buffer.</returns>
    public static bool IsComplete(int length, int capacity, long inputLength) => length < capacity || (inputLength >= 0 && length >= inputLength);
}
