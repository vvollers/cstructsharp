namespace CStructSharp.Codecs;

using System;
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
internal readonly partial record struct PrimitiveCodec(PrimitiveCodecKind Kind, byte Size, bool LittleEndian, char Terminator, bool LayoutLittleEndian)
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
        if (codecName is null || codecName.Length == 0)
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
        string name = codecName;
        if (name.Length > 0 && name[name.Length - 1] == '<')
        {
            littleEndian = true;
            name = name.Substring(0, name.Length - 1);
        }
        else if (name.Length > 0 && name[name.Length - 1] == '>')
        {
            littleEndian = false;
            name = name.Substring(0, name.Length - 1);
        }

        // Runs once per compiled field, including wide layouts; the one substring per suffixed name is negligible.
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
}
