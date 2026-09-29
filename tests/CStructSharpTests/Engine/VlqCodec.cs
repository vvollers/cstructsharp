namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;

/// <summary>
///     A variable-length custom codec for the engine tests, spelled <c>vlq</c>: an unsigned integer in seven-bit groups,
///     least significant first, each byte but the last with its high bit set (unsigned LEB128), at most five bytes.
/// </summary>
internal sealed class VlqCodec : ICustomCodec
{
    /// <summary>The shared instance.</summary>
    public static readonly VlqCodec Instance = new();

    /// <inheritdoc/>
    public string Name => "vlq";

    /// <inheritdoc/>
    public int? FixedSize => null;

    /// <inheritdoc/>
    public int Alignment => 1;

    /// <summary>Decodes a value of at most five bytes as a <see cref="uint"/>.</summary>
    /// <param name="source">The available bytes.</param>
    /// <param name="value">Receives the decoded value.</param>
    /// <param name="bytesConsumed">Receives the bytes the value used.</param>
    /// <returns>Whether the value was decoded, needs more bytes, or is longer than five bytes.</returns>
    public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
    {
        uint result = 0;
        for (int index = 0; index < source.Length && index < 5; index++)
        {
            result |= (uint)(source[index] & 0x7F) << (7 * index);
            if ((source[index] & 0x80) == 0)
            {
                value = result;
                bytesConsumed = index + 1;
                return OperationStatus.Done;
            }
        }

        value = null;
        bytesConsumed = 0;
        return source.Length >= 5 ? OperationStatus.InvalidData : OperationStatus.NeedMoreData;
    }

    /// <summary>Encodes an unsigned integer that fits <see cref="uint"/>.</summary>
    /// <param name="destination">The bytes to fill.</param>
    /// <param name="value">The value.</param>
    /// <param name="bytesWritten">Receives the bytes written.</param>
    /// <returns>Whether the value was written, needs more room, or is not a <see cref="uint"/>-sized unsigned value.</returns>
    public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
    {
        bytesWritten = 0;
        ulong remaining;
        try
        {
            remaining = Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception exception) when (exception is InvalidCastException or OverflowException or FormatException)
        {
            return OperationStatus.InvalidData;
        }

        if (remaining > uint.MaxValue)
        {
            return OperationStatus.InvalidData;
        }

        do
        {
            if (bytesWritten == destination.Length)
            {
                bytesWritten = 0;
                return OperationStatus.DestinationTooSmall;
            }

            byte group = (byte)(remaining & 0x7F);
            remaining >>= 7;
            destination[bytesWritten++] = remaining == 0 ? group : (byte)(group | 0x80);
        }
        while (remaining != 0);

        return OperationStatus.Done;
    }
}
