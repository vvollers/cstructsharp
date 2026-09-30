namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Reading;

/// <summary>Builds the primitive writer table used by the CStruct facade.</summary>
public sealed partial class CStruct
{
    /// <summary>
    ///     The canonical, direction-suffixed stream writers the compiled write engine calls through a codec id, keyed by
    ///     the names in <see cref="PrimitiveCatalog.CanonicalNames"/>. The engine encodes the one- to eight-byte
    ///     numbers (<see cref="PrimitiveCodec.IsFixedWidthNumeric"/>) itself, so every other codec has one: LEB128
    ///     integers, fixed-point numbers, UUIDs/GUIDs, 48- and 128-bit integers, <c>float16</c>, the character and
    ///     text-unit codecs, and terminated text (a character-by-character text write). None of these delegates depend on
    ///     per-instance state; the neutral and alias spellings are ids in the catalog that point at the same delegates.
    ///     Built once per process.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Action<Stream, object>> BaseWriteHandlers =
        BuildBaseWriteHandlers();

    private static readonly object CodecTableLock = new();
    private static readonly Dictionary<(bool LittleEndian, int CLongWidth), CodecTable> SharedCodecTables = new();

    /// <summary>
    ///     The shared writer table for one byte order and <c>long</c> width: the catalog's ids over the process-wide
    ///     writer map. A layout without custom codecs uses it directly; one with custom codecs derives its own table
    ///     from it (<see cref="RegisterCustomCodecs"/>).
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
    ///     Builds the writer array a catalog's ids index: the canonical writers in catalog order (null for a codec the
    ///     engine encodes itself), then one adapter per custom codec in registration order.
    /// </summary>
    /// <exception cref="InvalidOperationException">The custom codec instances do not match the catalog's descriptors.</exception>
    private static CodecTable BuildCodecTable(PrimitiveCatalog catalog, ImmutableArray<ICustomCodec> customCodecs)
    {
        var writers = new Action<Stream, object>?[catalog.CodecCount];
        for (int id = 0; id < PrimitiveCatalog.CanonicalNames.Length; id++)
        {
            writers[id] = BaseWriteHandlers.GetValueOrDefault(PrimitiveCatalog.CanonicalNames[id]);
        }

        if (customCodecs.Length != catalog.CustomCodecs.Length)
        {
            throw new InvalidOperationException("The custom codec instances do not match the catalog's custom codec descriptors.");
        }

        for (int index = 0; index < customCodecs.Length; index++)
        {
            ICustomCodec captured = customCodecs[index];
            writers[PrimitiveCatalog.CanonicalNames.Length + index] = (stream, value) => CustomCodecAdapter.Write(captured, stream, value);
        }

        return new CodecTable(catalog, writers) { CustomCodecs = customCodecs, };
    }

