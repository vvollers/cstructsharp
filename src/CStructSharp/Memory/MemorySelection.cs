namespace CStructSharp.Memory;

/// <summary>The result of resolving a path: which bytes were selected, what type they hold, and which field (and bit slice) named them.</summary>
/// <remarks>
/// <para>
/// A selection is a location, not a value. It is what an address display needs ("field <c>pid</c> of the record at
/// 0x1000 is at 0x1000, four bytes") and what patch planning needs, and it can be produced without decoding the
/// record. For a bitfield the region covers the whole storage unit and <see cref="Field"/> identifies the bits
/// within it. Resolving may already have read intermediate pointers on the way here, but the final selected value
/// is never decoded.
/// </para>
/// <para>
/// <see cref="Container"/> is the record that contains the selected member. It differs from the root region once a
/// path has entered a nested record or followed a pointer, and it is what a relative-pointer resolver uses as its
/// base.
/// </para>
/// </remarks>
/// <param name="Region">Finite logical storage selected by the path.</param>
/// <param name="Type">Type describing the selected bytes.</param>
/// <param name="Field">Member descriptor, including an optional bit slice; null for a root or an array element.</param>
/// <param name="ParentTypeId">ID of the containing record's type, or null for a root.</param>
/// <param name="Container">Region of the containing record, used to interpret relative pointers; null at a root.</param>
public sealed record MemorySelection(MemoryRegion Region, MemoryTypeDefinition Type, MemoryField? Field, string? ParentTypeId, MemoryRegion? Container = null);
