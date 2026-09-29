namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;

/// <summary>
///     A one-byte custom codec for the engine tests, spelled <c>odd</c>, whose edge cases the adapter and the readers must
///     report identically: byte 0 decodes successfully to no value at all, byte 1 makes the codec throw, byte 2 reports
///     more bytes consumed than it was given, and every other byte decodes to itself (a <see cref="byte"/>).
/// </summary>
internal sealed class QuirkyCodec : ICustomCodec
{
    /// <summary>The shared instance.</summary>
    public static readonly QuirkyCodec Instance = new();

    /// <inheritdoc/>
    public string Name => "odd";

    /// <inheritdoc/>
    public int? FixedSize => null;

    /// <inheritdoc/>
    public int Alignment => 1;

    /// <summary>Decodes one byte, with the quirks the class describes.</summary>
    /// <param name="source">The available bytes.</param>
    /// <param name="value">Receives the byte, or <see langword="null"/> for byte 0.</param>
    /// <param name="bytesConsumed">Receives 1, or an impossible count for byte 2.</param>
    /// <returns>Whether the value was decoded or needs a byte.</returns>
    /// <exception cref="InvalidOperationException">The byte is 1.</exception>
    public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
    {
        value = null;
        bytesConsumed = 0;
        if (source.IsEmpty)
        {
            return OperationStatus.NeedMoreData;
        }

        switch (source[0])
        {
        case 0:
            bytesConsumed = 1;
            return OperationStatus.Done;
        case 1:
            throw new InvalidOperationException("odd codec refused byte 1");
        case 2:
            bytesConsumed = source.Length + 1;
            return OperationStatus.Done;
        default:
            value = source[0];
            bytesConsumed = 1;
            return OperationStatus.Done;
        }
    }

    /// <summary>Encodes a <see cref="byte"/> as itself.</summary>
    /// <param name="destination">The bytes to fill.</param>
    /// <param name="value">The byte.</param>
    /// <param name="bytesWritten">Receives 1 when written.</param>
    /// <returns>Whether the value was written, needs room, or is not a byte.</returns>
    public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
    {
        bytesWritten = 0;
        if (value is not byte single)
        {
            return OperationStatus.InvalidData;
        }

        if (destination.IsEmpty)
        {
            return OperationStatus.DestinationTooSmall;
        }

        destination[0] = single;
        bytesWritten = 1;
        return OperationStatus.Done;
    }
}
