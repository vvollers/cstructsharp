namespace CStructSharp.Memory;

/// <summary>The exact bits of a pointer as found in memory, together with their width. Reading one never follows it.</summary>
/// <remarks>
/// <para>
/// A pointer field is just bits until a format's rules say what they mean: an absolute virtual address, an offset
/// from the containing object, an address with flag bits in its low end, or an address in some other process.
/// Keeping storage and interpretation apart lets a record be read, displayed, and written back without ever
/// decoding the pointer wrongly. <see cref="MemorySession"/> interprets the bits only when a path asks for a
/// target with <c>.value</c>, through the session's resolver, and the record itself is unchanged.
/// </para>
/// <para>
/// <see cref="Width"/> is in bytes and must match the schema's declared pointer width when the value is written.
/// A value of zero is null, even if the address space happens to have readable bytes at address zero; this is a
/// deliberate rule of the API.
/// </para>
/// </remarks>
public sealed record StoredPointer
{
    /// <summary>Creates a pointer value, checking that the bits fit the declared width.</summary>
    /// <param name="bits">The stored unsigned bits, exactly as read.</param>
    /// <param name="width">Storage width in bytes: 1, 2, 4, or 8.</param>
    public StoredPointer(ulong bits, int width = 8)
    {
        if (width is not (1 or 2 or 4 or 8) || (width < 8 && bits >= (1UL << (width * 8))))
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Pointer bits must fit a width of 1, 2, 4, or 8 bytes.");
        }

        this.Bits = bits;
        this.Width = width;
    }

    /// <summary>Gets the stored unsigned bits, uninterpreted.</summary>
    public ulong Bits { get; }

    /// <summary>Gets the storage width in bytes.</summary>
    public int Width { get; }

    /// <summary>Gets whether the stored bits are zero, which the API treats as null.</summary>
    public bool IsNull => this.Bits == 0;
}
