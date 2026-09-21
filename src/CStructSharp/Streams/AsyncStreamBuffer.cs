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
    public static byte[] Rent(Stream stream, ReadOptions? options, out int length)
        => Rent(stream, options, ArrayPool<byte>.Shared, out length);

    /// <summary>Reads the input synchronously from <paramref name="pool"/>'s arrays; the tests supply a counting pool.</summary>
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
    public static ValueTask<(byte[] Buffer, int Length)> RentAsync(Stream stream, ReadOptions? options, CancellationToken cancellationToken)
        => RentAsync(stream, options, ArrayPool<byte>.Shared, cancellationToken);

    /// <summary>Reads the input asynchronously from <paramref name="pool"/>'s arrays; the tests supply a counting pool.</summary>
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
    public static CancellationToken Link(ReadOptions? options, CancellationToken cancellationToken, out CancellationTokenSource? linked)
        => Link(options?.CancellationToken ?? default, cancellationToken, out linked);

    /// <inheritdoc cref="Link(ReadOptions?, CancellationToken, out CancellationTokenSource?)"/>
    public static CancellationToken Link(WriteOptions? options, CancellationToken cancellationToken, out CancellationTokenSource? linked)
        => Link(options?.CancellationToken ?? default, cancellationToken, out linked);

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