    /// <summary>Builds the process-wide, direction-suffixed primitive writer table (see <see cref="BaseWriteHandlers" />).</summary>
    private static Dictionary<string, Action<Stream, object>> BuildBaseWriteHandlers()
    {
        // Each delegate converts the caller value (a conversion failure is the caller's error) and writes exactly one
        // encoded value at the stream position.
        return new Dictionary<string, Action<Stream, object>>
        {
            ["uleb128_32"] = (stream, value) => Leb128Codec.WriteUnsigned(stream, Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            ["uleb128_64"] = (stream, value) => Leb128Codec.WriteUnsigned(stream, Convert.ToUInt64(value, CultureInfo.InvariantCulture)),
            ["sleb128_32"] = (stream, value) => Leb128Codec.WriteSigned(stream, Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            ["sleb128_64"] = (stream, value) => Leb128Codec.WriteSigned(stream, Convert.ToInt64(value, CultureInfo.InvariantCulture)),
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
            ["char"] = (stream, value) => stream.WriteByte(PrimitiveCodecs.ConvertToNarrowCharacter(value)),
            ["latin1"] = (stream, value) => stream.WriteByte(Convert.ToByte(value, CultureInfo.InvariantCulture)),
            ["cp437"] = (stream, value) => stream.WriteByte(Convert.ToByte(value, CultureInfo.InvariantCulture)),
            ["utf16le"] = (stream, value) => stream.WriteByte(Convert.ToByte(value, CultureInfo.InvariantCulture)),
            ["utf16be"] = (stream, value) => stream.WriteByte(Convert.ToByte(value, CultureInfo.InvariantCulture)),
            ["utf8"] = (stream, value) => stream.WriteByte(Convert.ToByte(value, CultureInfo.InvariantCulture)),
            ["wchar>"] = (stream, value) => BinaryPrimitiveIO.WriteChar(stream, Convert.ToChar(value, CultureInfo.InvariantCulture), false),
            ["wchar<"] = (stream, value) => BinaryPrimitiveIO.WriteChar(stream, Convert.ToChar(value, CultureInfo.InvariantCulture), true),
            ["int48>"] = (stream, value) => BinaryPrimitiveIO.WriteInt48(stream, Convert.ToInt64(value, CultureInfo.InvariantCulture), false),
            ["int48<"] = (stream, value) => BinaryPrimitiveIO.WriteInt48(stream, Convert.ToInt64(value, CultureInfo.InvariantCulture), true),
            ["uint48>"] = (stream, value) => BinaryPrimitiveIO.WriteUInt48(stream, Convert.ToUInt64(value, CultureInfo.InvariantCulture), false),
            ["uint48<"] = (stream, value) => BinaryPrimitiveIO.WriteUInt48(stream, Convert.ToUInt64(value, CultureInfo.InvariantCulture), true),
            ["int128>"] = (stream, value) => BinaryPrimitiveIO.WriteInt128(stream, WideIntegerConversion.ToInt128(value), false),
            ["int128<"] = (stream, value) => BinaryPrimitiveIO.WriteInt128(stream, WideIntegerConversion.ToInt128(value), true),
            ["uint128>"] = (stream, value) => BinaryPrimitiveIO.WriteUInt128(stream, WideIntegerConversion.ToUInt128(value), false),
            ["uint128<"] = (stream, value) => BinaryPrimitiveIO.WriteUInt128(stream, WideIntegerConversion.ToUInt128(value), true),
            ["float16>"] = (stream, value) => BinaryPrimitiveIO.WriteHalf(stream, WideIntegerConversion.ToHalf(value), false),
            ["float16<"] = (stream, value) => BinaryPrimitiveIO.WriteHalf(stream, WideIntegerConversion.ToHalf(value), true),
            ["ascii_string_zero"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictAsciiEncoding,
                                                                             Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                                                                             '\0'),
            ["ascii_string_newline"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictAsciiEncoding,
                                                                             Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                                                                             '\n'),
            ["utf8_string_zero"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf8Encoding,
                                                                             Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                                                                             '\0'),
            ["utf8_string_newline"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf8Encoding,
                                                                             Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                                                                             '\n'),
            ["unicode_string_zero>"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf16BigEndianEncoding,
                                                                             Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                                                                             '\0'),
            ["unicode_string_zero<"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf16LittleEndianEncoding,
                                                                             Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                                                                             '\0'),
            ["unicode_string_newline>"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf16BigEndianEncoding,
                                                                             Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                                                                             '\n'),
            ["unicode_string_newline<"]
                                 = (stream, value) => PrimitiveCodecs.WriteTerminatedString(
                                                                             stream,
                                                                             PrimitiveCodecs.StrictUtf16LittleEndianEncoding,
                                                                             Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
                                                                             '\n'),
        };
    }

    /// <summary>The UTF-16 encoding a wide-character field decodes with: its explicit suffix, or the layout byte order.</summary>
    /// <param name="field">The <c>wchar</c> field.</param>
    /// <returns>The strict encoding its text is validated with.</returns>
    internal Encoding GetWideCharacterEncoding(CompiledField field)
    {
        return field.ExplicitWideCharacterEncoding ??
               (this.IsLittleEndian ? PrimitiveCodecs.StrictUtf16LittleEndianEncoding : PrimitiveCodecs.StrictUtf16BigEndianEncoding);
    }
}
