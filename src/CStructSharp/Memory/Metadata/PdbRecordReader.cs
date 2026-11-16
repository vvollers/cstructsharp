namespace CStructSharp.Memory.Metadata;

using System.Buffers.Binary;
using System.Text;
using CStructSharp.Diagnostics;

/// <summary>Reads CodeView wire fields within one already bounded native PDB record.</summary>
internal ref struct PdbRecordReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ReadOnlySpan<byte> bytes;
    private int position;

    /// <summary>Starts at the first byte of a record payload, excluding its length and leaf code.</summary>
    /// <param name="bytes">The original bounded record payload.</param>
    internal PdbRecordReader(ReadOnlySpan<byte> bytes) => this.bytes = bytes;

    /// <summary>Gets the number of unconsumed original bytes.</summary>
    internal readonly int Remaining => this.bytes.Length - this.position;

    /// <summary>Reads an unsigned one-byte wire value.</summary>
    /// <returns>The original value.</returns>
    internal byte Byte() => this.Take(1)[0];

    /// <summary>Reads a little-endian unsigned two-byte wire value.</summary>
    /// <returns>The original value.</returns>
    internal ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(this.Take(2));

    /// <summary>Reads a little-endian unsigned four-byte wire value.</summary>
    /// <returns>The original value.</returns>
    internal uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(this.Take(4));

    /// <summary>Reads a CodeView integral numeric leaf without narrowing unsigned 64-bit values.</summary>
    /// <returns>The recorded signed or unsigned integer.</returns>
    /// <exception cref="CStructLayoutException">The leaf is truncated or is not a supported integral encoding.</exception>
    internal Int128 Integer()
    {
        ushort leaf = this.UInt16();
        if (leaf < 0x8000)
        {
            return leaf;
        }

        return leaf switch
        {
            0x8000 => unchecked((sbyte)this.Byte()),
            0x8001 => BinaryPrimitives.ReadInt16LittleEndian(this.Take(2)),
            0x8002 => this.UInt16(),
            0x8003 => BinaryPrimitives.ReadInt32LittleEndian(this.Take(4)),
            0x8004 => this.UInt32(),
            0x8009 => BinaryPrimitives.ReadInt64LittleEndian(this.Take(8)),
            0x800A => BinaryPrimitives.ReadUInt64LittleEndian(this.Take(8)),
            _ => throw new CStructLayoutException($"Native PDB numeric leaf 0x{leaf:X4} is not a supported integral encoding."),
        };
    }

    /// <summary>Reads a nonnegative byte or element count that fits the memory-schema extent model.</summary>
    /// <returns>The original bounded count.</returns>
    /// <exception cref="CStructLayoutException">The count is negative, too large or malformed.</exception>
    internal int Size()
    {
        Int128 value = this.Integer();
        if (value < 0 || value > int.MaxValue)
        {
            throw new CStructLayoutException("Native PDB metadata contains an out-of-range storage size or member offset.");
        }

        return (int)value;
    }

    /// <summary>Reads a terminated UTF-8 name without replacing malformed source bytes.</summary>
    /// <returns>The complete recorded name, excluding its terminator.</returns>
    /// <exception cref="CStructLayoutException">The name is unterminated, oversized or malformed UTF-8.</exception>
    internal string Name()
    {
        ReadOnlySpan<byte> rest = this.bytes[this.position..];
        int length = rest.IndexOf((byte)0);
        if (length < 0 || length > 65_535)
        {
            throw new CStructLayoutException("Native PDB metadata has an unterminated or oversized name.");
        }

        ReadOnlySpan<byte> name = this.Take(length + 1)[..length];
        try
        {
            return StrictUtf8.GetString(name);
        }
        catch (DecoderFallbackException exception)
        {
            throw new CStructLayoutException("Native PDB metadata has a malformed UTF-8 name.", exception);
        }
    }

    /// <summary>Consumes one complete CodeView alignment run if it precedes the next field.</summary>
    /// <returns>True when padding was present.</returns>
    /// <exception cref="CStructLayoutException">The declared padding is truncated or inconsistent.</exception>
    internal bool Padding()
    {
        if (this.Remaining == 0 || this.bytes[this.position] < 0xF0)
        {
            return false;
        }

        int count = this.Byte() - 0xF0;
        if (count == 0)
        {
            throw new CStructLayoutException("Native PDB metadata has a zero-length padding run.");
        }

        for (int remaining = count - 1; remaining > 0; remaining--)
        {
            if (this.Byte() != 0xF0 + remaining)
            {
                throw new CStructLayoutException("Native PDB metadata has an inconsistent padding run.");
            }
        }

        return true;
    }

    /// <summary>Accepts at most three uninterpreted symbol-alignment bytes after the terminated name.</summary>
    /// <exception cref="CStructLayoutException">More than one alignment interval remains.</exception>
    internal void EndSymbol()
    {
        if (this.Remaining <= 3)
        {
            this.position = this.bytes.Length;
            return;
        }

        this.End();
    }

    /// <summary>Requires the known record to end after its optional CodeView alignment bytes.</summary>
    /// <exception cref="CStructLayoutException">Unclaimed bytes remain after the known fields.</exception>
    internal void End()
    {
        while (this.Padding())
        {
        }

        if (this.Remaining != 0)
        {
            throw new CStructLayoutException("Native PDB metadata has unclaimed bytes after a known record.");
        }
    }

    /// <summary>Advances only after the entire requested field has been proved inside the record.</summary>
    /// <param name="count">Number of bytes to consume.</param>
    /// <returns>The original field bytes.</returns>
    /// <exception cref="CStructLayoutException">The field is truncated.</exception>
    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > this.Remaining)
        {
            throw new CStructLayoutException("Native PDB metadata contains a truncated CodeView record.");
        }

        ReadOnlySpan<byte> value = this.bytes.Slice(this.position, count);
        this.position += count;
        return value;
    }
}
