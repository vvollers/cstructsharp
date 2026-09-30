namespace CStructSharp.Codecs;

using System.IO;
using CStructSharp.Generated;

/// <summary>The stream writer over the shared fixed-point rule (<see cref="Codec.EncodeFixedPoint"/>).</summary>
internal static class FixedPointCodec
{
    /// <summary>Writes one fixed-point value, rejecting a value that is not exactly on the fixed-point grid.</summary>
    /// <param name="stream">The stream to write to; it advances by <paramref name="width"/> / 8 bytes.</param>
    /// <param name="value">
    ///     The number to encode; it must be exactly representable with the given fraction bits and range.
    /// </param>
    /// <param name="littleEndian">Whether to store the bytes little-endian.</param>
    /// <param name="width">The storage width in bits: 16 or 32.</param>
    /// <param name="fraction">The number of low bits that hold the fractional part.</param>
    /// <param name="signed">
    ///     Whether the stored integer is two's-complement signed, which sets the accepted range.
    /// </param>
    public static void Write(Stream stream, object value, bool littleEndian, int width, int fraction, bool signed)
    {
        long raw = Codec.EncodeFixedPoint(value, width, fraction, signed);
        if (width == 16)
        {
            BinaryPrimitiveIO.WriteUInt16(stream, (ushort)raw, littleEndian);
        }
        else if (signed)
        {
            BinaryPrimitiveIO.WriteInt32(stream, (int)raw, littleEndian);
        }
        else
        {
            BinaryPrimitiveIO.WriteUInt32(stream, (uint)raw, littleEndian);
        }
    }
}
