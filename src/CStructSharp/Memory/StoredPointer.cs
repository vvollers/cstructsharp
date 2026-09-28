namespace CStructSharp.Memory;

/// <summary>The exact stored value of a pointer as found in memory, together with its width. Reading one never follows it.</summary>
/// <remarks>
/// <para>
/// A pointer field is just a stored number until a format's rules say what it means: an absolute virtual address, an offset
/// from the containing object, an address with flag bits in its low end, or an address in some other process.
/// Keeping storage and interpretation apart lets a record be read, displayed, and written back without ever
/// decoding the pointer wrongly. <see cref="Address"/> is therefore the stored value, as the core's
/// <c>Pointer.Address</c> is; <see cref="MemorySession"/> interprets it only when a path asks for a target with
/// <c>.value</c>, through the session's resolver, and the record itself is unchanged.
/// </para>
/// <para>
/// <see cref="Width"/> is in bytes and must match the schema's declared pointer width when the value is written.
/// A value of zero is null, even if the address space happens to have readable bytes at address zero; this is a
/// deliberate rule of the API.
/// </para>
/// </remarks>
public sealed record StoredPointer
{
    /// <summary>The failure message shared by the width check and the value-fits-width check.</summary>
    private const string WidthMessage = "A pointer's stored value must fit a width of 1, 2, 4, or 8 bytes.";

    /// <summary>Creates a pointer value, checking that the stored value fits the declared width.</summary>
    /// <param name="address">The stored unsigned value, exactly as read.</param>
    /// <param name="width">Storage width in bytes: 1, 2, 4, or 8.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is not 1, 2, 4, or 8, or <paramref name="address"/> does not fit it.</exception>
    public StoredPointer(ulong address, int width = 8)
    {
        ValidateWidth(width);
        if (width < 8 && address >= (1UL << (width * 8)))
        {
            throw new ArgumentOutOfRangeException(nameof(width), WidthMessage);
        }

        this.Address = address;
        this.Width = width;
    }

    /// <summary>Gets the stored unsigned value, uninterpreted: it may carry tag bits or be relative to a base.</summary>
    public ulong Address { get; }

    /// <summary>Gets the storage width in bytes.</summary>
    public int Width { get; }

    /// <summary>Gets whether the stored value is zero, which the API treats as null.</summary>
    public bool IsNull => this.Address == 0;

    /// <summary>Checks that a pointer width is one of the supported storage widths, with the constructor's exception.</summary>
    /// <remarks>Schemas and import options call this to validate a declared pointer width without a pointer value.</remarks>
    /// <param name="width">Storage width in bytes.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> is not 1, 2, 4, or 8.</exception>
    internal static void ValidateWidth(int width)
    {
        if (width is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(width), WidthMessage);
        }
    }
}
