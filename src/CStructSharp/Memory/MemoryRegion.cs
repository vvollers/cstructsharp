namespace CStructSharp.Memory;

/// <summary>A finite byte range in one source: the source object, an unsigned start address, and a length in bytes.</summary>
/// <remarks>
/// <para>
/// A region is the unit every session, walker, and patch operates on. It answers "which bytes may this operation
/// touch?" and nothing more. It does not copy bytes, and it is not proof that the bytes exist: reading through the
/// region still consults the source, which may report a hole or changed data.
/// </para>
/// <para>
/// The address is unsigned so that high 64-bit process addresses such as 0xffff800000000000 can be represented,
/// while the length is a signed <see cref="long"/> because a single operation must always be finite and must fit
/// in an allocation. Both constructor and <see cref="Slice"/> check that the range does not wrap around the end of
/// the address space, including the edge case where the final byte sits at <see cref="ulong.MaxValue"/>.
/// </para>
/// <para>
/// <see cref="OpenRead"/> is the bridge to the ordinary stream-based CStructSharp API: it exposes the region as a
/// stream whose position zero is the region's start. The bridge does not rewrite any pointer bits stored in the
/// bytes; stored process addresses remain meaningless as stream positions, and only <see cref="MemorySession"/>
/// interprets them.
/// </para>
/// </remarks>
public sealed record MemoryRegion
{
    /// <summary>Creates a region and rejects ranges that would wrap past the end of the unsigned address space.</summary>
    /// <param name="source">Caller-owned address space; the region does not take ownership or dispose it.</param>
    /// <param name="address">Unsigned starting coordinate in <paramref name="source"/>.</param>
    /// <param name="length">Finite byte extent; zero is allowed.</param>
    public MemoryRegion(IMemorySource source, ulong address, long length)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        // The last byte is at address + length - 1; compare against the remaining space instead of adding, so a
        // region ending exactly at ulong.MaxValue is accepted and one byte further is rejected without overflow.
        if (length > 0 && (ulong)(length - 1) > ulong.MaxValue - address)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Region overflows the unsigned address space.");
        }

        this.Source = source;
        this.Address = address;
        this.Length = length;
    }

    /// <summary>Gets the caller-owned source whose coordinate system <see cref="Address"/> belongs to.</summary>
    public IMemorySource Source { get; }

    /// <summary>Gets the unsigned address of the first byte.</summary>
    public ulong Address { get; }

    /// <summary>Gets the number of bytes in the region.</summary>
    public long Length { get; }

    /// <summary>Creates a subregion that must lie entirely within this region.</summary>
    /// <remarks>Offsets are relative to this region's start, which is how a schema's member offsets become
    /// addresses: a field at offset 4 of a record region is <c>record.Slice(4, size)</c>. An empty slice is allowed
    /// at any offset up to and including <see cref="Length"/>.</remarks>
    /// <param name="offset">Byte offset from the start of this region.</param>
    /// <param name="length">Finite byte extent of the subregion.</param>
    /// <returns>A validated subregion in the same source.</returns>
    public MemoryRegion Slice(long offset, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        if (offset > this.Length || length > this.Length - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return new MemoryRegion(this.Source, checked(this.Address + (ulong)offset), length);
    }

    /// <summary>Opens a read-only, seekable stream over this region whose position zero is <see cref="Address"/>.</summary>
    /// <remarks>Use this to feed the core <see cref="CStruct"/> reader, for example to parse a runtime-sized layout
    /// that must stop at the region's end. The view cannot write, resize the source, or seek outside the region.
    /// Disposing it closes only the view; the source and any caller-owned file stream stay open. Core stream pointer
    /// rules still apply to pointers parsed through this view; use <see cref="MemorySession"/> for unsigned
    /// source-address resolution.</remarks>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>A non-owning finite stream positioned at zero.</returns>
    public Stream OpenRead(MemoryAccessContext? context = null)
    {
        return new MemoryRegionStream(this, context ?? new MemoryAccessContext());
    }
}
