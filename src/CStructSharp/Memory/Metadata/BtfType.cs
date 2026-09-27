namespace CStructSharp.Memory.Metadata;

/// <summary>One parsed BTF type record: its ID, name, kind, size-or-reference word, flag bit, payload words, and the table that owns its strings.</summary>
/// <param name="Id">Numeric type ID, unique across the base chain.</param>
/// <param name="Name">Display name, possibly empty.</param>
/// <param name="Kind">The record's kind.</param>
/// <param name="Size">Byte size for sized kinds; the referenced type ID for pointers and modifiers.</param>
/// <param name="Flag">The kind-specific flag bit of the info word.</param>
/// <param name="Payload">Kind-specific payload words following the fixed part.</param>
/// <param name="Owner">The table whose string section the record's member names refer to.</param>
internal sealed record BtfType(uint Id, string Name, BtfKind Kind, uint Size, bool Flag, uint[] Payload, BtfMetadata Owner)
{
    /// <summary>Whether the kind only qualifies or annotates another type (typedef, const, volatile, restrict, and tags) and points onward through its size word.</summary>
    public bool IsModifier => this.Kind is BtfKind.Typedef or BtfKind.Volatile or BtfKind.Const or BtfKind.Restrict or BtfKind.DeclTag or BtfKind.TypeTag;

    /// <summary>Whether the kind has no value representation (void, a forward declaration, a function or its prototype), so it can only be pointed at.</summary>
    public bool IsIncomplete => this.Kind is BtfKind.Void or BtfKind.Fwd or BtfKind.Func or BtfKind.FuncProto;

    /// <summary>Whether an INT record's encoding marks it signed (bit 24 of its payload word).</summary>
    public bool IsSignedInt => this.Kind == BtfKind.Int && (this.Payload[0] & 0x01000000) != 0;
}
