namespace CStructSharp.Memory.Metadata;

/// <summary>One struct or union member exactly as <see cref="BtfMetadata.Describe"/> reports it, before any of it is imported.</summary>
/// <param name="Name">Member name; a generated <c>__anonymous_N</c> name for a promoted anonymous member, matching what <see cref="BtfMetadata.Import"/> would assign it.</param>
/// <param name="TypeId">The member's declared BTF type ID, exactly as recorded - not resolved through modifiers, and not imported.</param>
/// <param name="Offset">Byte offset from the start of the containing type.</param>
/// <param name="BitOffset">Bit position within the storage unit at <see cref="Offset"/>, or null for a whole-value (non-bitfield) member.</param>
/// <param name="BitWidth">Bitfield width in bits, or null for a whole-value member.</param>
public sealed record BtfMemberDescription(string Name, uint TypeId, int Offset, int? BitOffset, int? BitWidth);
