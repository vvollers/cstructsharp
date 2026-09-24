namespace CStructSharp.Memory.Metadata;

/// <summary>One BTF type record's own shape, as <see cref="BtfMetadata.Describe"/> reports it: its kind, size, and (for a struct or union) direct members - without importing or validating any type it refers to.</summary>
/// <param name="Id">The requested type ID (before following any modifier chain).</param>
/// <param name="Name">Display name, possibly empty for an anonymous type; <c>"void"</c> for ID 0.</param>
/// <param name="Kind">The kind storage actually resolves to - modifiers such as <c>typedef</c>/<c>const</c>/<c>volatile</c> are followed transparently, the same way <see cref="BtfMetadata.Import"/> treats them.</param>
/// <param name="Size">Byte size for a sized kind; zero for a kind with no storage of its own (void, forward declarations, functions, function prototypes).</param>
/// <param name="TargetTypeId">For a pointer, the type it points to, or null for an opaque pointer.</param>
/// <param name="ElementTypeId">For an array, its element type; null otherwise.</param>
/// <param name="ElementCount">For an array, its element count; zero otherwise.</param>
/// <param name="Members">For a struct or union, its direct members in declaration order; empty otherwise. Enum values are not described - use <see cref="BtfMetadata.Import"/> for those.</param>
public sealed record BtfTypeDescription(uint Id, string Name, BtfKind Kind, int Size, uint? TargetTypeId, uint? ElementTypeId, int ElementCount, IReadOnlyList<BtfMemberDescription> Members);
