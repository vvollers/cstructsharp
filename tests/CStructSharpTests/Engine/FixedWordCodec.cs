namespace CStructSharp.Tests;

using System.Buffers;
using System.Buffers.Binary;
using CStructSharp.Codecs;

/// <summary>
///     A custom codec for the engine tests, spelled <c>word4</c>, that declares a fixed size of four bytes (aligned to
///     four) but may take fewer: a little-endian <see cref="uint"/> in four bytes, except the one-byte short form
///     <c>0xEE</c> for the value 238. A first byte of <c>0xFF</c> is invalid. The layout compiles every later offset from
///     the declared size, while the reader continues where the codec's bytes end, so this codec shows whether the engine
///     places members after a caller's codec as the interpreter does.
/// </summary>
internal sealed class FixedWordCodec : ICustomCodec
{
    /// <summary>The first byte of the one-byte short form, which is also its value.</summary>
    public const byte Short = 0xEE;

    /// <summary>The first byte the codec rejects.</summary>
    public const byte Invalid = 0xFF;

    /// <summary>The shared instance.</summary>
    public static readonly FixedWordCodec Instance = new();

    /// <inheritdoc/>
    public string Name => "word4";

    /// <inheritdoc/>
    public int? FixedSize => 4;

    /// <inheritdoc/>
    public int Alignment => 4;

    /// <summary>Decodes the short form from one byte, or a full word from four.</summary>
    /// <param name="source">The available bytes.</param>
    /// <param name="value">Receives the decoded <see cref="uint"/>.</param>
    /// <param name="bytesConsumed">Receives 1 for the short form, 4 otherwise.</param>
    /// <returns>Whether the value was decoded, needs more bytes, or starts with the invalid byte.</returns>
    public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
    {
        value = null;
        bytesConsumed = 0;
        if (source.Length > 0 && source[0] == Invalid)
        {
            return OperationStatus.InvalidData;
        }

        if (source.Length > 0 && source[0] == Short)
        {
            value = (uint)Short;
            bytesConsumed = 1;
            return OperationStatus.Done;
        }

        if (source.Length < 4)
        {
            return OperationStatus.NeedMoreData;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(source);
        bytesConsumed = 4;
        return OperationStatus.Done;
    }

    /// <summary>Encodes 238 in its short form and every other <see cref="uint"/> whose low byte is not a marker in four bytes.</summary>
    /// <param name="destination">The bytes to fill.</param>
    /// <param name="value">The value.</param>
    /// <param name="bytesWritten">Receives the bytes written.</param>
    /// <returns>Whether the value was written, needs more room, or cannot be encoded.</returns>
    public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
    {
        bytesWritten = 0;
        if (value is not uint word || (word != Short && (byte)word is Short or Invalid))
        {
            return OperationStatus.InvalidData;
        }

        int size = word == Short ? 1 : 4;
        if (destination.Length < size)
        {
            return OperationStatus.DestinationTooSmall;
        }

        if (size == 1)
        {
            destination[0] = Short;
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, word);
        }

        bytesWritten = size;
        return OperationStatus.Done;
    }
}
