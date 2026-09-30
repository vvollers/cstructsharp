namespace CStructSharp.Codecs;

using System;
using System.Globalization;
using CStructSharp.Generated;

/// <summary>The runtime half of a primitive descriptor: reading and writing one numeric value through a span, via <see cref="Codec"/>.</summary>
internal readonly partial record struct PrimitiveCodec
{
    // Every one-byte value boxed once: a box cannot be modified, so readers can share them and a parsed byte, sbyte
    // or bool costs no allocation. An sbyte box is indexed by its raw byte.
    private static readonly object[] BoxedBytes = CreateBoxes(static raw => (byte)raw);
    private static readonly object[] BoxedSBytes = CreateBoxes(static raw => unchecked((sbyte)raw));
    private static readonly object BoxedTrue = true;
    private static readonly object BoxedFalse = false;

    /// <summary>
    ///     Encodes one caller-supplied value into exactly this codec's bytes, applying the same <see cref="Convert"/>
    ///     conversion (and the same range failures) as the stream write handler for the same primitive name, so a
    ///     static write plan produces the bytes and the errors of the member-by-member write.
    /// </summary>
    /// <param name="bytes">The destination, at least this codec's size; the value fills its first bytes.</param>
    /// <param name="value">The number to encode, converted to the codec's type with the invariant culture.</param>
    /// <exception cref="OverflowException">The value is outside the codec type's range.</exception>
    /// <exception cref="InvalidOperationException">This codec is not a fixed-width numeric codec.</exception>
    public void WriteNumeric(Span<byte> bytes, object value)
    {
        bool le = this.LittleEndian;
        switch (this.Kind)
        {
        case PrimitiveCodecKind.UInt8:
            bytes[0] = value is byte b ? b : Convert.ToByte(value, CultureInfo.InvariantCulture);
            break;
        case PrimitiveCodecKind.Int8:
            bytes[0] = unchecked((byte)(value is sbyte sb ? sb : Convert.ToSByte(value, CultureInfo.InvariantCulture)));
            break;
        case PrimitiveCodecKind.Bool:
            bytes[0] = (byte)((value is bool flag ? flag : Convert.ToBoolean(value, CultureInfo.InvariantCulture)) ? 1 : 0);
            break;
        case PrimitiveCodecKind.Int16:
            Codec.WriteInt16(bytes, value is short s ? s : Convert.ToInt16(value, CultureInfo.InvariantCulture), le);
            break;
        case PrimitiveCodecKind.UInt16:
            Codec.WriteUInt16(bytes, value is ushort us ? us : Convert.ToUInt16(value, CultureInfo.InvariantCulture), le);
            break;
        case PrimitiveCodecKind.Int24:
            Codec.WriteInt24(bytes, value is int i24 ? i24 : Convert.ToInt32(value, CultureInfo.InvariantCulture), le);
            break;
        case PrimitiveCodecKind.UInt24:
            Codec.WriteUInt24(bytes, value is uint u24 ? u24 : Convert.ToUInt32(value, CultureInfo.InvariantCulture), le);
            break;
        case PrimitiveCodecKind.Int32:
            Codec.WriteInt32(bytes, value is int i ? i : Convert.ToInt32(value, CultureInfo.InvariantCulture), le);
            break;
        case PrimitiveCodecKind.UInt32:
            Codec.WriteUInt32(bytes, value is uint u ? u : Convert.ToUInt32(value, CultureInfo.InvariantCulture), le);
            break;
        case PrimitiveCodecKind.Int64:
            Codec.WriteInt64(bytes, value is long l ? l : Convert.ToInt64(value, CultureInfo.InvariantCulture), le);
            break;
        case PrimitiveCodecKind.UInt64:
            Codec.WriteUInt64(bytes, value is ulong ul ? ul : Convert.ToUInt64(value, CultureInfo.InvariantCulture), le);
            break;
        case PrimitiveCodecKind.Float32:
            Codec.WriteSingle(bytes, value is float f ? f : Convert.ToSingle(value, CultureInfo.InvariantCulture), le);
            break;
        case PrimitiveCodecKind.Float64:
            Codec.WriteDouble(bytes, value is double d ? d : Convert.ToDouble(value, CultureInfo.InvariantCulture), le);
            break;
        default:
            throw new InvalidOperationException("Codec is not a fixed-width numeric: " + this.Kind);
        }
    }

    /// <summary>Decodes one fixed-width numeric element from exactly its bytes into the same boxed CLR type the stream codec produces.</summary>
    /// <param name="bytes">The encoded element, starting at its first byte.</param>
    /// <returns>The boxed value, such as a <c>short</c> for <c>int16</c>; one-byte values share cached boxes.</returns>
    /// <exception cref="InvalidOperationException">This codec is not a fixed-width numeric codec.</exception>
    public object ReadNumeric(ReadOnlySpan<byte> bytes)
    {
        bool le = this.LittleEndian;
        return this.Kind switch
        {
            PrimitiveCodecKind.UInt8 => BoxedBytes[bytes[0]],
            PrimitiveCodecKind.Int8 => BoxedSBytes[bytes[0]],
            PrimitiveCodecKind.Bool => bytes[0] != 0 ? BoxedTrue : BoxedFalse,
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

    /// <summary>Boxes the value of each of the 256 raw bytes.</summary>
    private static object[] CreateBoxes(Func<int, object> box)
    {
        var boxes = new object[256];
        for (int raw = 0; raw < boxes.Length; raw++)
        {
            boxes[raw] = box(raw);
        }

        return boxes;
    }
}
