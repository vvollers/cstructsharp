namespace CStructSharp.Codecs;

using System;
using CStructSharp.Diagnostics;

/// <summary>Identity of a primitive codec, resolved once per compiled field.</summary>
internal enum PrimitiveCodecKind : byte
{
    /// <summary>Not a primitive (composite, pointer, unknown).</summary>
    None,

    /// <summary>Unsigned 8-bit integer (<c>byte</c>, <c>uint8</c>), 1 byte.</summary>
    UInt8,

    /// <summary>Signed 8-bit integer (<c>int8</c>), 1 byte.</summary>
    Int8,

    /// <summary>Boolean stored in 1 byte; any nonzero byte reads as true.</summary>
    Bool,

    /// <summary>One-byte character (<c>char</c>).</summary>
    Char,

    /// <summary>One ISO-8859-1 (Latin-1) code unit, 1 byte.</summary>
    Latin1,

    /// <summary>One IBM code page 437 code unit, 1 byte.</summary>
    Cp437,

    /// <summary>One UTF-8 code unit, 1 byte; arrays of it decode as bounded UTF-8 text.</summary>
    Utf8Unit,

    /// <summary>UTF-16 little-endian text counted in bytes (<c>utf16le</c>); arrays decode as bounded text.</summary>
    Utf16LeUnit,

    /// <summary>UTF-16 big-endian text counted in bytes (<c>utf16be</c>); arrays decode as bounded text.</summary>
    Utf16BeUnit,

    /// <summary>One 2-byte wide character (<c>wchar</c>) in the field's byte order.</summary>
    WChar,

    /// <summary>Signed 16-bit integer, 2 bytes.</summary>
    Int16,

    /// <summary>Unsigned 16-bit integer, 2 bytes.</summary>
    UInt16,

    /// <summary>Signed 24-bit integer, 3 bytes.</summary>
    Int24,

    /// <summary>Unsigned 24-bit integer, 3 bytes.</summary>
    UInt24,

    /// <summary>Signed 32-bit integer, 4 bytes.</summary>
    Int32,

    /// <summary>Unsigned 32-bit integer, 4 bytes.</summary>
    UInt32,

    /// <summary>Signed 64-bit integer, 8 bytes.</summary>
    Int64,

    /// <summary>Unsigned 64-bit integer, 8 bytes.</summary>
    UInt64,

    /// <summary>IEEE 754 single-precision float, 4 bytes.</summary>
    Float32,

    /// <summary>IEEE 754 double-precision float, 8 bytes.</summary>
    Float64,

    /// <summary>Variable-length unsigned LEB128 value limited to 32 bits.</summary>
    ULeb128_32,

    /// <summary>Variable-length unsigned LEB128 value limited to 64 bits.</summary>
    ULeb128_64,

    /// <summary>Variable-length signed LEB128 value limited to 32 bits.</summary>
    SLeb128_32,

    /// <summary>Variable-length signed LEB128 value limited to 64 bits.</summary>
    SLeb128_64,

    /// <summary>Signed 16.16 fixed-point number, 4 bytes.</summary>
    Fixed16_16,

    /// <summary>Unsigned 16.16 fixed-point number, 4 bytes.</summary>
    UFixed16_16,

    /// <summary>Signed 2.30 fixed-point number, 4 bytes.</summary>
    Fixed2_30,

    /// <summary>Unsigned 8.8 fixed-point number, 2 bytes.</summary>
    UFixed8_8,

    /// <summary>16-byte UUID stored in network (big-endian) order.</summary>
    Uuid,

    /// <summary>16-byte GUID in Windows order (first three groups little-endian).</summary>
    Guid,

    /// <summary>ASCII text ended by a terminator character (NUL or newline).</summary>
    TerminatedAscii,

    /// <summary>UTF-8 text ended by a terminator character (NUL or newline).</summary>
    TerminatedUtf8,

