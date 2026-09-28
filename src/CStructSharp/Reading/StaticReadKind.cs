namespace CStructSharp.Reading;

/// <summary>
///     The value kind of one <see cref="StaticReadOperation"/>: how a fixed composite's field is decoded from, or
///     encoded to, its fixed offset in a span.
/// </summary>
internal enum StaticReadKind : byte
{
    /// <summary>One fixed-width numeric primitive decoded from <c>Codec.Size</c> bytes.</summary>
    Numeric,

    /// <summary>One enum value decoded from its integer storage.</summary>
    Enum,

    /// <summary>A fixed-length character array of <c>Count</c> bytes decoded as Latin-1 text.</summary>
    CharArray,

    /// <summary>A fixed-length array of <c>Count</c> numeric primitives.</summary>
    NumericArray,

    /// <summary>One nested fixed composite read through its own static plan.</summary>
    Nested,

    /// <summary>A fixed-length array of <c>Count</c> nested fixed composites sharing one nested plan.</summary>
    NestedArray,
}
