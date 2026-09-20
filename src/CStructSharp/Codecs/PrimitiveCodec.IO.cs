namespace CStructSharp.Codecs;

using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using CStructSharp.Diagnostics;

/// <summary>The runtime half of a primitive descriptor: reading and writing one numeric value through a span.</summary>
internal readonly partial record struct PrimitiveCodec
{
    /// <summary>
    ///     Encodes one caller-supplied value into exactly this codec's bytes, applying the same <see cref="Convert"/>
    ///     conversion (and the same range failures) as the stream write handler for the same primitive name, so a
    ///     static write plan produces the bytes and the errors the general writer produces.
    /// </summary>
    public void WriteNumeric(Span<byte> bytes, object value)
    {
        bool le = this.LittleEndian;
        switch (this.Kind)
        {
        case PrimitiveCodecKind.UInt8:
            bytes[0] = value is byte b ? b : Convert.ToByte(value);
            break;
        case PrimitiveCodecKind.Int8:
            bytes[0] = unchecked((byte)(value is sbyte sb ? sb : Convert.ToSByte(value)));
            break;
        case PrimitiveCodecKind.Bool:
            bytes[0] = (byte)((value is bool flag ? flag : Convert.ToBoolean(value)) ? 1 : 0);
            break;
        case PrimitiveCodecKind.Int16:
            {
                short typed = value is short s ? s : Convert.ToInt16(value);
                if (le)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(bytes, typed);
                }
                else
                {
                    BinaryPrimitives.WriteInt16BigEndian(bytes, typed);
                }

                break;
            }

        case PrimitiveCodecKind.UInt16:
            {
                ushort typed = value is ushort us ? us : Convert.ToUInt16(value);
                if (le)
                {
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes, typed);
                }
                else
                {
                    BinaryPrimitives.WriteUInt16BigEndian(bytes, typed);
                }

                break;
            }

        case PrimitiveCodecKind.Int24:
            {
                int typed = value is int i ? i : Convert.ToInt32(value);
                if (typed is < -8388608 or > 8388607)
                {
                    throw new CStructWriteException("Value is outside the int24 range.");
                }

                WriteUInt24(bytes, unchecked((uint)typed) & 0xffffff, le);
                break;
            }

        case PrimitiveCodecKind.UInt24:
            {
                uint typed = value is uint u ? u : Convert.ToUInt32(value);
                if (typed > 0xffffff)
                {
                    throw new CStructWriteException("Value is outside the uint24 range.");
                }

                WriteUInt24(bytes, typed, le);
                break;
            }

        case PrimitiveCodecKind.Int32:
            {
                int typed = value is int i ? i : Convert.ToInt32(value);
                if (le)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(bytes, typed);
                }
                else
                {
                    BinaryPrimitives.WriteInt32BigEndian(bytes, typed);
                }

                break;
            }

        case PrimitiveCodecKind.UInt32:
            {
                uint typed = value is uint u ? u : Convert.ToUInt32(value);
                if (le)
                {
                    BinaryPrimitives.WriteUInt32LittleEndian(bytes, typed);
                }
                else
                {
                    BinaryPrimitives.WriteUInt32BigEndian(bytes, typed);
                }

                break;
            }

        case PrimitiveCodecKind.Int64:
            {
                long typed = value is long l ? l : Convert.ToInt64(value);
                if (le)
                {
                    BinaryPrimitives.WriteInt64LittleEndian(bytes, typed);
                }
                else
                {
                    BinaryPrimitives.WriteInt64BigEndian(bytes, typed);
                }

                break;
            }

        case PrimitiveCodecKind.UInt64:
            {
                ulong typed = value is ulong ul ? ul : Convert.ToUInt64(value);
                if (le)
                {
                    BinaryPrimitives.WriteUInt64LittleEndian(bytes, typed);
                }
                else
                {
                    BinaryPrimitives.WriteUInt64BigEndian(bytes, typed);
                }

                break;
            }

        case PrimitiveCodecKind.Float32:
            {
                float typed = value is float f ? f : Convert.ToSingle(value);
                if (le)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(bytes, typed);
                }
                else
                {
                    BinaryPrimitives.WriteSingleBigEndian(bytes, typed);
                }

                break;
            }

        case PrimitiveCodecKind.Float64:
            {
                double typed = value is double d ? d : Convert.ToDouble(value);
                if (le)
                {
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes, typed);
                }
                else
                {
                    BinaryPrimitives.WriteDoubleBigEndian(bytes, typed);
                }

                break;
            }

        default:
            throw new InvalidOperationException("Codec is not a fixed-width numeric: " + this.Kind);
        }
    }

    /// <summary>Decodes one fixed-width numeric element from exactly its bytes into the same boxed CLR type the stream codec produces.</summary>
    public object ReadNumeric(ReadOnlySpan<byte> bytes)
    {
        bool le = this.LittleEndian;
        return this.Kind switch
        {
            PrimitiveCodecKind.UInt8 => bytes[0],
            PrimitiveCodecKind.Int8 => unchecked((sbyte)bytes[0]),
            PrimitiveCodecKind.Bool => bytes[0] != 0,
            PrimitiveCodecKind.Int16 => le ? BinaryPrimitives.ReadInt16LittleEndian(bytes) : BinaryPrimitives.ReadInt16BigEndian(bytes),
            PrimitiveCodecKind.UInt16 => le ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes),
            PrimitiveCodecKind.Int24 => le
                ? unchecked((int)((uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16)) << 8)) >> 8
                : unchecked((int)((uint)(bytes[2] | (bytes[1] << 8) | (bytes[0] << 16)) << 8)) >> 8,
            PrimitiveCodecKind.UInt24 => le
                ? (uint)(bytes[0] | (bytes[1] << 8) | (bytes[2] << 16))
                : (uint)(bytes[2] | (bytes[1] << 8) | (bytes[0] << 16)),
            PrimitiveCodecKind.Int32 => le ? BinaryPrimitives.ReadInt32LittleEndian(bytes) : BinaryPrimitives.ReadInt32BigEndian(bytes),
            PrimitiveCodecKind.UInt32 => le ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes),
            PrimitiveCodecKind.Int64 => le ? BinaryPrimitives.ReadInt64LittleEndian(bytes) : BinaryPrimitives.ReadInt64BigEndian(bytes),
            PrimitiveCodecKind.UInt64 => le ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes),
            PrimitiveCodecKind.Float32 => le ? BinaryPrimitives.ReadSingleLittleEndian(bytes) : BinaryPrimitives.ReadSingleBigEndian(bytes),
            PrimitiveCodecKind.Float64 => le ? BinaryPrimitives.ReadDoubleLittleEndian(bytes) : BinaryPrimitives.ReadDoubleBigEndian(bytes),
            _ => throw new InvalidOperationException("Codec is not a fixed-width numeric: " + this.Kind),
        };
    }

    private static void WriteUInt24(Span<byte> bytes, uint value, bool littleEndian)
    {
        bytes[1] = (byte)(value >> 8);
        bytes[littleEndian ? 0 : 2] = (byte)value;
        bytes[littleEndian ? 2 : 0] = (byte)(value >> 16);
    }
}