    /// <summary>UTF-16 text ended by a terminator code unit (NUL or newline).</summary>
    TerminatedUtf16,

    // Wide and narrow numerics past the bulk/static-plan range: read through their delegates, never span-decoded.

    /// <summary>Signed 48-bit integer, 6 bytes.</summary>
    Int48,

    /// <summary>Unsigned 48-bit integer, 6 bytes.</summary>
    UInt48,

    /// <summary>Signed 128-bit integer, 16 bytes.</summary>
    Int128,

    /// <summary>Unsigned 128-bit integer, 16 bytes.</summary>
    UInt128,

    /// <summary>IEEE 754 half-precision float, 2 bytes.</summary>
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
    /// <summary>The non-primitive codec: kind <see cref="PrimitiveCodecKind.None"/>, size 0, little-endian layout.</summary>
    public static readonly PrimitiveCodec None = new(PrimitiveCodecKind.None, 0, true, '\0', true);

    /// <summary>Gets a value indicating whether the field has a primitive codec (any kind except None).</summary>
    public bool IsPrimitive => this.Kind != PrimitiveCodecKind.None;

    /// <summary>Fixed-width integers, booleans, and floats decodable from exactly <see cref="Size"/> bytes.</summary>
    public bool IsFixedWidthNumeric => this.Kind is >= PrimitiveCodecKind.UInt8 and <= PrimitiveCodecKind.Float64 and not
        (PrimitiveCodecKind.Char or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or
         PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit or PrimitiveCodecKind.WChar);

    /// <summary>
    ///     Gets a value indicating whether the codec is a one-byte text unit (UTF-8, Latin-1, CP437, or UTF-16 counted
    ///     in bytes) whose arrays decode as text bounded by the array's byte length.
    /// </summary>
    public bool IsBoundedText => this.Kind is PrimitiveCodecKind.Utf8Unit or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or
        PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit;

    /// <summary>Gets a value indicating whether the codec reads variable-length text up to <see cref="Terminator"/>.</summary>
    public bool IsTerminatedText => this.Kind is PrimitiveCodecKind.TerminatedAscii or PrimitiveCodecKind.TerminatedUtf8 or PrimitiveCodecKind.TerminatedUtf16;

    /// <summary>Gets a value indicating whether the codec is a variable-length signed or unsigned LEB128 integer.</summary>
    public bool IsLeb128 => this.Kind is PrimitiveCodecKind.ULeb128_32 or PrimitiveCodecKind.ULeb128_64 or PrimitiveCodecKind.SLeb128_32 or PrimitiveCodecKind.SLeb128_64;

    /// <summary>Gets a value indicating whether the codec is one of the fixed-point number formats.</summary>
    public bool IsFixedPoint => this.Kind is PrimitiveCodecKind.Fixed16_16 or PrimitiveCodecKind.UFixed16_16 or PrimitiveCodecKind.Fixed2_30 or PrimitiveCodecKind.UFixed8_8;

    /// <summary>Gets a value indicating whether the codec is a 16-byte UUID or GUID identifier.</summary>
    public bool IsIdentifier => this.Kind is PrimitiveCodecKind.Uuid or PrimitiveCodecKind.Guid;

    /// <summary>Gets a value indicating whether the codec is a caller-supplied <see cref="ICustomCodec"/>.</summary>
    public bool IsCustom => this.Kind == PrimitiveCodecKind.Custom;

    /// <summary>Resolves a codec name from the primitive registry vocabulary; unknown names map to <see cref="None"/>.</summary>
    /// <param name="codecName">
    ///     The codec spelling, optionally suffixed with <c>&lt;</c> (little-endian) or <c>&gt;</c> (big-endian);
    ///     null or empty yields <see cref="None"/>.
    /// </param>
    /// <param name="layoutLittleEndian">The layout's default byte order, applied to spellings without a suffix.</param>
    /// <returns>The resolved codec, or <see cref="None"/> carrying the layout byte order when the name is unknown.</returns>
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
