namespace CStructSharp.Streams;

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CStructSharp.Reading;

/// <summary>
///     The one buffering rule every stream form that runs the span reader follows (the generated <c>Parse(Stream)</c>,
///     the runtime's <c>*Async</c> operations): the input is read from the stream's current position into a pooled
///     array - a seekable stream up to its remaining length, any stream up to <see cref="ReadOptions.MaxTotalBytesRead"/>,
///     plus one byte so the reader reports a budget failure rather than a short read when the value is larger than
///     the budget - and the span reader runs over it. The caller returns the array to the pool.
/// </summary>
/// <remarks>
///     A seekable stream is left where the read loop stopped; the operation that used the buffer sets the final
///     position (just after the value on success, the origin on failure). A non-seekable stream is consumed by
///     whatever the loop read, which the documentation of every async form states.
/// </remarks>
internal static class AsyncStreamBuffer
{
    /// <summary>The number of bytes to buffer: the budget plus one, or the seekable stream's remaining length plus one when that is smaller.</summary>
    /// <param name="stream">The input stream; its length and position are consulted only when it can seek.</param>
    /// <param name="options">The read options that supply the byte budget, or <see langword="null"/>.</param>
    /// <returns>The buffer capacity in bytes, at most <c>int.MaxValue - 1</c>.</returns>
    public static int Capacity(Stream stream, ReadOptions? options)
    {
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(options);
        long limit = Math.Min(settings.MaxTotalBytesRead, int.MaxValue - 1);
        if (stream.CanSeek)
        {
            limit = Math.Min(limit, Math.Max(0, stream.Length - stream.Position));
        }

        return (int)Math.Min(limit + 1, int.MaxValue - 1);
    }

    /// <summary>Reads the input synchronously; see the class remarks.</summary>
    /// <param name="stream">The stream to read from its current position.</param>
    /// <param name="options">The read options that bound the byte count, or <see langword="null"/>.</param>
    /// <param name="length">Receives the number of bytes read into the returned array.</param>
    /// <returns>An array rented from <see cref="ArrayPool{T}.Shared"/>, which the caller returns.</returns>
    public static byte[] Rent(Stream stream, ReadOptions? options, out int length)
        => Rent(stream, options, ArrayPool<byte>.Shared, out length);

    /// <summary>Reads the input synchronously from <paramref name="pool"/>'s arrays; the tests supply a counting pool.</summary>
    /// <param name="stream">The stream to read from its current position.</param>
    /// <param name="options">The read options that bound the byte count, or <see langword="null"/>.</param>
    /// <param name="pool">The pool that supplies the array; the array returns to it if reading fails.</param>
    /// <param name="length">Receives the number of bytes read into the returned array.</param>
    /// <returns>The rented array, which the caller returns to <paramref name="pool"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    public static byte[] Rent(Stream stream, ReadOptions? options, ArrayPool<byte> pool, out int length)
    {
        ArgumentNullException.ThrowIfNull(stream);
        int capacity = Capacity(stream, options);
        byte[] buffer = pool.Rent(capacity);
        try
        {
            length = 0;
            while (length < capacity)
            {
                int read = stream.Read(buffer, length, capacity - length);
                if (read <= 0)
                {
                    break;
                }

                length += read;
            }

            return buffer;
        }
        catch
        {
            pool.Return(buffer);
            throw;
        }
    }

    /// <summary>Reads the input with <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>; see the class remarks.</summary>
    /// <param name="stream">The stream to read from its current position.</param>
    /// <param name="options">The read options that bound the byte count, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token observed before and during each read.</param>
    /// <returns>
    ///     An array rented from <see cref="ArrayPool{T}.Shared"/>, which the caller returns, and the number of bytes
    ///     read into it.
    /// </returns>
    public static ValueTask<(byte[] Buffer, int Length)> RentAsync(Stream stream, ReadOptions? options, CancellationToken cancellationToken)
        => RentAsync(stream, options, ArrayPool<byte>.Shared, cancellationToken);

    /// <summary>Reads the input asynchronously from <paramref name="pool"/>'s arrays; the tests supply a counting pool.</summary>
    /// <param name="stream">The stream to read from its current position.</param>
    /// <param name="options">The read options that bound the byte count, or <see langword="null"/>.</param>
    /// <param name="pool">The pool that supplies the array; the array returns to it if reading fails.</param>
    /// <param name="cancellationToken">The token observed before and during each read.</param>
    /// <returns>
    ///     The rented array, which the caller returns to <paramref name="pool"/>, and the number of bytes read into it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async ValueTask<(byte[] Buffer, int Length)> RentAsync(Stream stream, ReadOptions? options, ArrayPool<byte> pool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        cancellationToken.ThrowIfCancellationRequested();
        int capacity = Capacity(stream, options);
        byte[] buffer = pool.Rent(capacity);
        try
        {
            int length = 0;
            while (length < capacity)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(length, capacity - length), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                length += read;
            }

            return (buffer, length);
        }
        catch
        {
            pool.Return(buffer);
            throw;
        }
    }

    /// <summary>
    ///     The token an async operation observes: the parameter linked with the options' token when both can be
    ///     cancelled, else whichever can. The caller disposes the returned source, when there is one.
    /// </summary>
    /// <param name="options">The operation's options, whose token is linked in, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token passed to the async operation itself.</param>
    /// <param name="linked">
    ///     The linked source the caller must dispose after the operation, or <see langword="null"/> when no link
    ///     was needed.
    /// </param>
    /// <returns>The single token the operation observes.</returns>
    public static CancellationToken Link(ReadOptions? options, CancellationToken cancellationToken, out CancellationTokenSource? linked)
        => Link(options?.CancellationToken ?? default, cancellationToken, out linked);

    /// <inheritdoc cref="Link(ReadOptions?, CancellationToken, out CancellationTokenSource?)"/>
    public static CancellationToken Link(WriteOptions? options, CancellationToken cancellationToken, out CancellationTokenSource? linked)
        => Link(options?.CancellationToken ?? default, cancellationToken, out linked);

    /// <summary>Combines two tokens, linking them only when both can be cancelled.</summary>
    /// <param name="first">The options' token.</param>
    /// <param name="second">The caller's token.</param>
    /// <param name="linked">The linked source the caller disposes, or null when no link was needed.</param>
    /// <returns>The single token the operation observes.</returns>
    private static CancellationToken Link(CancellationToken first, CancellationToken second, out CancellationTokenSource? linked)
    {
        linked = null;
        if (!first.CanBeCanceled)
        {
            return second;
        }

        if (!second.CanBeCanceled)
        {
            return first;
        }

        linked = CancellationTokenSource.CreateLinkedTokenSource(first, second);
        return linked.Token;
    }
}
