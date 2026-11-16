namespace CStructSharp.Codecs;

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
