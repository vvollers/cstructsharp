namespace CStructSharp.Codecs;

using System;
using CStructSharp.Generated;

/// <summary>The runtime half of a primitive descriptor: reading and writing one numeric value through a span, via <see cref="Codec"/>.</summary>
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
            Codec.WriteInt16(bytes, value is short s ? s : Convert.ToInt16(value), le);
            break;
        case PrimitiveCodecKind.UInt16:
            Codec.WriteUInt16(bytes, value is ushort us ? us : Convert.ToUInt16(value), le);
            break;
        case PrimitiveCodecKind.Int24:
            Codec.WriteInt24(bytes, value is int i24 ? i24 : Convert.ToInt32(value), le);
            break;
        case PrimitiveCodecKind.UInt24:
            Codec.WriteUInt24(bytes, value is uint u24 ? u24 : Convert.ToUInt32(value), le);
            break;
        case PrimitiveCodecKind.Int32:
            Codec.WriteInt32(bytes, value is int i ? i : Convert.ToInt32(value), le);
            break;
        case PrimitiveCodecKind.UInt32:
            Codec.WriteUInt32(bytes, value is uint u ? u : Convert.ToUInt32(value), le);
            break;
        case PrimitiveCodecKind.Int64:
            Codec.WriteInt64(bytes, value is long l ? l : Convert.ToInt64(value), le);
            break;
        case PrimitiveCodecKind.UInt64:
            Codec.WriteUInt64(bytes, value is ulong ul ? ul : Convert.ToUInt64(value), le);
            break;
        case PrimitiveCodecKind.Float32:
            Codec.WriteSingle(bytes, value is float f ? f : Convert.ToSingle(value), le);
            break;
        case PrimitiveCodecKind.Float64:
            Codec.WriteDouble(bytes, value is double d ? d : Convert.ToDouble(value), le);
            break;
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
            PrimitiveCodecKind.Int16 => Codec.ReadInt16(bytes, le),
            PrimitiveCodecKind.UInt16 => Codec.ReadUInt16(bytes, le),
            PrimitiveCodecKind.Int24 => Codec.ReadInt24(bytes, le),
            PrimitiveCodecKind.UInt24 => Codec.ReadUInt24(bytes, le),
            PrimitiveCodecKind.Int32 => Codec.ReadInt32(bytes, le),
            PrimitiveCodecKind.UInt32 => Codec.ReadUInt32(bytes, le),
            PrimitiveCodecKind.Int64 => Codec.ReadInt64(bytes, le),
            PrimitiveCodecKind.UInt64 => Codec.ReadUInt64(bytes, le),
            PrimitiveCodecKind.Float32 => Codec.ReadSingle(bytes, le),
            PrimitiveCodecKind.Float64 => Codec.ReadDouble(bytes, le),
            _ => throw new InvalidOperationException("Codec is not a fixed-width numeric: " + this.Kind),
        };
    }
}
