namespace CStructSharp.Memory;

/// <summary>One member of a struct or union: a name, a type ID, a byte offset from the container's start, and an optional bit slice.</summary>
/// <remarks>
/// <para>
/// A field is the link between a containing composite and the type stored inside it. The offset is relative to the
/// start of the containing record, never an absolute address; the session adds it to the record's region when a
/// path selects the member. The referenced type supplies the storage size.
/// </para>
/// <para>
/// A bit slice describes a C bitfield: the field occupies only <see cref="BitWidth"/> bits of its storage integer,
/// starting at <see cref="BitOffset"/> counted from the least significant bit. Bits are numbered after the storage
/// integer has been assembled from its bytes, so switching byte order changes which byte holds the slice but not
/// the meaning of bit index zero. A signed slice is sign-extended on read and range-checked on write, using
/// two's complement.
/// </para>
/// <para>
/// A promoted field makes the members of an anonymous nested struct or union visible in the parent, mirroring C's
/// anonymous members. If two promoted members share a name the schema rejects the ambiguity instead of picking one.
/// Extent and overlap checks belong to <see cref="MemorySchema"/>, which sees the whole graph.
/// </para>
/// </remarks>
public sealed record MemoryField
{
    /// <summary>Creates a member description; type references, extents, and overlap are validated later by the schema.</summary>
    /// <param name="name">Member name used in session paths and struct value dictionaries.</param>
    /// <param name="typeId">ID of the member's type in the schema.</param>
    /// <param name="offset">Byte offset from the start of the containing struct or union, not a source address.</param>
    /// <param name="bitOffset">Index of the slice's least significant bit within the storage integer, or null for a whole value.</param>
    /// <param name="bitWidth">Number of bits in the slice, or null for a whole value; must accompany <paramref name="bitOffset"/>.</param>
    /// <param name="signed">Whether the bit slice is a two's-complement signed number.</param>
    /// <param name="promoted">Whether the members of this composite are visible in the containing record.</param>
    public MemoryField(string name, string typeId, int offset, int? bitOffset = null, int? bitWidth = null, bool signed = false, bool promoted = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeId);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (bitOffset.HasValue != bitWidth.HasValue || bitOffset < 0 || bitWidth <= 0)
        {
            throw new ArgumentException("A bit slice needs a nonnegative bit offset and positive width.");
        }

        this.Name = name;
        this.TypeId = typeId;
        this.Offset = offset;
        this.BitOffset = bitOffset;
        this.BitWidth = bitWidth;
        this.Signed = signed;
        this.Promoted = promoted;
    }

    /// <summary>Gets the member name used for path lookup and value dictionaries.</summary>
    public string Name { get; }

    /// <summary>Gets the ID of the member's type.</summary>
    public string TypeId { get; }

    /// <summary>Gets the byte offset from the start of the containing record.</summary>
    public int Offset { get; }

    /// <summary>Gets the index of the slice's least significant bit within the storage integer, or null for a whole value.</summary>
    public int? BitOffset { get; }

    /// <summary>Gets the number of bits in the slice, or null for a whole value.</summary>
    public int? BitWidth { get; }

    /// <summary>Gets whether the bit slice is read and written as a two's-complement signed number.</summary>
    public bool Signed { get; }

    /// <summary>Gets whether the members of this composite are visible in its containing record.</summary>
    public bool Promoted { get; }
}
