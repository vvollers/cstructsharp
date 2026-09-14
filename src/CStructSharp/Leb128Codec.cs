namespace CStructSharp;

using System.IO;

/// <summary>Width-bounded LEB128 integers, accepting legal padding and writing canonical encodings.</summary>
internal static class Leb128Codec
{
    /// <summary>Identifies dynamic numeric codecs, separately from terminated strings.</summary>
    public static bool IsType(string name) => name is "uleb128_32" or "uleb128_64" or "sleb128_32" or "sleb128_64";

    /// <summary>Reads one bounded integer and validates unused terminal bits before narrowing.</summary>
    public static ulong Read(Stream stream, int width, bool signed)
    {
        ulong value = 0;
        for (int shift = 0; shift < width; shift += 7)
        {
            byte octet = BinaryPrimitiveIO.ReadByteExactly(stream);
            int payload = octet & 127;
            int remaining = width - shift;
            if (remaining < 7)
            {
                int positiveLimit = 1 << (signed ? remaining - 1 : remaining);
                bool valid = payload < positiveLimit || (signed && payload >= 128 - positiveLimit);
                if (!valid || (octet & 128) != 0)
                {
                    throw new CStructReadException("LEB128 integer exceeds its declared width.");
                }
            }

            value |= (ulong)payload << shift;
            if ((octet & 128) == 0)
            {
                if (signed && (octet & 64) != 0 && shift + 7 < 64)
                {
                    value |= ulong.MaxValue << (shift + 7);
                }

                return value;
            }
        }

        throw new CStructReadException("Unterminated LEB128 integer.");
    }

    /// <summary>Writes a canonical unsigned integer.</summary>
    public static void WriteUnsigned(Stream stream, ulong value)
    {
        do
        {
            byte octet = (byte)(value & 127);
            value >>= 7;
            stream.WriteByte(value == 0 ? octet : (byte)(octet | 128));
        }
        while (value != 0);
    }

    /// <summary>Writes a canonical signed integer with arithmetic sign extension.</summary>
    public static void WriteSigned(Stream stream, long value)
    {
        while (true)
        {
            byte octet = (byte)(value & 127);
            value >>= 7;
            bool done = (value == 0 && (octet & 64) == 0) || (value == -1 && (octet & 64) != 0);
            stream.WriteByte(done ? octet : (byte)(octet | 128));
            if (done)
            {
                return;
            }
        }
    }
}
