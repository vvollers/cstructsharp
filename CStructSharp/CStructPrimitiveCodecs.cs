namespace CStructSharp;

using System;
using System.Collections.Generic;
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
    ///     <see cref="BuildFieldHandlers" />, depends on <see cref="IsLittleEndian" />. Built once per process
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

    /// <summary>Builds the process-wide, direction-suffixed primitive reader table (see <see cref="BaseFieldHandlers" />).</summary>
    private static Dictionary<string, Func<Stream, object>> BuildBaseFieldHandlers()
    {
        // First register only the canonical primitive spellings. Each delegate consumes exactly one encoded value.
        return new Dictionary<string, Func<Stream, object>>
        {
            ["byte"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream),
            ["int8"] = stream => unchecked((sbyte)BinaryPrimitiveIO.ReadByteExactly(stream)),
            ["uint8"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream),
            ["bool"] = stream => BinaryPrimitiveIO.ReadByteExactly(stream) != 0,
            ["char"] = stream => (char)BinaryPrimitiveIO.ReadByteExactly(stream),
            ["wchar>"] = stream => BitConverter.ToChar(BinaryPrimitiveIO.ReadIntoBuffer(stream, 2, false)),
            ["wchar<"] = stream => BitConverter.ToChar(BinaryPrimitiveIO.ReadIntoBuffer(stream, 2, true)),
            ["int16>"]
                                 = stream => (short)((BinaryPrimitiveIO.ReadByteExactly(stream) << 8) | BinaryPrimitiveIO.ReadByteExactly(stream)),
            ["int16<"]
                                 = stream => (short)(BinaryPrimitiveIO.ReadByteExactly(stream) | (BinaryPrimitiveIO.ReadByteExactly(stream) << 8)),
            ["uint16>"]
                                 = stream => (ushort)((BinaryPrimitiveIO.ReadByteExactly(stream) << 8) | BinaryPrimitiveIO.ReadByteExactly(stream)),
            ["uint16<"]
                                 = stream => (ushort)(BinaryPrimitiveIO.ReadByteExactly(stream) | (BinaryPrimitiveIO.ReadByteExactly(stream) << 8)),
            ["int32>"]
                                 = stream => (BinaryPrimitiveIO.ReadByteExactly(stream) << 24) |
                                             (BinaryPrimitiveIO.ReadByteExactly(stream) << 16) |
                                             (BinaryPrimitiveIO.ReadByteExactly(stream) << 8) |
                                             BinaryPrimitiveIO.ReadByteExactly(stream),
            ["int32<"]
                                 = stream => BinaryPrimitiveIO.ReadByteExactly(stream) |
                                             (BinaryPrimitiveIO.ReadByteExactly(stream) << 8) |
                                             (BinaryPrimitiveIO.ReadByteExactly(stream) << 16) |
                                             (BinaryPrimitiveIO.ReadByteExactly(stream) << 24),
            ["uint32>"]
                                 = stream => ((uint)BinaryPrimitiveIO.ReadByteExactly(stream) << 24) |
                                             ((uint)BinaryPrimitiveIO.ReadByteExactly(stream) << 16) |
                                             ((uint)BinaryPrimitiveIO.ReadByteExactly(stream) << 8) |
                                             BinaryPrimitiveIO.ReadByteExactly(stream),
            ["uint32<"]
                                 = stream => BinaryPrimitiveIO.ReadByteExactly(stream) |
                                             ((uint)BinaryPrimitiveIO.ReadByteExactly(stream) << 8) |
                                             ((uint)BinaryPrimitiveIO.ReadByteExactly(stream) << 16) |
                                             ((uint)BinaryPrimitiveIO.ReadByteExactly(stream) << 24),
            ["int64>"] = stream => BitConverter.ToInt64(BinaryPrimitiveIO.ReadIntoBuffer(stream, 8, false)),
            ["int64<"] = stream => BitConverter.ToInt64(BinaryPrimitiveIO.ReadIntoBuffer(stream, 8, true)),
            ["uint64>"] = stream => BitConverter.ToUInt64(BinaryPrimitiveIO.ReadIntoBuffer(stream, 8, false)),
            ["uint64<"] = stream => BitConverter.ToUInt64(BinaryPrimitiveIO.ReadIntoBuffer(stream, 8, true)),
            ["float32>"] = stream => BitConverter.ToSingle(BinaryPrimitiveIO.ReadIntoBuffer(stream, 4, false)),
            ["float32<"] = stream => BitConverter.ToSingle(BinaryPrimitiveIO.ReadIntoBuffer(stream, 4, true)),
            ["float64>"] = stream => BitConverter.ToDouble(BinaryPrimitiveIO.ReadIntoBuffer(stream, 8, false)),
            ["float64<"] = stream => BitConverter.ToDouble(BinaryPrimitiveIO.ReadIntoBuffer(stream, 8, true)),
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
            ["byte"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
            ["int8"]
                                 = (stream, value) => stream.WriteByte(unchecked((byte)Convert.ToSByte(value))),
            ["uint8"] = (stream, value) => stream.WriteByte(Convert.ToByte(value)),
            ["bool"] = (stream, value) => stream.WriteByte((byte)(Convert.ToBoolean(value) ? 1 : 0)),
            ["char"] = (stream, value) => stream.WriteByte(PrimitiveCodecs.ConvertToNarrowCharacter(value)),
            ["wchar>"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.GetBytes(Convert.ToChar(value)),
                                                                       false),
            ["wchar<"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.GetBytes(Convert.ToChar(value)),
                                                                       true),
            ["int16>"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.
                                                                           GetBytes(Convert.ToInt16(value)),
                                                                       false),
            ["int16<"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.
                                                                           GetBytes(Convert.ToInt16(value)),
                                                                       true),
            ["uint16>"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.GetBytes(
                                                                        Convert.ToUInt16(value)),
                                                                       false),
            ["uint16<"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.GetBytes(
                                                                        Convert.ToUInt16(value)),
                                                                       true),
            ["int32>"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.
                                                                           GetBytes(Convert.ToInt32(value)),
                                                                       false),
            ["int32<"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.
                                                                           GetBytes(Convert.ToInt32(value)),
                                                                       true),
            ["uint32>"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.GetBytes(
                                                                        Convert.ToUInt32(value)),
                                                                       false),
            ["uint32<"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.GetBytes(
                                                                        Convert.ToUInt32(value)),
                                                                       true),
            ["int64>"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.
                                                                           GetBytes(Convert.ToInt64(value)),
                                                                       false),
            ["int64<"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.
                                                                           GetBytes(Convert.ToInt64(value)),
                                                                       true),
            ["uint64>"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.GetBytes(
                                                                        Convert.ToUInt64(value)),
                                                                       false),
            ["uint64<"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.GetBytes(
                                                                        Convert.ToUInt64(value)),
                                                                       true),
            ["float32>"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.
                                                                           GetBytes(Convert.ToSingle(value)),
                                                                       false),
            ["float32<"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.
                                                                           GetBytes(Convert.ToSingle(value)),
                                                                       true),
            ["float64>"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.
                                                                           GetBytes(Convert.ToDouble(value)),
                                                                       false),
            ["float64<"]
                                 = (stream, value) => BinaryPrimitiveIO.WriteEndianBytes(
                                                                       stream,
                                                                       BitConverter.
                                                                           GetBytes(Convert.ToDouble(value)),
                                                                       true),
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

        foreach (KeyValuePair<string, Func<Stream, object>> fieldHandler in handlers)
        {
            if (PrimitiveCodecs.IsVariableLengthType(fieldHandler.Key))
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

    /// <summary>Builds the named primitive readers and records each primitive's byte size for layout calculations.</summary>
    private void BuildFieldHandlers()
    {
        this.fieldHandlers.ReplaceWith(BaseFieldHandlers);

        // Canonical numeric names have explicit `>` and `<` variants; collect one side to derive their neutral names.
        List<string> fieldTypesWithSpecificEndianness = BaseFieldHandlers.Keys.Where(o => o.EndsWith('>')).ToList();

        // Add the unsuffixed names (for example, int32) using the byte order chosen for this CStruct instance.
        foreach (string alias in fieldTypesWithSpecificEndianness.Select(fieldType => fieldType[..^1]))
        {
            // Choose the instance's default endian reader once so field parsing stays a simple dictionary lookup.
            this.fieldHandlers[alias]
                = this.IsLittleEndian ? this.fieldHandlers[alias + '<'] : this.fieldHandlers[alias + '>'];
        }

        foreach (KeyValuePair<string, string> alias in PrimitiveCodecs.FieldTypeAliasses)
        {
            // C-style spellings such as `int` and `long` are aliases, not separate codecs with different behavior.
            this.fieldHandlers[alias.Key] = this.fieldHandlers[alias.Value];
        }

        // Seed alignments from the precomputed base table, then mirror the same alias resolution used for the
        // handlers above - an alias's consumed byte count is always identical to whichever canonical entry it
        // resolves to, so this only ever copies an already-measured value, never re-measures.
        foreach (KeyValuePair<string, byte> baseAlignment in BaseFieldAlignments)
        {
            this.fieldAlignments[baseAlignment.Key] = baseAlignment.Value;
        }

        foreach (string alias in fieldTypesWithSpecificEndianness.Select(fieldType => fieldType[..^1]))
        {
            this.fieldAlignments[alias]
                = this.IsLittleEndian ? this.fieldAlignments[alias + '<'] : this.fieldAlignments[alias + '>'];
        }

        foreach (KeyValuePair<string, string> alias in PrimitiveCodecs.FieldTypeAliasses)
        {
            this.fieldAlignments[alias.Key] = this.fieldAlignments[alias.Value];
        }
    }

    /// <summary>Builds the named primitive writers using the same aliases and byte order as the readers.</summary>
    private void BuildWriteHandlers()
    {
        this.writeHandlers.ReplaceWith(BaseWriteHandlers);

        // Build default-endian names after both explicit byte-order writers have been registered.
        List<string> fieldTypesWithSpecificEndianness = BaseWriteHandlers.Keys.Where(o => o.EndsWith('>')).ToList();

        foreach (string alias in fieldTypesWithSpecificEndianness.Select(fieldType => fieldType[..^1]))
        {
            // The layout-level endianness selects the neutral writer once, avoiding a branch for every field value.
            this.writeHandlers[alias]
                = this.IsLittleEndian ? this.writeHandlers[alias + '<'] : this.writeHandlers[alias + '>'];
        }

        foreach (KeyValuePair<string, string> alias in PrimitiveCodecs.FieldTypeAliasses)
        {
            // Reuse the canonical delegate for familiar C aliases and string shorthand names.
            this.writeHandlers[alias.Key] = this.writeHandlers[alias.Value];
        }
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
}
