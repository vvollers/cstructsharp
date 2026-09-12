namespace CStructSharp;

using System;
using System.IO;

/// <summary>Exact binary-scaled 16/32-bit fixed-point codecs.</summary>
internal static class FixedPointCodec
{
    /// <summary>Identifies fixed-point spellings, including their endian variants.</summary>
    public static bool IsType(string name) => name is "fixed16_16" or "fixed16_16<" or "fixed16_16>" or "ufixed16_16" or "ufixed16_16<" or "ufixed16_16>" or "fixed2_30" or "fixed2_30<" or "fixed2_30>" or "ufixed8_8" or "ufixed8_8<" or "ufixed8_8>";

    /// <summary>Reads integer storage and scales it exactly into a double.</summary>
    public static double Read(Stream stream, bool littleEndian, int width, int fraction, bool signed)
    {
        double raw = width == 16 ? BinaryPrimitiveIO.ReadUInt16(stream, littleEndian)
                         : signed ? BinaryPrimitiveIO.ReadInt32(stream, littleEndian)
                         : BinaryPrimitiveIO.ReadUInt32(stream, littleEndian);
        return raw / Math.Pow(2, fraction);
    }

    /// <summary>Rejects non-grid, non-finite and out-of-range values before writing any bytes.</summary>
    public static void Write(Stream stream, object value, bool littleEndian, int width, int fraction, bool signed)
    {
        double scale = Math.Pow(2, fraction);
        double raw;
        if (value is decimal exact)
        {
            // Converting to Double first can erase a small off-grid decimal fraction.
            decimal scaled = checked(exact * (decimal)scale);
            if (scaled != decimal.Truncate(scaled))
            {
                throw new CStructWriteException("Fixed-point value is outside the exact storage grid or range.");
            }

            raw = (double)scaled;
        }
        else
        {
            raw = Convert.ToDouble(value) * scale;
        }

        double minimum = signed ? -Math.Pow(2, width - 1) : 0;
        double maximum = Math.Pow(2, signed ? width - 1 : width) - 1;
        if (!double.IsFinite(raw) || raw != Math.Truncate(raw) || raw < minimum || raw > maximum)
        {
            throw new CStructWriteException("Fixed-point value is outside the exact storage grid or range.");
        }

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
