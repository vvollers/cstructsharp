namespace CStructSharp.Generated;

using System;
using CStructSharp.Reading;

/// <summary>Preparing a stream or byte sequence as one contiguous buffer, with the runtime's limits and cancellation, before a generated read.</summary>
public ref partial struct ReadCursor
{
    /// <summary>
    ///     Reads a stream into a pooled buffer for the span-based generated reader: up to the options' total read
    ///     budget (or the stream's remaining length when it is known and smaller). The caller returns the array to
    ///     <see cref="System.Buffers.ArrayPool{T}.Shared"/> after parsing. A seekable stream is left where it was; the
    ///     generated <c>Parse</c> moves it past the bytes the value took.
    /// </summary>
    /// <param name="stream">The stream to read from its current position.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="length">The number of bytes read into the buffer.</param>
    /// <returns>The rented buffer.</returns>
    public static byte[] BufferStream(System.IO.Stream stream, ReadOptions? options, out int length)
        => Streams.AsyncStreamBuffer.Rent(stream, options, out length);

    /// <summary>
    ///     The awaitable form of <see cref="BufferStream"/>: the same bytes read with
    ///     <see cref="System.IO.Stream.ReadAsync(Memory{byte}, System.Threading.CancellationToken)"/>; the generated
    ///     <c>ParseAsync</c> forms and the runtime's async operations share it.
    /// </summary>
    /// <param name="stream">The stream to read from its current position.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="cancellationToken">The token that ends the read while it waits for bytes.</param>
    /// <returns>The rented buffer and the number of bytes read into it.</returns>
    public static System.Threading.Tasks.ValueTask<(byte[] Buffer, int Length)> BufferStreamAsync(System.IO.Stream stream, ReadOptions? options, System.Threading.CancellationToken cancellationToken)
        => Streams.AsyncStreamBuffer.RentAsync(stream, options, cancellationToken);

    /// <summary>
    ///     The options an awaitable generated read runs with: <paramref name="options"/> carrying the token that
    ///     ends the operation - the options' own token, <paramref name="cancellationToken"/>, or a source linked from
    ///     both when both can cancel (the caller disposes <paramref name="linked"/> after the operation).
    /// </summary>
    /// <param name="options">The caller's read options, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token given to the async method.</param>
    /// <param name="linked">The linked source when both tokens can cancel; otherwise <see langword="null"/>.</param>
    /// <returns>The options to read with; <see langword="null"/> when neither token can cancel and none were given.</returns>
    public static ReadOptions? WithCancellation(ReadOptions? options, System.Threading.CancellationToken cancellationToken, out System.Threading.CancellationTokenSource? linked)
    {
        System.Threading.CancellationToken token = Streams.AsyncStreamBuffer.Link(options, cancellationToken, out linked);
        return token.CanBeCanceled ? (options ?? new ReadOptions()) with { CancellationToken = token, } : options;
    }

    /// <summary>
    ///     Copies a multi-segment <see cref="System.Buffers.ReadOnlySequence{T}"/> into one pooled array so it can be
    ///     read through the span reader: at most the total read budget plus one byte is copied (the extra byte lets
    ///     the reader report the budget failure instead of a short read, as <see cref="BufferStream"/> does). A
    ///     single-segment sequence needs no copy - callers take its <see cref="System.Buffers.ReadOnlySequence{T}.FirstSpan"/>
    ///     directly. The caller returns the array to <see cref="System.Buffers.ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <param name="source">The sequence to copy.</param>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <param name="length">The number of bytes copied.</param>
    /// <returns>The rented buffer.</returns>
    public static byte[] CopySequence(System.Buffers.ReadOnlySequence<byte> source, ReadOptions? options, out int length)
    {
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(options);
        long limit = Math.Min(settings.MaxTotalBytesRead, int.MaxValue - 1);
        length = (int)Math.Min(source.Length, limit + 1);
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(Math.Max(length, 1));
        try
        {
            System.Buffers.BuffersExtensions.CopyTo(source.Slice(0, length), buffer.AsSpan(0, length));
            return buffer;
        }
        catch
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
            throw;
        }
    }
}
