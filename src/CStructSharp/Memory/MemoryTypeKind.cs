namespace CStructSharp.Memory;

/// <summary>The storage category of a <see cref="MemoryTypeDefinition"/>, which decides how a session reads and writes it.</summary>
/// <remarks>
/// The kind selects the rules that <see cref="MemorySchema"/> validates and <see cref="MemorySession"/> executes.
/// A scalar delegates to a core codec, an array repeats fixed-size elements, and composites place their fields at
/// explicit offsets recorded by the metadata rather than computed from a host compiler's ABI. A pointer stores an
/// unsigned address and never embeds its target by value. An incomplete type preserves an identity for
/// address-only metadata such as forward declarations; it can be pointed at but not read.
/// </remarks>
public enum MemoryTypeKind
{
    /// <summary>A value decoded by a core primitive codec or a caller-registered custom codec, named by <see cref="MemoryTypeDefinition.ScalarType"/>.</summary>
    Scalar,

    /// <summary>An unsigned stored address of the declared width, optionally pointing to a known target type.</summary>
    Pointer,

    /// <summary>Members at explicit, nonoverlapping offsets inside a fixed extent that may include padding.</summary>
    Struct,

    /// <summary>Members at explicit offsets that deliberately overlap, so each is one interpretation of shared bytes.</summary>
    Union,

    /// <summary>A fixed number of elements of one type, laid out consecutively with the element's size as stride.</summary>
    Array,

    /// <summary>A type whose representation is unavailable; it has identity and size zero and cannot be read by value.</summary>
    Incomplete,
}
