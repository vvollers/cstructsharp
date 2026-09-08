namespace CStructSharp;

using System;
using System.IO;

/// <summary>Reads and writes fixed-width unsigned integers in a caller-chosen byte order.</summary>
internal static class BinaryPrimitiveIO
{
    /// <summary>Reads exactly a fixed number of bytes and puts them in the requested byte order.</summary>
    public static Span<byte> ReadIntoBuffer(Stream stream, int len, bool isLittleEndian)
    {
        // Allocate exactly the primitive width requested by the caller.
        byte[] buffer = new byte[len];
        try
        {
            // ReadExactly handles throttled and network-like streams that return fewer bytes per Read call.
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            throw new CStructReadException("Not enough bytes in stream.", exception);
        }

        // BitConverter follows the current machine. Reverse only when the binary format differs.
        if (isLittleEndian != BitConverter.IsLittleEndian)
        {
            Array.Reverse(buffer);
        }

        return buffer.AsSpan();
    }

    /// <summary>Reads one required byte and turns an unexpected end of stream into a layout-specific error.</summary>
    public static byte ReadByteExactly(Stream stream)
    {
        int value = stream.ReadByte();
        if (value < 0)
        {
            throw new CStructReadException("Not enough bytes in stream.");
        }

        return (byte)value;
    }

    /// <summary>Converts one, two, four, or eight bytes into an unsigned number in the layout's byte order.</summary>
    public static ulong ReadUnsigned(byte[] buffer, bool littleEndian)
    {
        // Do not reorder the caller's buffer: bitfield and pointer code may still need the original byte sequence.
        byte[] local = (byte[])buffer.Clone();
        if (littleEndian != BitConverter.IsLittleEndian)
        {
            Array.Reverse(local);
        }

        // BitConverter only needs to handle the four widths supported by CStruct pointer and primitive codecs.
        return local.Length switch
        {
            1 => local[0],
            2 => BitConverter.ToUInt16(local, 0),
            4 => BitConverter.ToUInt32(local, 0),
            8 => BitConverter.ToUInt64(local, 0),
            _ => throw new InvalidOperationException("Unsupported integer size: " + local.Length),
        };
    }

    /// <summary>Writes bytes in the layout's requested byte order.</summary>
    public static void WriteEndianBytes(Stream stream, byte[] bytes, bool littleEndian)
    {
        if (littleEndian != BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Converts an unsigned value to one, two, four, or eight bytes in the layout's byte order.</summary>
    public static byte[] WriteUnsigned(ulong value, int byteSize, bool littleEndian)
    {
        // Start with the platform conversion for the requested width.
        byte[] bytes = byteSize switch
        {
            1 => new[] { (byte)value, },
            2 => BitConverter.GetBytes((ushort)value),
            4 => BitConverter.GetBytes((uint)value),
            8 => BitConverter.GetBytes(value),
            _ => throw new InvalidOperationException("Unsupported integer size: " + byteSize),
        };

        if (littleEndian != BitConverter.IsLittleEndian)
        {
            // Reverse only when the layout's byte order differs from the machine's order.
            Array.Reverse(bytes);
        }

        return bytes;
    }
}
