namespace CStructSharp.Tests;

using System.Buffers;
using System.Buffers.Binary;
using CStructSharp.Codecs;

/// <summary>
///     A variable-length custom codec for the engine tests, spelled <c>blob</c>: a little-endian 16-bit length, then that
///     many bytes, decoded as the length (an <see cref="int"/>). A long value needs more than the stream adapter's first
///     256-byte window, so it shows the window doubling up to <see cref="ReadOptions.MaxStringBytes"/>.
/// </summary>
internal sealed class LengthPrefixedCodec : ICustomCodec
{
    /// <summary>The shared instance.</summary>
    public static readonly LengthPrefixedCodec Instance = new();

    /// <inheritdoc/>
    public string Name => "blob";

    /// <inheritdoc/>
    public int? FixedSize => null;

    /// <inheritdoc/>
    public int Alignment => 1;

    /// <summary>Decodes the length once the prefix and every byte it announces are available.</summary>
    /// <param name="source">The available bytes.</param>
    /// <param name="value">Receives the announced length.</param>
    /// <param name="bytesConsumed">Receives the prefix and the bytes it announces.</param>
    /// <returns>Whether the value was decoded or needs more bytes.</returns>
    public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
    {
        value = null;
        bytesConsumed = 0;
        if (source.Length < 2 || source.Length < 2 + BinaryPrimitives.ReadUInt16LittleEndian(source))
        {
            return OperationStatus.NeedMoreData;
        }

        int length = BinaryPrimitives.ReadUInt16LittleEndian(source);
        value = length;
        bytesConsumed = 2 + length;
        return OperationStatus.Done;
    }

    /// <summary>Encodes a length as its prefix followed by that many zero bytes.</summary>
    /// <param name="destination">The bytes to fill.</param>
    /// <param name="value">The length, an <see cref="int"/> up to 65535.</param>
    /// <param name="bytesWritten">Receives the bytes written.</param>
    /// <returns>Whether the value was written, needs more room, or is not a valid length.</returns>
    public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
    {
        bytesWritten = 0;
        if (value is not int length || length is < 0 or > ushort.MaxValue)
        {
            return OperationStatus.InvalidData;
        }

        if (destination.Length < 2 + length)
        {
            return OperationStatus.DestinationTooSmall;
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)length);
        destination.Slice(2, length).Clear();
        bytesWritten = 2 + length;
        return OperationStatus.Done;
    }
}
