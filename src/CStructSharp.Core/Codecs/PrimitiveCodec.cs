namespace CStructSharp.Codecs;

using System;
using System.Buffers.Binary;
using CStructSharp.Diagnostics;

/// <summary>Identity of a primitive codec, resolved once per compiled field.</summary>
internal enum PrimitiveCodecKind : byte
{
    /// <summary>Not a primitive (composite, pointer, unknown).</summary>
    None,
    UInt8,
    Int8,
    Bool,
    Char,
    Latin1,
    Cp437,
    Utf8Unit,
    Utf16LeUnit,
    Utf16BeUnit,
    WChar,
    Int16,
    UInt16,
    Int24,
    UInt24,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float32,
    Float64,
    ULeb128_32,
    ULeb128_64,
    SLeb128_32,
    SLeb128_64,
    Fixed16_16,
    UFixed16_16,
    Fixed2_30,
    UFixed8_8,
    Uuid,
    Guid,
    TerminatedAscii,
    TerminatedUtf8,
    TerminatedUtf16,

    // Wide and narrow numerics past the bulk/static-plan range: read through their delegates, never span-decoded.
    Int48,
    UInt48,
    Int128,
    UInt128,
    Float16,

    /// <summary>A caller-supplied <see cref="ICustomCodec"/>: delegate path only, size from the codec.</summary>
    Custom,
}

