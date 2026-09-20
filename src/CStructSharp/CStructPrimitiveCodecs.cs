namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Reading;
using CStructSharp.Syntax;

/// <summary>Builds the primitive binary codec maps used by the CStruct facade.</summary>
public partial class CStruct
{
    /// <summary>
    ///     The canonical, direction-suffixed primitive readers (for example <c>int32&gt;</c>/<c>int32&lt;</c>, or
    ///     <c>byte</c>, which has no direction), keyed by the names in <see cref="PrimitiveCatalog.CanonicalNames"/>.
    ///     None of these delegates depend on per-instance state; the neutral and alias spellings are ids in the
    ///     catalog that point at the same delegates. Built once per process.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<Stream, object>> BaseFieldHandlers =
        BuildBaseFieldHandlers();

    /// <summary>The write-side counterpart to <see cref="BaseFieldHandlers" />, same instance-independence.</summary>
    private static readonly IReadOnlyDictionary<string, Action<Stream, object>> BaseWriteHandlers =
        BuildBaseWriteHandlers();

    private static readonly object CodecTableLock = new();
    private static readonly Dictionary<(bool LittleEndian, int CLongWidth), CodecTable> SharedCodecTables = new();

    /// <summary>
    ///     The shared delegate table for one byte order and <c>long</c> width: the catalog's ids over the process-wide
    ///     reader and writer maps. A layout without custom codecs uses it directly; one with custom codecs derives its
    ///     own table from it (<see cref="RegisterCustomCodecs"/>).
    /// </summary>
    private static CodecTable GetSharedCodecTable(bool littleEndian, int cLongWidth)
    {
        PrimitiveCatalog catalog = PrimitiveCatalog.For(littleEndian, cLongWidth);
        lock (CodecTableLock)
        {
            (bool, int) key = (catalog.LittleEndian, catalog.CLongWidth);
            if (!SharedCodecTables.TryGetValue(key, out CodecTable? table))
            {
                table = BuildCodecTable(catalog, ImmutableArray<ICustomCodec>.Empty);
                SharedCodecTables.Add(key, table);
            }

            return table;
        }
    }

    /// <summary>
    ///     Builds the delegate arrays a catalog's ids index: the canonical delegates in catalog order, then one adapter
    ///     pair per custom codec in registration order. Every canonical name must have a delegate pair - a missing one
    ///     is a programming error the first layout construction reports.
    /// </summary>
    private static CodecTable BuildCodecTable(PrimitiveCatalog catalog, ImmutableArray<ICustomCodec> customCodecs)
    {
        var readers = new Func<Stream, object>?[catalog.CodecCount];
        var writers = new Action<Stream, object>?[catalog.CodecCount];
        for (int id = 0; id < PrimitiveCatalog.CanonicalNames.Length; id++)
        {
            string name = PrimitiveCatalog.CanonicalNames[id];
            readers[id] = BaseFieldHandlers[name];
            writers[id] = BaseWriteHandlers[name];
        }

        if (customCodecs.Length != catalog.CustomCodecs.Length)
        {
            throw new InvalidOperationException("The custom codec instances do not match the catalog's custom codec descriptors.");
        }

        for (int index = 0; index < customCodecs.Length; index++)
        {
            ICustomCodec captured = customCodecs[index];
            int id = PrimitiveCatalog.CanonicalNames.Length + index;
            readers[id] = stream => CustomCodecAdapter.Read(captured, stream);
            writers[id] = (stream, value) => CustomCodecAdapter.Write(captured, stream, value);
        }

        return new CodecTable(catalog, readers, writers);
    }

