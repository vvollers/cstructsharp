namespace CStructSharp.Memory;

/// <summary>One translation rule: logical addresses starting at <see cref="Address"/> come from the bytes of <see cref="Backing"/>.</summary>
/// <remarks>
/// <para>
/// Operating systems manage memory in pages, and a capture stores those pages wherever it likes. A mapping records
/// one such placement: the byte at logical <c>Address + i</c> is the byte at <c>Backing.Address + i</c> in
/// <c>Backing.Source</c>, for every <c>i</c> below <c>Backing.Length</c>. A table of these rules is what lets
/// <see cref="MappedMemorySource"/> present scattered file ranges as one contiguous address space.
/// </para>
/// <para>
/// A mapping describes placement only. It does not read bytes, infer a page size, or take ownership of the backing
/// source, and a single mapping knows nothing about its neighbors; <see cref="MappedMemorySource"/> validates the
/// table as a whole, including the rule that logical ranges must not overlap.
/// </para>
/// </remarks>
public sealed record MemoryMapping
{
    /// <summary>Creates a nonempty mapping and checks that the logical range does not wrap past the end of the address space.</summary>
    /// <param name="address">First logical address covered by this mapping.</param>
    /// <param name="backing">Caller-owned backing range; its length is also the mapping's logical length.</param>
    public MemoryMapping(ulong address, MemoryRegion backing)
    {
        ArgumentNullException.ThrowIfNull(backing);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(backing.Length);
        if ((ulong)(backing.Length - 1) > ulong.MaxValue - address)
        {
            throw new ArgumentOutOfRangeException(nameof(address));
        }

        this.Address = address;
        this.Backing = backing;
    }

    /// <summary>Gets the first logical address covered by this mapping.</summary>
    public ulong Address { get; }

    /// <summary>Gets the caller-owned backing range that supplies the bytes.</summary>
    public MemoryRegion Backing { get; }
}
