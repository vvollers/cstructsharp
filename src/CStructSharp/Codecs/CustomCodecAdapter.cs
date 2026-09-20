namespace CStructSharp.Codecs;

using System;
using System.Buffers;
using System.IO;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>
///     Runs a span-based <see cref="ICustomCodec"/> on the operation streams: memory-backed input hands the codec
///     the remaining bytes directly; a stream source or destination goes through a scratch window that grows while
///     the codec asks for more room, up to the operation's per-value byte limit.
/// </summary>
internal static class CustomCodecAdapter
{
    private const int InitialWindow = 256;

    /// <summary>
    ///     Decodes one value from memory-backed input (the whole remaining input is the codec's window), as the
    ///     runtime's memory path and the generated code's <c>ReadCursor.TakeCustom</c> both do: the value and the
    ///     bytes it took, or the failure to raise after the caller moved past the bytes the codec looked at.
    /// </summary>
    /// <param name="codec">The codec.</param>
    /// <param name="remaining">The bytes from the value's start to the end of the input.</param>
    /// <param name="value">The decoded value when there is no failure.</param>
    /// <param name="consumed">The bytes to advance by: the value's length, or the whole window on a short read.</param>
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

            return null;
        case OperationStatus.NeedMoreData:
            consumed = remaining.Length;
            return new CStructReadException(ReadFailures.CustomCodecShortRead(codec.Name, remaining.Length));
        default:
            consumed = 0;
            return new CStructReadException(ReadFailures.CustomCodecRejected(codec.Name));
        }
    }

    /// <summary>Reads one value at the stream position and leaves the stream after it.</summary>
    public static object Read(ICustomCodec codec, Stream stream)
    {
        var budget = stream as ReadBudgetStream;
        if (budget is not null && budget.TryPeekRemaining(out ReadOnlySpan<byte> remaining))
        {
            CStructReadException? failure = DecodeFromMemory(codec, remaining, out object? value, out int consumed);
            budget.Advance(consumed);
            if (failure is not null)
            {
                throw failure;
            }

            return value!;
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

                    stream.Position = start + consumed;
                    return value!;
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

    /// <summary>Writes one value at the stream position and leaves the stream after it.</summary>
    public static void Write(ICustomCodec codec, Stream stream, object value)
    {
        long limit = (stream as WriteBudgetStream)?.MaxStringBytes ?? int.MaxValue;
        int window = codec.FixedSize ?? InitialWindow;
        byte[] rented = ArrayPool<byte>.Shared.Rent(window);
        try
        {
            while (true)
            {
                OperationStatus status = Encode(codec, rented.AsSpan(0, window), value, out int written);
                switch (status)
                {
                case OperationStatus.Done:
                    if (written < 0 || written > window)
                    {
                        throw new CStructWriteException($"Custom codec '{codec.Name}' reported {written} bytes written into a {window}-byte window.");
                    }

                    stream.Write(rented, 0, written);
                    return;
                case OperationStatus.DestinationTooSmall when window >= limit:
                    throw new CStructWriteLimitException($"Custom codec '{codec.Name}' needs more than MaxStringBytes ({limit}) for one value.");
                case OperationStatus.DestinationTooSmall:
                    window = (int)Math.Min((long)window * 2, limit);
                    ArrayPool<byte>.Shared.Return(rented);
                    rented = ArrayPool<byte>.Shared.Rent(window);
                    continue;
                default:
                    throw new CStructWriteException($"Custom codec '{codec.Name}' cannot encode the value {value ?? "null"}.");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

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

    private static OperationStatus Encode(ICustomCodec codec, Span<byte> destination, object value, out int written)
    {
        try
        {
            return codec.Write(destination, value, out written);
        }
        catch (Exception exception) when (exception is not CStructException)
        {
            throw new CStructWriteException($"Custom codec '{codec.Name}' failed to encode {value ?? "null"}: {exception.Message}", exception);
        }
    }

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
