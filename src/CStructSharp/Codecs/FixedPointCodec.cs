namespace CStructSharp.Codecs;

using System.IO;
using CStructSharp.Generated;

/// <summary>Stream adapters over the shared fixed-point rule (<see cref="Codec.DecodeFixedPoint"/>, <see cref="Codec.EncodeFixedPoint"/>).</summary>
internal static class FixedPointCodec
{
    /// <summary>Reads one fixed-point value of <paramref name="width"/> bits with <paramref name="fraction"/> fraction bits from the stream.</summary>
    public static double Read(Stream stream, bool littleEndian, int width, int fraction, bool signed)
    {
        long raw = width == 16 ? BinaryPrimitiveIO.ReadUInt16(stream, littleEndian)
                       : signed ? BinaryPrimitiveIO.ReadInt32(stream, littleEndian)
                       : BinaryPrimitiveIO.ReadUInt32(stream, littleEndian);
        return Codec.DecodeFixedPoint(raw, fraction);
    }

    /// <summary>Writes one fixed-point value, rejecting a value that is not exactly on the fixed-point grid.</summary>
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
