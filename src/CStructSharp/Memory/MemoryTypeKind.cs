namespace CStructSharp.Memory;

/// <summary>The storage category of a <see cref="MemoryTypeDefinition"/>, which decides how a session reads and writes it.</summary>
/// <remarks>
/// The kind selects the rules that <see cref="MemorySchema"/> validates and <see cref="MemorySession"/> executes.
/// A scalar delegates to a core codec, an array repeats fixed-size elements, and composites place their fields at
/// explicit offsets recorded by the metadata rather than computed from a host compiler's ABI. A pointer stores an
/// unsigned address and never embeds its target by value. An incomplete type preserves an identity for
/// address-only metadata such as forward declarations; it can be pointed at but not read. A raw-bytes type keeps a
/// trustworthy size for a member or element whose own internal layout did not hold up, so its container's
/// placement math stays correct even though that one member can only be read as raw bytes. (A pointer with no
/// target type, like C's <c>void *</c>, is an <em>opaque pointer</em>: a <see cref="Pointer"/> whose
/// <see cref="MemoryTypeDefinition.ElementTypeId"/> is null. That is a different thing from a raw-bytes type.)
/// </remarks>
public enum MemoryTypeKind
{
    /// <summary>A value decoded by a core primitive codec or a caller-registered custom codec, named by <see cref="MemoryTypeDefinition.ScalarType"/>.</summary>
    Scalar,

    /// <summary>An unsigned stored address of the declared width, pointing to a known target type or, as an opaque pointer with no target type, to nothing the schema describes.</summary>
    Pointer,

    /// <summary>Members at explicit, nonoverlapping offsets inside a fixed extent that may include padding.</summary>
    Struct,

    /// <summary>Members at explicit offsets that deliberately overlap, so each is one interpretation of shared bytes.</summary>
    Union,

    /// <summary>A fixed number of elements of one type, laid out consecutively with the element's size as stride.</summary>
    Array,

    /// <summary>A type whose representation is unavailable; it has identity and size zero and cannot be read by value.</summary>
    Incomplete,

    /// <summary>A type with a real, trustworthy byte size but no usable member layout; readable only as raw bytes.</summary>
    /// <remarks>
    /// Unlike <see cref="Incomplete"/>, a raw-bytes type is a fully legitimate by-value member or array element: a
    /// container that embeds one still places every sibling correctly, because the size <see cref="MemorySchema"/>
    /// checked and kept is real, even though the metadata describing what is inside it did not hold up (an
    /// inconsistent field offset, a bitfield that does not fit its storage, and similar defects in the source
    /// metadata rather than in this library). <see cref="MemorySession"/> reads and writes it as an untyped byte
    /// block (a <c>byte[]</c>) the same size as the original, unreadable type; it has no fields and cannot be
    /// traversed further. Best-effort schema construction and metadata import create these placeholders.
    /// </remarks>
    RawBytes,
}