    /// <summary>Builds the process-wide, direction-suffixed primitive reader table (see <see cref="BaseFieldHandlers" />).</summary>
    private static Dictionary<string, Func<Stream, object>> BuildBaseFieldHandlers()
    {
        // First register only the canonical primitive spellings. Each delegate consumes exactly one encoded value.
        return new Dictionary<string, Func<Stream, object>>
        {
            ["uleb128_32"] = stream => (uint)Leb128Codec.Read(stream, 32, false),
            ["uleb128_64"] = stream => Leb128Codec.Read(stream, 64, false),
            ["sleb128_32"] = stream => unchecked((int)Leb128Codec.Read(stream, 32, true)),
            ["sleb128_64"] = stream => unchecked((long)Leb128Codec.Read(stream, 64, true)),
            ["fixed16_16>"] = stream => FixedPointCodec.Read(stream, false, 32, 16, true),
            ["fixed16_16<"] = stream => FixedPointCodec.Read(stream, true, 32, 16, true),
            ["ufixed16_16>"] = stream => FixedPointCodec.Read(stream, false, 32, 16, false),
            ["ufixed16_16<"] = stream => FixedPointCodec.Read(stream, true, 32, 16, false),
            ["fixed2_30>"] = stream => FixedPointCodec.Read(stream, false, 32, 30, true),
            ["fixed2_30<"] = stream => FixedPointCodec.Read(stream, true, 32, 30, true),
            ["ufixed8_8>"] = stream => FixedPointCodec.Read(stream, false, 16, 8, false),
            ["ufixed8_8<"] = stream => FixedPointCodec.Read(stream, true, 16, 8, false),
            ["uuid"] = stream => IdentifierCodec.Read(stream, true),
            ["guid"] = stream => IdentifierCodec.Read(stream, false),
            ["byte"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream),
            ["int8"] = stream => unchecked((sbyte)BinaryPrimitiveIO.ReadByteExactly(stream)),
            ["uint8"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream),
            ["bool"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream) != 0,
            ["char"] = stream => (char)BinaryPrimitiveIO.ReadByteExactly(stream),
            ["latin1"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream),
            ["cp437"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream),
            ["utf16le"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream),
            ["utf16be"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream),
            ["utf8"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream),
            ["wchar>"] = stream => BinaryPrimitiveIO.ReadChar(stream, false),
            ["wchar<"] = stream => BinaryPrimitiveIO.ReadChar(stream, true),
            ["int16>"] = stream => BinaryPrimitiveIO.ReadInt16(stream, false),
            ["int16<"] = stream => BinaryPrimitiveIO.ReadInt16(stream, true),
            ["uint16>"] = stream => BinaryPrimitiveIO.ReadUInt16(stream, false),
            ["uint16<"] = stream => BinaryPrimitiveIO.ReadUInt16(stream, true),
            ["int24>"] = stream => BinaryPrimitiveIO.ReadInt24(stream, false),
            ["int24<"] = stream => BinaryPrimitiveIO.ReadInt24(stream, true),
            ["uint24>"] = stream => BinaryPrimitiveIO.ReadUInt24(stream, false),
            ["uint24<"] = stream => BinaryPrimitiveIO.ReadUInt24(stream, true),
            ["int32>"] = stream => BinaryPrimitiveIO.ReadInt32(stream, false),
            ["int32<"] = stream => BinaryPrimitiveIO.ReadInt32(stream, true),
            ["uint32>"] = stream => BinaryPrimitiveIO.ReadUInt32(stream, false),
            ["uint32<"] = stream => BinaryPrimitiveIO.ReadUInt32(stream, true),
            ["int48>"] = stream => BinaryPrimitiveIO.ReadInt48(stream, false),
            ["int48<"] = stream => BinaryPrimitiveIO.ReadInt48(stream, true),
            ["uint48>"] = stream => BinaryPrimitiveIO.ReadUInt48(stream, false),
            ["uint48<"] = stream => BinaryPrimitiveIO.ReadUInt48(stream, true),
            ["int128>"] = stream => BinaryPrimitiveIO.ReadInt128(stream, false),
            ["int128<"] = stream => BinaryPrimitiveIO.ReadInt128(stream, true),
            ["uint128>"] = stream => BinaryPrimitiveIO.ReadUInt128(stream, false),
            ["uint128<"] = stream => BinaryPrimitiveIO.ReadUInt128(stream, true),
            ["float16>"] = stream => BinaryPrimitiveIO.ReadHalf(stream, false),
            ["float16<"] = stream => BinaryPrimitiveIO.ReadHalf(stream, true),
            ["int64>"] = stream => BinaryPrimitiveIO.ReadInt64(stream, false),
            ["int64<"] = stream => BinaryPrimitiveIO.ReadInt64(stream, true),
            ["uint64>"] = stream => BinaryPrimitiveIO.ReadUInt64(stream, false),
            ["uint64<"] = stream => BinaryPrimitiveIO.ReadUInt64(stream, true),
            ["float32>"] = stream => BinaryPrimitiveIO.ReadSingle(stream, false),
            ["float32<"] = stream => BinaryPrimitiveIO.ReadSingle(stream, true),
            ["float64>"] = stream => BinaryPrimitiveIO.ReadDouble(stream, false),
            ["float64<"] = stream => BinaryPrimitiveIO.ReadDouble(stream, true),
            ["ascii_string_zero"] = stream => PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictAsciiEncoding, '\0'),
            ["ascii_string_newline"] = stream => PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictAsciiEncoding, '\n'),
            ["utf8_string_zero"] = stream => PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictUtf8Encoding, '\0'),
            ["utf8_string_newline"] = stream => PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictUtf8Encoding, '\n'),
            ["unicode_string_zero>"] = stream => PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictUtf16BigEndianEncoding, '\0'),
            ["unicode_string_zero<"] = stream => PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictUtf16LittleEndianEncoding, '\0'),
            ["unicode_string_newline>"] = stream => PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictUtf16BigEndianEncoding, '\n'),
            ["unicode_string_newline<"] =
                stream => PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictUtf16LittleEndianEncoding, '\n'),
        };
    }

    /// <summary>Builds the process-wide, direction-suffixed primitive writer table (see <see cref="BaseWriteHandlers" />).</summary>
    private static Dictionary<string, Action<Stream, object>> BuildBaseWriteHandlers()
    {
        // Mirror the reader map with canonical writers so serialize and update use the exact same type vocabulary.
        return new Dictionary<string, Action<Stream, object>>
        {
            ["uleb128_32"] = (stream, value) => Leb128Codec.WriteUnsigned(stream, Convert.ToUInt32(value)),
            ["uleb128_64"] = (stream, value) => Leb128Codec.WriteUnsigned(stream, Convert.ToUInt64(value)),
            ["sleb128_32"] = (stream, value) => Leb128Codec.WriteSigned(stream, Convert.ToInt32(value)),
            ["sleb128_64"] = (stream, value) => Leb128Codec.WriteSigned(stream, Convert.ToInt64(value)),
            ["fixed16_16>"] = (stream, value) => FixedPointCodec.Write(stream, value, false, 32, 16, true),
            ["fixed16_16<"] = (stream, value) => FixedPointCodec.Write(stream, value, true, 32, 16, true),
            ["ufixed16_16>"] = (stream, value) => FixedPointCodec.Write(stream, value, false, 32, 16, false),
            ["ufixed16_16<"] = (stream, value) => FixedPointCodec.Write(stream, value, true, 32, 16, false),
            ["fixed2_30>"] = (stream, value) => FixedPointCodec.Write(stream, value, false, 32, 30, true),
            ["fixed2_30<"] = (stream, value) => FixedPointCodec.Write(stream, value, true, 32, 30, true),
            ["ufixed8_8>"] = (stream, value) => FixedPointCodec.Write(stream, value, false, 16, 8, false),
            ["ufixed8_8<"] = (stream, value) => FixedPointCodec.Write(stream, value, true, 16, 8, false),
            ["uuid"] = (stream, value) => IdentifierCodec.Write(stream, value, true),
            ["guid"] = (stream, value) => IdentifierCodec.Write(stream, value, false),
            ["byte"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
            ["int8"]
                                 = (stream, value) => stream.WriteByte(unchecked((byte)Convert.ToSByte(value))),
            ["uint8"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
            ["bool"] = (stream, value) => stream.WriteByte((byte)(Convert.ToBoolean(value) ? 1 : 0)),
            ["char"] = (stream, value) => stream.WriteByte(PrimitiveCodecs.ConvertToNarrowCharacter(value)),
            ["latin1"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
            ["cp437"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
            ["utf16le"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
            ["utf16be"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
            ["utf8"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
            ["wchar>"] = (stream, value) => BinaryPrimitiveIO.WriteChar(stream, Convert.ToChar(value), false),
            ["wchar<"] = (stream, value) => BinaryPrimitiveIO.WriteChar(stream, Convert.ToChar(value), true),
            ["int16>"] = (stream, value) => BinaryPrimitiveIO.WriteInt16(stream, Convert.ToInt16(value), false),
            ["int16<"] = (stream, value) => BinaryPrimitiveIO.WriteInt16(stream, Convert.ToInt16(value), true),
            ["uint16>"] = (stream, value) => BinaryPrimitiveIO.WriteUInt16(stream, Convert.ToUInt16(value), false),
            ["uint16<"] = (stream, value) => BinaryPrimitiveIO.WriteUInt16(stream, Convert.ToUInt16(value), true),
            ["int24>"] = (stream, value) => BinaryPrimitiveIO.WriteInt24(stream, Convert.ToInt32(value), false),
            ["int24<"] = (stream, value) => BinaryPrimitiveIO.WriteInt24(stream, Convert.ToInt32(value), true),
            ["uint24>"] = (stream, value) => BinaryPrimitiveIO.WriteUInt24(stream, Convert.ToUInt32(value), false),
            ["uint24<"] = (stream, value) => BinaryPrimitiveIO.WriteUInt24(stream, Convert.ToUInt32(value), true),
            ["int32>"] = (stream, value) => BinaryPrimitiveIO.WriteInt32(stream, Convert.ToInt32(value), false),
            ["int32<"] = (stream, value) => BinaryPrimitiveIO.WriteInt32(stream, Convert.ToInt32(value), true),
            ["uint32>"] = (stream, value) => BinaryPrimitiveIO.WriteUInt32(stream, Convert.ToUInt32(value), false),
            ["uint32<"] = (stream, value) => BinaryPrimitiveIO.WriteUInt32(stream, Convert.ToUInt32(value), true),
            ["int48>"] = (stream, value) => BinaryPrimitiveIO.WriteInt48(stream, Convert.ToInt64(value), false),
            ["int48<"] = (stream, value) => BinaryPrimitiveIO.WriteInt48(stream, Convert.ToInt64(value), true),
            ["uint48>"] = (stream, value) => BinaryPrimitiveIO.WriteUInt48(stream, Convert.ToUInt64(value), false),
            ["uint48<"] = (stream, value) => BinaryPrimitiveIO.WriteUInt48(stream, Convert.ToUInt64(value), true),
            ["int128>"] = (stream, value) => BinaryPrimitiveIO.WriteInt128(stream, WideIntegerConversion.ToInt128(value), false),
            ["int128<"] = (stream, value) => BinaryPrimitiveIO.WriteInt128(stream, WideIntegerConversion.ToInt128(value), true),
            ["uint128>"] = (stream, value) => BinaryPrimitiveIO.WriteUInt128(stream, WideIntegerConversion.ToUInt128(value), false),
            ["uint128<"] = (stream, value) => BinaryPrimitiveIO.WriteUInt128(stream, WideIntegerConversion.ToUInt128(value), true),
            ["float16>"] = (stream, value) => BinaryPrimitiveIO.WriteHalf(stream, WideIntegerConversion.ToHalf(value), false),
            ["float16<"] = (stream, value) => BinaryPrimitiveIO.WriteHalf(stream, WideIntegerConversion.ToHalf(value), true),
            ["int64>"] = (stream, value) => BinaryPrimitiveIO.WriteInt64(stream, Convert.ToInt64(value), false),
            ["int64<"] = (stream, value) => BinaryPrimitiveIO.WriteInt64(stream, Convert.ToInt64(value), true),
            ["uint64>"] = (stream, value) => BinaryPrimitiveIO.WriteUInt64(stream, Convert.ToUInt64(value), false),
            ["uint64<"] = (stream, value) => BinaryPrimitiveIO.WriteUInt64(stream, Convert.ToUInt64(value), true),
            ["float32>"] = (stream, value) => BinaryPrimitiveIO.WriteSingle(stream, Convert.ToSingle(value), false),
            ["float32<"] = (stream, value) => BinaryPrimitiveIO.WriteSingle(stream, Convert.ToSingle(value), true),
            ["float64>"] = (stream, value) => BinaryPrimitiveIO.WriteDouble(stream, Convert.ToDouble(value), false),
            ["float64<"] = (stream, value) => BinaryPrimitiveIO.WriteDouble(stream, Convert.ToDouble(value), true),
            ["ascii_string_zero"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictAsciiEncoding,
                                                                             Convert.ToString(value) ?? string.Empty,
                                                                             '\0'),
            ["ascii_string_newline"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictAsciiEncoding,
                                                                             Convert.ToString(value) ?? string.Empty,
                                                                             '\n'),
            ["utf8_string_zero"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf8Encoding,
                                                                             Convert.ToString(value) ?? string.Empty,
                                                                             '\0'),
            ["utf8_string_newline"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf8Encoding,
                                                                             Convert.ToString(value) ?? string.Empty,
                                                                             '\n'),
            ["unicode_string_zero>"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf16BigEndianEncoding,
                                                                             Convert.ToString(value) ?? string.Empty,
                                                                             '\0'),
            ["unicode_string_zero<"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf16LittleEndianEncoding,
                                                                             Convert.ToString(value) ?? string.Empty,
                                                                             '\0'),
            ["unicode_string_newline>"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf16BigEndianEncoding,
                                                                             Convert.ToString(value) ?? string.Empty,
                                                                             '\n'),
            ["unicode_string_newline<"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf16LittleEndianEncoding,
                                                                             Convert.ToString(value) ?? string.Empty,
                                                                             '\n'),
        };
    }

    /// <summary>The UTF-16 encoding a wide-character field decodes with: its explicit suffix, or the layout byte order.</summary>
    private Encoding GetWideCharacterEncoding(CompiledField field)
    {
        return field.ExplicitWideCharacterEncoding ??
               (this.IsLittleEndian ? PrimitiveCodecs.StrictUtf16LittleEndianEncoding : PrimitiveCodecs.StrictUtf16BigEndianEncoding);
    }
}