/// <summary>
///     Everything the hot paths need to know about a field's primitive codec without touching its name again:
///     the kind, the fixed element size (0 when variable), the byte order (neutral spellings resolved against the
///     layout's order at compile time), and the category flags, so no hot path compares the spelling again.
/// </summary>
internal readonly record struct PrimitiveCodec(PrimitiveCodecKind Kind, byte Size, bool LittleEndian, char Terminator, bool LayoutLittleEndian)
{
    public static readonly PrimitiveCodec None = new(PrimitiveCodecKind.None, 0, true, '\0', true);

    public bool IsPrimitive => this.Kind != PrimitiveCodecKind.None;

    /// <summary>Fixed-width integers, booleans, and floats decodable from exactly <see cref="Size"/> bytes.</summary>
    public bool IsFixedWidthNumeric => this.Kind is >= PrimitiveCodecKind.UInt8 and <= PrimitiveCodecKind.Float64 and not
        (PrimitiveCodecKind.Char or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or
         PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit or PrimitiveCodecKind.WChar);

    public bool IsBoundedText => this.Kind is PrimitiveCodecKind.Utf8Unit or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or
        PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit;

    public bool IsTerminatedText => this.Kind is PrimitiveCodecKind.TerminatedAscii or PrimitiveCodecKind.TerminatedUtf8 or PrimitiveCodecKind.TerminatedUtf16;

    public bool IsLeb128 => this.Kind is PrimitiveCodecKind.ULeb128_32 or PrimitiveCodecKind.ULeb128_64 or PrimitiveCodecKind.SLeb128_32 or PrimitiveCodecKind.SLeb128_64;

    public bool IsFixedPoint => this.Kind is PrimitiveCodecKind.Fixed16_16 or PrimitiveCodecKind.UFixed16_16 or PrimitiveCodecKind.Fixed2_30 or PrimitiveCodecKind.UFixed8_8;

    public bool IsIdentifier => this.Kind is PrimitiveCodecKind.Uuid or PrimitiveCodecKind.Guid;

    public bool IsCustom => this.Kind == PrimitiveCodecKind.Custom;

    /// <summary>Resolves a codec name from the primitive registry vocabulary; unknown names map to <see cref="None"/>.</summary>
    public static PrimitiveCodec Resolve(string? codecName, bool layoutLittleEndian)
    {
        if (string.IsNullOrEmpty(codecName))
        {
            return None with { LayoutLittleEndian = layoutLittleEndian };
        }

        // Compiled fields carry canonical names (the registry resolves alias spellings to canonical symbols), so
        // the switch is tried on the spelling as given; only an unknown spelling - a caller resolving a declared
        // name directly - pays for the alias lookup. The long family is resolved at its default width there; a
        // layout's CLongWidth is applied by its registry, not by this lookup.
        PrimitiveCodec resolved = ResolveCanonical(codecName, layoutLittleEndian);
        if (resolved.Kind == PrimitiveCodecKind.None)
        {
            string canonical = PrimitiveSpellings.Canonicalize(codecName, 64);
            if (!ReferenceEquals(canonical, codecName))
            {
                resolved = ResolveCanonical(canonical, layoutLittleEndian);
            }
        }

        return resolved;
    }

    private static PrimitiveCodec ResolveCanonical(string codecName, bool layoutLittleEndian)
    {
        bool littleEndian = layoutLittleEndian;
        ReadOnlySpan<char> name = codecName;
        if (name.Length > 0 && name[^1] == '<')
        {
            littleEndian = true;
            name = name[..^1];
        }
        else if (name.Length > 0 && name[^1] == '>')
        {
            littleEndian = false;
            name = name[..^1];
        }

        // Span patterns keep this allocation-free; it runs once per compiled field, including wide layouts.
        return name switch
        {
            "byte" or "uint8" => new(PrimitiveCodecKind.UInt8, 1, littleEndian, '\0', layoutLittleEndian),
            "int8" => new(PrimitiveCodecKind.Int8, 1, littleEndian, '\0', layoutLittleEndian),
            "bool" => new(PrimitiveCodecKind.Bool, 1, littleEndian, '\0', layoutLittleEndian),
            "char" => new(PrimitiveCodecKind.Char, 1, littleEndian, '\0', layoutLittleEndian),
            "latin1" => new(PrimitiveCodecKind.Latin1, 1, littleEndian, '\0', layoutLittleEndian),
            "cp437" => new(PrimitiveCodecKind.Cp437, 1, littleEndian, '\0', layoutLittleEndian),
            "utf8" => new(PrimitiveCodecKind.Utf8Unit, 1, littleEndian, '\0', layoutLittleEndian),
            "utf16le" => new(PrimitiveCodecKind.Utf16LeUnit, 1, true, '\0', layoutLittleEndian),
            "utf16be" => new(PrimitiveCodecKind.Utf16BeUnit, 1, false, '\0', layoutLittleEndian),
            "wchar" => new(PrimitiveCodecKind.WChar, 2, littleEndian, '\0', layoutLittleEndian),
            "int16" => new(PrimitiveCodecKind.Int16, 2, littleEndian, '\0', layoutLittleEndian),
            "uint16" => new(PrimitiveCodecKind.UInt16, 2, littleEndian, '\0', layoutLittleEndian),
            "int24" => new(PrimitiveCodecKind.Int24, 3, littleEndian, '\0', layoutLittleEndian),
            "uint24" => new(PrimitiveCodecKind.UInt24, 3, littleEndian, '\0', layoutLittleEndian),
            "int32" => new(PrimitiveCodecKind.Int32, 4, littleEndian, '\0', layoutLittleEndian),
            "uint32" => new(PrimitiveCodecKind.UInt32, 4, littleEndian, '\0', layoutLittleEndian),
            "int64" => new(PrimitiveCodecKind.Int64, 8, littleEndian, '\0', layoutLittleEndian),
            "uint64" => new(PrimitiveCodecKind.UInt64, 8, littleEndian, '\0', layoutLittleEndian),
            "float32" => new(PrimitiveCodecKind.Float32, 4, littleEndian, '\0', layoutLittleEndian),
            "float64" => new(PrimitiveCodecKind.Float64, 8, littleEndian, '\0', layoutLittleEndian),
            "int48" => new(PrimitiveCodecKind.Int48, 6, littleEndian, '\0', layoutLittleEndian),
            "uint48" => new(PrimitiveCodecKind.UInt48, 6, littleEndian, '\0', layoutLittleEndian),
            "int128" => new(PrimitiveCodecKind.Int128, 16, littleEndian, '\0', layoutLittleEndian),
            "uint128" => new(PrimitiveCodecKind.UInt128, 16, littleEndian, '\0', layoutLittleEndian),
            "float16" => new(PrimitiveCodecKind.Float16, 2, littleEndian, '\0', layoutLittleEndian),
            "uleb128_32" => new(PrimitiveCodecKind.ULeb128_32, 0, littleEndian, '\0', layoutLittleEndian),
            "uleb128_64" => new(PrimitiveCodecKind.ULeb128_64, 0, littleEndian, '\0', layoutLittleEndian),
            "sleb128_32" => new(PrimitiveCodecKind.SLeb128_32, 0, littleEndian, '\0', layoutLittleEndian),
            "sleb128_64" => new(PrimitiveCodecKind.SLeb128_64, 0, littleEndian, '\0', layoutLittleEndian),
            "fixed16_16" => new(PrimitiveCodecKind.Fixed16_16, 4, littleEndian, '\0', layoutLittleEndian),
            "ufixed16_16" => new(PrimitiveCodecKind.UFixed16_16, 4, littleEndian, '\0', layoutLittleEndian),
            "fixed2_30" => new(PrimitiveCodecKind.Fixed2_30, 4, littleEndian, '\0', layoutLittleEndian),
            "ufixed8_8" => new(PrimitiveCodecKind.UFixed8_8, 2, littleEndian, '\0', layoutLittleEndian),
            "uuid" => new(PrimitiveCodecKind.Uuid, 16, littleEndian, '\0', layoutLittleEndian),
            "guid" => new(PrimitiveCodecKind.Guid, 16, littleEndian, '\0', layoutLittleEndian),
            "ascii_string_zero" => new(PrimitiveCodecKind.TerminatedAscii, 0, littleEndian, '\0', layoutLittleEndian),
            "ascii_string_newline" => new(PrimitiveCodecKind.TerminatedAscii, 0, littleEndian, '\n', layoutLittleEndian),
            "utf8_string_zero" => new(PrimitiveCodecKind.TerminatedUtf8, 0, littleEndian, '\0', layoutLittleEndian),
            "utf8_string_newline" => new(PrimitiveCodecKind.TerminatedUtf8, 0, littleEndian, '\n', layoutLittleEndian),
            "unicode_string_zero" => new(PrimitiveCodecKind.TerminatedUtf16, 0, littleEndian, '\0', layoutLittleEndian),
            "unicode_string_newline" => new(PrimitiveCodecKind.TerminatedUtf16, 0, littleEndian, '\n', layoutLittleEndian),
            _ => None with { LayoutLittleEndian = layoutLittleEndian },
        };
    }

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

    private static void WriteUInt24(Span<byte> bytes, uint value, bool littleEndian)
    {
        bytes[1] = (byte)(value >> 8);
        bytes[littleEndian ? 0 : 2] = (byte)value;
        bytes[littleEndian ? 2 : 0] = (byte)(value >> 16);
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
}
