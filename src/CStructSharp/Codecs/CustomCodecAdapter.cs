namespace CStructSharp.Codecs;

using System;
using System.Buffers;
using System.IO;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Streams;

/// <summary>
///     Runs a span-based <see cref="ICustomCodec"/> on the operation streams: memory-backed input hands the codec
///     the remaining bytes directly; a stream source or destination goes through a scratch window that grows while
///     the codec asks for more room, up to the operation's per-value byte limit.
/// </summary>
/// <remarks>
///     A codec that declares a <see cref="ICustomCodec.FixedSize"/> keeps that promise on every path: a value it decodes
///     occupies exactly that many bytes (the read continues after them even when the codec took fewer, and an input that
///     ends before them is a short read), a value it encodes is padded with zero bytes to that size, and a codec that
///     consumes or needs more than that size fails.
/// </remarks>
internal static class CustomCodecAdapter
{
    private const int InitialWindow = 256;

    /// <summary>
    ///     Reports whether a codec's answer over a window that ends where a partly buffered input's buffer ends
    ///     (<see cref="Streams.BufferedInput"/>) may change once more of the input is buffered. The codec's contract
    ///     promises it the whole remaining input, so any answer that could rest on the window's end - a value that took
    ///     the whole window, a short read, a rejection - is decided over more input. A fixed-size codec whose window holds
    ///     its declared size, and a value that ended before the window's end, are already decided.
    /// </summary>
    /// <param name="codec">The codec.</param>
    /// <param name="failure">The failure <see cref="DecodeFromMemory"/> returned, or <see langword="null"/>.</param>
    /// <param name="consumed">The bytes <see cref="DecodeFromMemory"/> reported.</param>
    /// <param name="windowLength">The window's length in bytes.</param>
    /// <returns>Whether the caller must require bytes past the window before using the answer.</returns>
    public static bool MayDependOnWindowEnd(ICustomCodec codec, CStructReadException? failure, int consumed, int windowLength)
        => (codec.FixedSize is not int size || size > windowLength) && (consumed == windowLength || failure is not null);

    /// <summary>
    ///     Decodes one value from memory-backed input (the whole remaining input is the codec's window), as the
    ///     runtime's memory path and the generated code's <c>ReadCursor.TakeCustom</c> both do: the value and the
    ///     bytes it took, or the failure to raise after the caller moved past the bytes the codec looked at.
    /// </summary>
    /// <param name="codec">The codec.</param>
    /// <param name="remaining">The bytes from the value's start to the end of the input.</param>
    /// <param name="value">The decoded value when there is no failure.</param>
    /// <param name="consumed">
    ///     The bytes to advance by: the value's length (a fixed-size codec's declared size), or the whole window on a short
    ///     read.
    /// </param>
    /// <returns>The failure to throw, or <see langword="null"/>.</returns>
    public static CStructReadException? DecodeFromMemory(ICustomCodec codec, ReadOnlySpan<byte> remaining, out object? value, out int consumed)
    {
        OperationStatus status = Decode(codec, remaining, out value, out consumed);
        switch (status)
        {
        case OperationStatus.Done:
            if (consumed < 0 || consumed > remaining.Length)
            {
                int reported = consumed;
                consumed = 0;
                return new CStructReadException(ReadFailures.CustomCodecConsumed(codec.Name, reported, remaining.Length));
            }

            return codec.FixedSize is int size ? ToFixedSize(codec, size, remaining.Length, ref consumed) : null;
        case OperationStatus.NeedMoreData:
            consumed = remaining.Length;
            return new CStructReadException(ReadFailures.CustomCodecShortRead(codec.Name, remaining.Length));
        default:
            consumed = 0;
            return new CStructReadException(ReadFailures.CustomCodecRejected(codec.Name));
        }
    }

    /// <summary>Reads one value at the stream position and leaves the stream after it.</summary>
    /// <param name="codec">The custom codec that decodes the value.</param>
    /// <param name="stream">
    ///     The source, at the value's first byte; a memory-backed <see cref="ReadBudgetStream"/> is decoded in place,
    ///     and any other stream is read through a window of at most the operation's <c>MaxStringBytes</c>.
    /// </param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="CStructReadException">
    ///     The input ends before the value, the codec rejects the bytes or throws, or it reports an impossible length.
    /// </exception>
    /// <exception cref="CStructReadLimitException">The codec needs a window larger than the byte limit.</exception>
    public static object Read(ICustomCodec codec, Stream stream)
    {
        var budget = stream as ReadBudgetStream;
        if (budget is not null && budget.TryPeekRemaining(out ReadOnlySpan<byte> remaining))
        {
            var cursor = new StreamReadCursor(budget);
            return ReadInMemory(codec, ref cursor, remaining);
        }

