namespace CStructSharp.Streams;

using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
///     The stream half of the buffering every stream form that runs the span reader shares (the generated
///     <c>Parse(Stream)</c> and <c>ParseAsync</c>, the runtime's <c>*Async</c> operations, the windows of a record
///     sequence): the input is read from the stream's current position into a pooled array - first a seekable stream up
///     to its remaining length, any stream up to <see cref="ReadOptions.MaxTotalBytesRead"/>, plus one byte - and the
///     array grows (<see cref="Grow"/>, <see cref="Fill"/>) when the reader needs bytes past it, as
///     <see cref="BufferedInput"/> decides. The caller returns the array to the pool.
/// </summary>
/// <remarks>
///     A seekable stream is left where the read loop stopped; the operation that used the buffer sets the final
///     position (just after the value on success, the origin on failure). A non-seekable stream is consumed by
///     whatever the loops read, which the documentation of every async form states.
/// </remarks>
internal static class AsyncStreamBuffer
{
    /// <summary>
    ///     The number of bytes to buffer first: the budget plus one, or the seekable stream's remaining length plus one
    ///     when that is smaller, never more than one array (<see cref="BufferedInput.InitialLength"/>, the rule the
    ///     sequence copy shares). The byte past a seekable stream's end lets a fill that comes up short show that the
    ///     buffer holds the whole input.
    /// </summary>
    /// <param name="stream">The input stream; its length and position are consulted only when it can seek.</param>
    /// <param name="options">The read options that supply the byte budget, or <see langword="null"/>.</param>
    /// <returns>The buffer capacity in bytes, from 1 to <see cref="Array.MaxLength"/>.</returns>
    public static int Capacity(Stream stream, ReadOptions? options)
    {
        long inputLength = stream.CanSeek ? Math.Min(Math.Max(0, stream.Length - stream.Position), long.MaxValue - 1) + 1 : BufferedInput.UnknownLength;
        return BufferedInput.InitialLength(options, inputLength);
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
            length = Fill(stream, buffer, 0, capacity);
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
    ///     An array rented from <see cref="ArrayPool{T}.Shared"/>, which the caller returns, the number of bytes read
    ///     into it, and the number of bytes the fill asked for (<see cref="Capacity"/>; fewer bytes read means the
    ///     stream ended).
    /// </returns>
    public static ValueTask<(byte[] Buffer, int Length, int Capacity)> RentAsync(Stream stream, ReadOptions? options, CancellationToken cancellationToken)
        => RentAsync(stream, options, ArrayPool<byte>.Shared, cancellationToken);

    /// <summary>Reads the input asynchronously from <paramref name="pool"/>'s arrays; the tests supply a counting pool.</summary>
    /// <param name="stream">The stream to read from its current position.</param>
    /// <param name="options">The read options that bound the byte count, or <see langword="null"/>.</param>
    /// <param name="pool">The pool that supplies the array; the array returns to it if reading fails.</param>
    /// <param name="cancellationToken">The token observed before and during each read.</param>
    /// <returns>
    ///     The rented array, which the caller returns to <paramref name="pool"/>, the number of bytes read into it, and
    ///     the number of bytes the fill asked for.
    /// </returns>
    /// <remarks>
    ///     The fill loop is written out here rather than awaiting <see cref="FillAsync"/>: a stream that completes its
    ///     reads asynchronously then boxes one state machine for the acquisition, not two.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public static async ValueTask<(byte[] Buffer, int Length, int Capacity)> RentAsync(Stream stream, ReadOptions? options, ArrayPool<byte> pool, CancellationToken cancellationToken)
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

            return (buffer, length, capacity);
        }
        catch
        {
            pool.Return(buffer);
            throw;
        }
    }

    /// <summary>Reads from the stream into <paramref name="buffer"/> after its first <paramref name="length"/> bytes until <paramref name="capacity"/> bytes are buffered or the stream ends.</summary>
    /// <param name="stream">The stream, read from its current position.</param>
    /// <param name="buffer">The array that receives the bytes; at least <paramref name="capacity"/> long.</param>
    /// <param name="length">The bytes already buffered.</param>
    /// <param name="capacity">The bytes to buffer in total.</param>
    /// <returns>The bytes buffered afterwards; less than <paramref name="capacity"/> only when the stream ended.</returns>
    public static int Fill(Stream stream, byte[] buffer, int length, int capacity)
    {
        while (length < capacity)
        {
            int read = stream.Read(buffer, length, capacity - length);
            if (read <= 0)
            {
                break;
            }

            length += read;
        }

        return length;
    }

    /// <summary>The awaitable form of <see cref="Fill"/>, with <see cref="Stream.ReadAsync(Memory{byte}, CancellationToken)"/>.</summary>
    /// <param name="stream">The stream, read from its current position.</param>
    /// <param name="buffer">The array that receives the bytes; at least <paramref name="capacity"/> long.</param>
    /// <param name="length">The bytes already buffered.</param>
    /// <param name="capacity">The bytes to buffer in total.</param>
    /// <param name="cancellationToken">The token observed during each read.</param>
    /// <returns>The bytes buffered afterwards; less than <paramref name="capacity"/> only when the stream ended.</returns>
    public static async ValueTask<int> FillAsync(Stream stream, byte[] buffer, int length, int capacity, CancellationToken cancellationToken)
    {
        while (length < capacity)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(length, capacity - length), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
            {
                break;
            }

            length += read;
        }

        return length;
    }

    /// <summary>
    ///     Moves the first <paramref name="length"/> bytes of <paramref name="buffer"/> into a larger array from
    ///     <paramref name="pool"/> and returns the old array to it.
    /// </summary>
    /// <param name="buffer">The current array, rented from <paramref name="pool"/>; the caller no longer uses it afterwards.</param>
    /// <param name="length">The bytes it holds.</param>
    /// <param name="capacity">The length the new array must reach.</param>
    /// <param name="pool">The pool both arrays belong to.</param>
    /// <returns>The new array, holding the same first bytes.</returns>
    public static byte[] Grow(byte[] buffer, int length, int capacity, ArrayPool<byte> pool)
    {
        byte[] larger = pool.Rent(capacity);
        Buffer.BlockCopy(buffer, 0, larger, 0, length);
        pool.Return(buffer);
        return larger;
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
