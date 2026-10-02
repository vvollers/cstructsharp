namespace CStructSharp.Generated;

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CStructSharp.Streams;

/// <summary>
///     The buffered input forms of a generated read: a segmented sequence or a stream is read through a pooled buffer
///     that grows while the span reader needs bytes past it, so the value, failures and offsets are those of the span
///     form over the whole input (<see cref="BufferedInput"/>).
/// </summary>
public ref partial struct ReadCursor
{
    /// <summary>
    ///     Reads a multi-segment <see cref="ReadOnlySequence{T}"/> with the span reader <paramref name="reader"/>: the
    ///     sequence is copied into a pooled array - first at most the total read budget plus one byte - and the copy
    ///     grows and the read runs again while the reader needs bytes past it. A single-segment sequence needs no copy;
    ///     callers read its <see cref="ReadOnlySequence{T}.FirstSpan"/> directly.
    /// </summary>
    /// <typeparam name="TReader">The span reader's struct type, so each run is a direct call (the generated code's reader carries its layout variables).</typeparam>
    /// <typeparam name="TResult">The value type.</typeparam>
    /// <param name="source">The sequence; offset 0 is coordinate zero.</param>
    /// <param name="reader">The span reader with the operation's state, run over each buffer.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The value the span reader returns for the whole sequence.</returns>
    /// <exception cref="Diagnostics.CStructReadLimitException">The value needs more of the sequence than one array can hold.</exception>
    public static TResult ReadSequence<TReader, TResult>(ReadOnlySequence<byte> source, TReader reader, ReadOptions? options)
        where TReader : struct, IBufferedReader<TResult>
        => BufferedInput.ReadSequence<TReader, TResult>(source, options, reader);

    /// <summary>
    ///     Reads a value from <paramref name="stream"/>'s current position with the span reader <paramref name="reader"/>:
    ///     the stream is read into a pooled buffer - first a seekable stream up to its remaining length, any stream up
    ///     to the total read budget, plus one byte - which grows while the reader needs bytes past it. A seekable stream
    ///     is left just after the value, or back at its origin on any failure; a stream that cannot seek is consumed by
    ///     what was buffered.
    /// </summary>
    /// <typeparam name="TReader">The span reader's struct type, so each run is a direct call (the generated code's reader carries its layout variables).</typeparam>
    /// <typeparam name="TResult">The value type.</typeparam>
    /// <param name="stream">The readable stream; its current position is coordinate zero.</param>
    /// <param name="reader">The span reader with the operation's state, run over each buffer.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The value the span reader returns for the stream's remaining bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public static TResult ReadStream<TReader, TResult>(Stream stream, TReader reader, ReadOptions? options)
        where TReader : struct, IBufferedReader<TResult>
    {
        ArgumentNullException.ThrowIfNull(stream);
        long start = stream.CanSeek ? stream.Position : 0;
        try
        {
            int capacity = AsyncStreamBuffer.Capacity(stream, options);
            byte[]? buffer = ArrayPool<byte>.Shared.Rent(capacity);
            try
            {
                int length = AsyncStreamBuffer.Fill(stream, buffer, 0, capacity);
                long continuation = BufferedInput.Continuation(stream, start, length, capacity);
                TResult value;
                long consumed;
                if (continuation == BufferedInput.WholeInput)
                {
                    value = reader.Read(new ReadOnlySpan<byte>(buffer, 0, length), options, out consumed);
                }
                else
                {
                    // Only part of the input is buffered: the growth path takes the array over.
                    byte[] partial = buffer;
                    buffer = null;
                    value = BufferedInput.ReadPartialStream<TReader, TResult>(stream, start, reader, options, partial, length, continuation, out consumed);
                }

                if (stream.CanSeek)
                {
                    stream.Position = start + consumed;
                }

                return value;
            }
            finally
            {
                if (buffer is not null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }
        }
        catch
        {
            RestoreOrigin(stream, start);
            throw;
        }
    }

    /// <summary>
    ///     The awaitable form of <see cref="ReadStream{TReader, TResult}"/>: the same buffering with
    ///     <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>. The token is linked with the options' own
    ///     token; it ends the read while it waits for bytes and at the boundaries the span reader checks.
    /// </summary>
    /// <typeparam name="TReader">The span reader's struct type, so each run is a direct call (the generated code's reader carries its layout variables).</typeparam>
    /// <typeparam name="TResult">The value type.</typeparam>
    /// <param name="stream">The readable stream; its current position is coordinate zero.</param>
    /// <param name="reader">The span reader with the operation's state, run over each buffer.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="cancellationToken">The token given to the async method.</param>
    /// <returns>The value the span reader returns for the stream's remaining bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException">A token was cancelled.</exception>
    public static async ValueTask<TResult> ReadStreamAsync<TReader, TResult>(Stream stream, TReader reader, ReadOptions? options, CancellationToken cancellationToken = default)
        where TReader : struct, IBufferedReader<TResult>
    {
        ArgumentNullException.ThrowIfNull(stream);
        CancellationToken token = AsyncStreamBuffer.Link(options, cancellationToken, out CancellationTokenSource? linked);
        using (linked)
        {
            ReadOptions? effective = token.CanBeCanceled ? (options ?? new ReadOptions()) with { CancellationToken = token, } : options;
            long start = stream.CanSeek ? stream.Position : 0;
            try
            {
                // The first fill and every growth step yield the same tuple, so one await serves both: the state machine
                // a stream that reads asynchronously boxes holds nothing for the growth path.
                ValueTask<(byte[] Buffer, int Length, int Capacity)> pending = AsyncStreamBuffer.RentAsync(stream, effective, token);
                while (true)
                {
                    (byte[] buffer, int length, int capacity) = await pending.ConfigureAwait(false);
                    long continuation = BufferedInput.Continuation(stream, start, length, capacity, buffer);
                    TResult? value;
                    long consumed;
                    if (continuation == BufferedInput.WholeInput)
                    {
                        try
                        {
                            value = reader.Read(new ReadOnlySpan<byte>(buffer, 0, length), effective, out consumed);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }
                    else if (!BufferedInput.TryReadPartialStream<TReader, TResult>(stream, start, reader, effective, buffer, length, continuation, out value, out consumed, out pending))
                    {
                        // The value needs bytes past the buffer; the growth step now owns it.
                        continue;
                    }

                    if (stream.CanSeek)
                    {
                        stream.Position = start + consumed;
                    }

                    return value!;
                }
            }
            catch
            {
                RestoreOrigin(stream, start);
                throw;
            }
        }
    }

    /// <summary>Moves a seekable stream back to the operation's origin after a failure, without replacing that failure.</summary>
    /// <param name="stream">The caller's stream.</param>
    /// <param name="start">The origin.</param>
    private static void RestoreOrigin(Stream stream, long start)
    {
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = start;
            }
        }
        catch
        {
            // Preserve the original failure if the underlying stream also refuses restoration.
        }
    }
}
