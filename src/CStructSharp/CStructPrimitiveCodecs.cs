namespace CStructSharp;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using CStructSharp.Structure;

/// <summary>Builds the primitive binary codec maps used by the CStruct facade.</summary>
public partial class CStruct
{
    /// <summary>
    ///     The direction-suffixed and byte-order-agnostic primitive readers (for example <c>int32&gt;</c>/
    ///     <c>int32&lt;</c>, or <c>byte</c>, which has no direction). None of these delegates depend on any
    ///     per-instance state - only the unsuffixed/C-style alias layer, built per instance in
    ///     <see cref="CreatePrimitiveRegistry" />, depends on <see cref="IsLittleEndian" />. Built once per process
    ///     instead of once per <see cref="CStruct" /> construction.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Func<Stream, object>> BaseFieldHandlers =
        BuildBaseFieldHandlers();

    /// <summary>The write-side counterpart to <see cref="BaseFieldHandlers" />, same instance-independence.</summary>
    private static readonly IReadOnlyDictionary<string, Action<Stream, object>> BaseWriteHandlers =
        BuildBaseWriteHandlers();

    /// <summary>
    ///     Each <see cref="BaseFieldHandlers" /> entry's fixed byte width, measured once by actually running the
    ///     reader - keeping the measurement coupled to the real reader logic instead of a separately maintained
    ///     constant table. A per-instance unsuffixed/alias key's alignment is always identical to whichever
    ///     canonical entry it resolves to (they share the same delegate), so it is copied rather than re-measured.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, byte> BaseFieldAlignments =
        MeasureBaseFieldAlignments(BaseFieldHandlers);

    private static readonly Lazy<PrimitiveRegistry> LittleEndianRegistry = new(() => CreatePrimitiveRegistry(true));
    private static readonly Lazy<PrimitiveRegistry> BigEndianRegistry = new(() => CreatePrimitiveRegistry(false));

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

    /// <summary>
    ///     Runs every non-variable-length reader in <paramref name="handlers" /> against enough zero bytes to
    ///     measure its fixed width for alignment and sizing calculations - once per canonical entry, computed
    ///     once for the whole process rather than once per <see cref="CStruct" /> construction.
    /// </summary>
    private static Dictionary<string, byte> MeasureBaseFieldAlignments(IReadOnlyDictionary<string, Func<Stream, object>> handlers)
    {
        var alignments = new Dictionary<string, byte>(StringComparer.Ordinal);
        byte[] buffer = "\x00\n\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00\x00".Select(o => (byte)o).ToArray();

        Array.Resize(ref buffer, 16);

        foreach (KeyValuePair<string, Func<Stream, object>> fieldHandler in handlers)
        {
            if (PrimitiveCodecs.IsVariableLengthType(fieldHandler.Key) || Leb128Codec.IsType(fieldHandler.Key))
            {
                // A terminated string has no fixed footprint. Keep its alignment at one without trying to read a
                // synthetic terminator, because the real reader now correctly treats an unterminated string as an error.
                alignments[fieldHandler.Key] = 1;
                continue;
            }

            var stream = new MemoryStream(buffer);
            fieldHandler.Value(stream);

            // Variable-length strings report zero consumed bytes here, which is normalized to alignment one.
            alignments[fieldHandler.Key] = (byte)Math.Max(1, stream.Position);
        }

        return alignments;
    }

    /// <summary>Builds immutable primitive descriptors once per byte order, independent of user layouts and pointer widths.</summary>
    private static PrimitiveRegistry CreatePrimitiveRegistry(bool littleEndian)
    {
        var readers = new Dictionary<string, Func<Stream, object>>(BaseFieldHandlers, StringComparer.Ordinal);
        var writers = new Dictionary<string, Action<Stream, object>>(BaseWriteHandlers, StringComparer.Ordinal);
        var alignments = new Dictionary<string, byte>(BaseFieldAlignments, StringComparer.Ordinal);
        foreach (string name in BaseFieldHandlers.Keys.Where(name => name.EndsWith('>')))
        {
            string neutral = name[..^1];
            string canonical = neutral + (littleEndian ? '<' : '>');
            readers.Add(neutral, readers[canonical]);
            writers.Add(neutral, writers[canonical]);
            alignments.Add(neutral, alignments[canonical]);
        }

        foreach ((string alias, string canonical) in PrimitiveCodecs.FieldTypeAliasses)
        {
            readers.Add(alias, readers[canonical]);
            writers.Add(alias, writers[canonical]);
            alignments.Add(alias, alignments[canonical]);
        }

        var symbols = ImmutableDictionary.CreateBuilder<string, CompiledTypeReference>(StringComparer.Ordinal);
        foreach ((string name, Func<Stream, object> reader) in readers)
        {
            int? size = PrimitiveCodecs.IsVariableLengthType(name) || Leb128Codec.IsType(name) ? null : alignments[name];
            var symbol = new CompiledTypeSymbol(name, CompiledTypeKind.Primitive, null, size == 3 || name is "uuid" or "guid" ? 1 : alignments[name], size, reader, writers[name]);
            symbol.Bind(new CompiledPrimitiveType(symbol));
            symbol.Freeze();
            symbols.Add(name, new CompiledTypeReference(symbol, 0, name));
        }

        return new PrimitiveRegistry(
            readers.ToFrozenDictionary(StringComparer.Ordinal),
            writers.ToFrozenDictionary(StringComparer.Ordinal),
            alignments.ToFrozenDictionary(StringComparer.Ordinal),
            symbols.ToImmutable(),
            new BitfieldCodecTable(littleEndian, alignments, readers, writers, PrimitiveCodecs.FieldTypeAliasses));
    }

    /// <summary>Selects strict UTF-16 in the explicit field order, or in the layout order for neutral <c>wchar</c>.</summary>
    private Encoding GetWideCharacterEncoding(Identifier type)
    {
        if (type.Equals(CharacterFieldTypes.WcharBigEndianType))
        {
            return PrimitiveCodecs.StrictUtf16BigEndianEncoding;
        }

        if (type.Equals(CharacterFieldTypes.WcharLittleEndianType))
        {
            return PrimitiveCodecs.StrictUtf16LittleEndianEncoding;
        }

        return this.IsLittleEndian ? PrimitiveCodecs.StrictUtf16LittleEndianEncoding : PrimitiveCodecs.StrictUtf16BigEndianEncoding;
    }

    /// <summary>All members are constructed once and used read-only by layouts of the same byte order.</summary>
    private sealed record PrimitiveRegistry(
        FrozenDictionary<string, Func<Stream, object>> Readers,
        FrozenDictionary<string, Action<Stream, object>> Writers,
        FrozenDictionary<string, byte> Alignments,
        ImmutableDictionary<string, CompiledTypeReference> Symbols,
        BitfieldCodecTable Bitfields);
}