        // A stream source: read a window at the value's position, widen it while the codec needs more, then leave
        // the stream exactly after the value.
        long start = stream.Position;
        long limit = budget?.MaxStringBytes ?? int.MaxValue;
        int window = codec.FixedSize ?? InitialWindow;
        byte[] rented = ArrayPool<byte>.Shared.Rent(window);
        try
        {
            while (true)
            {
                stream.Position = start;
                int read = ReadUpTo(stream, rented.AsSpan(0, window));
                OperationStatus status = Decode(codec, rented.AsSpan(0, read), out object? value, out int consumed);
                switch (status)
                {
                case OperationStatus.Done:
                    if (consumed < 0 || consumed > read)
                    {
                        throw new CStructReadException(ReadFailures.CustomCodecConsumed(codec.Name, consumed, read));
                    }

                    // A fixed-size value ends at its declared size, whatever the codec took; the window held every byte
                    // of the input up to that size, so the failures and positions are the memory path's.
                    CStructReadException? failure = codec.FixedSize is int size ? ToFixedSize(codec, size, read, ref consumed) : null;
                    stream.Position = start + consumed;
                    return failure is null ? value! : throw failure;
                case OperationStatus.NeedMoreData when read < window:
                    throw new CStructReadException(ReadFailures.CustomCodecShortRead(codec.Name, read));
                case OperationStatus.NeedMoreData when window >= limit:
                    throw new CStructReadLimitException(ReadFailures.CustomCodecLimit(codec.Name, limit));
                case OperationStatus.NeedMoreData:
                    window = (int)Math.Min((long)window * 2, limit);
                    ArrayPool<byte>.Shared.Return(rented);
                    rented = ArrayPool<byte>.Shared.Rent(window);
                    continue;
                default:
                    throw new CStructReadException(ReadFailures.CustomCodecRejected(codec.Name));
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    ///     Decodes one value from memory input whose remaining bytes a cursor exposed (<see cref="IReadCursor.TryPeekRemaining"/>):
    ///     the codec sees every remaining byte, and the cursor advances past - and is charged for - the bytes it took,
    ///     the whole window when it needs more data, before any failure is raised. The memory branch of
    ///     <see cref="Read"/> and the engine's memory cursor both run this.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="codec">The codec.</param>
    /// <param name="cursor">The cursor at the value's first byte.</param>
    /// <param name="remaining">The bytes from the value's start to the end of the input, as the cursor exposed them.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="CStructReadException">The input ends before the value, the codec rejects the bytes or throws, or it reports an impossible length.</exception>
    /// <exception cref="CStructReadLimitException">The advance exceeds the total read budget.</exception>
    public static object ReadInMemory<TCursor>(ICustomCodec codec, ref TCursor cursor, ReadOnlySpan<byte> remaining)
        where TCursor : struct, IReadCursor
    {
        CStructReadException? failure = DecodeFromMemory(codec, remaining, out object? value, out int consumed);
        cursor.Advance(consumed);
        if (failure is not null)
        {
            throw failure;
        }

        return value!;
    }

    /// <summary>Writes one value at the stream position and leaves the stream after it.</summary>
    /// <param name="codec">The custom codec that encodes the value.</param>
    /// <param name="stream">
    ///     The destination, at the value's first byte; a stream that enforces the write budget (<see cref="IWriteBudget"/>) supplies the byte limit.
    /// </param>
    /// <param name="value">The caller value to encode.</param>
    /// <exception cref="CStructWriteException">The codec rejects the value or reports an impossible length.</exception>
    /// <exception cref="CStructWriteLimitException">The encoding needs more bytes than the limit allows.</exception>
    public static void Write(ICustomCodec codec, Stream stream, object value)
    {
        long limit = (stream as IWriteBudget)?.MaxStringBytes ?? int.MaxValue;
        byte[] rented = EncodeToRented(codec, value, limit, out int written);
        try
        {
            stream.Write(rented, 0, written);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    /// <summary>
    ///     Encodes one value into a pooled window that grows while the codec asks for more room, up to
    ///     <paramref name="limit"/> (the runtime's rule for every destination); the caller copies
    ///     <paramref name="written"/> bytes out and returns the array to <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    /// <param name="codec">The codec.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="limit">The largest window (the operation's <c>MaxStringBytes</c>).</param>
    /// <param name="written">The encoded length.</param>
    /// <returns>The rented array holding the encoded bytes.</returns>
    public static byte[] EncodeToRented(ICustomCodec codec, object value, long limit, out int written)
    {
        int window = codec.FixedSize ?? InitialWindow;
        byte[] rented = ArrayPool<byte>.Shared.Rent(window);
        try
        {
            while (true)
            {
                OperationStatus status = Encode(codec, rented.AsSpan(0, window), value, out written);
                switch (status)
                {
                case OperationStatus.Done:
                    if (written < 0 || written > window)
                    {
                        throw new CStructWriteException(WriteFailures.CustomCodecWritten(codec.Name, written, window));
                    }

                    if (codec.FixedSize is int size && written < size)
                    {
                        // A fixed-size value occupies its declared size: the bytes the codec left are zero.
                        rented.AsSpan(written, size - written).Clear();
                        written = size;
                    }

                    byte[] result = rented;
                    rented = null!;
                    return result;
                case OperationStatus.DestinationTooSmall when codec.FixedSize is int fixedSize:
                    throw new CStructWriteException(WriteFailures.CustomCodecOversized(codec.Name, fixedSize));
                case OperationStatus.DestinationTooSmall when window >= limit:
                    throw new CStructWriteLimitException(WriteFailures.CustomCodecLimit(codec.Name, limit));
                case OperationStatus.DestinationTooSmall:
                    window = (int)Math.Min((long)window * 2, limit);
                    ArrayPool<byte>.Shared.Return(rented);
                    rented = ArrayPool<byte>.Shared.Rent(window);
                    continue;
                default:
                    throw new CStructWriteException(WriteFailures.CustomCodecCannotEncode(codec.Name, value));
                }
            }
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    /// <summary>
    ///     Makes a fixed-size value occupy its declared size after the codec decoded it: a codec that took more than the size
    ///     fails without moving; one whose declared size runs past the input is a short read that moves to the input's end;
    ///     otherwise the read moves past the whole declared size.
    /// </summary>
    /// <param name="codec">The codec.</param>
    /// <param name="size">The codec's declared fixed size in bytes.</param>
    /// <param name="available">The bytes of the input from the value's start (the window a stream read filled).</param>
    /// <param name="consumed">The bytes the codec reported; replaced by the bytes to advance by.</param>
    /// <returns>The failure to throw, or <see langword="null"/>.</returns>
    private static CStructReadException? ToFixedSize(ICustomCodec codec, int size, int available, ref int consumed)
    {
        if (consumed > size)
        {
            int reported = consumed;
            consumed = 0;
            return new CStructReadException(ReadFailures.CustomCodecOversized(codec.Name, reported, size));
        }

        if (size > available)
        {
            consumed = available;
            return new CStructReadException(ReadFailures.CustomCodecShortRead(codec.Name, available));
        }

        consumed = size;
        return null;
    }

    /// <summary>Runs the codec's <see cref="ICustomCodec.Read"/>, turning an exception it throws into the read failure that names the codec.</summary>
    /// <param name="codec">The codec.</param>
    /// <param name="source">The bytes from the value's start.</param>
    /// <param name="value">The decoded value when the status is <see cref="OperationStatus.Done"/>.</param>
    /// <param name="consumed">The byte count the codec reported.</param>
    /// <returns>The codec's status.</returns>
    /// <exception cref="CStructReadException">The codec threw.</exception>
    private static OperationStatus Decode(ICustomCodec codec, ReadOnlySpan<byte> source, out object? value, out int consumed)
    {
        try
        {
            return codec.Read(source, out value, out consumed);
        }
        catch (Exception exception) when (exception is not CStructException)
        {
            throw new CStructReadException(ReadFailures.CustomCodecFailed(codec.Name, exception.Message), exception);
        }
    }

    /// <summary>Runs the codec's <see cref="ICustomCodec.Write"/>, turning an exception it throws into the write failure that names the codec.</summary>
    /// <param name="codec">The codec.</param>
    /// <param name="destination">The window to encode into.</param>
    /// <param name="value">The value.</param>
    /// <param name="written">The byte count the codec reported.</param>
    /// <returns>The codec's status.</returns>
    /// <exception cref="CStructWriteException">The codec threw.</exception>
    private static OperationStatus Encode(ICustomCodec codec, Span<byte> destination, object value, out int written)
    {
        try
        {
            return codec.Write(destination, value, out written);
        }
        catch (Exception exception) when (exception is not CStructException)
        {
            throw new CStructWriteException(WriteFailures.CustomCodecFailed(codec.Name, value, exception.Message), exception);
        }
    }

    /// <summary>Fills <paramref name="destination"/> from the stream until it is full or the stream ends.</summary>
    /// <param name="stream">The source.</param>
    /// <param name="destination">The window to fill.</param>
    /// <returns>The number of bytes read.</returns>
    private static int ReadUpTo(Stream stream, Span<byte> destination)
    {
        int total = 0;
        while (total < destination.Length)
        {
            int read = stream.Read(destination[total..]);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }
}
