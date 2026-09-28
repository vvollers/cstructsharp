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
        ValidateRange(address, length);
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

    /// <summary>Rejects a negative length or a range that would wrap past the end of the unsigned address space.</summary>
    /// <remarks>This is the constructor's range rule. Sources that compute addresses inside a requested range call
    /// it directly, so a request that could not be a region fails before any address arithmetic overflows.</remarks>
    /// <param name="address">Unsigned address of the first byte.</param>
    /// <param name="length">Byte extent; zero is allowed at any address.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="length"/> is negative, or the range wraps past <see cref="ulong.MaxValue"/>.</exception>
    internal static void ValidateRange(ulong address, long length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);

        // The last byte is at address + length - 1; compare against the remaining space instead of adding, so a
        // region ending exactly at ulong.MaxValue is accepted and one byte further is rejected without overflow.
        if (length > 0 && (ulong)(length - 1) > ulong.MaxValue - address)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Region overflows the unsigned address space.");
        }
    }

    /// <summary>Copies the whole region into a new array, failing rather than padding if the source ends early.</summary>
    /// <remarks>The region must fit an <see cref="int"/>-sized allocation and the byte budget. Positive short reads
    /// continue, but a premature zero is <see cref="MemoryFailure.MissingBytes"/>, so absent bytes can never become
    /// default zeroes in the returned array.</remarks>
    /// <param name="context">Shared budget charged by every underlying source read.</param>
    /// <returns>An owned array containing exactly the region's bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The region is longer than an array can hold.</exception>
    /// <exception cref="MemoryAccessException">The region exceeds the byte budget, or its bytes are unavailable.</exception>
    internal byte[] ReadAll(MemoryAccessContext context)
    {
        if (this.Length > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException("region", "Region is too large for a materialized value.");
        }

        if (this.Length > context.MaxTotalBytes)
        {
            throw new MemoryAccessException(MemoryFailure.BudgetExceeded, this.Source.Id, this.Address, (int)this.Length, "Region exceeds the byte budget.");
        }

        var bytes = new byte[(int)this.Length];
        this.ReadExactly(bytes, context);
        return bytes;
    }

    /// <summary>Proves that the source can supply every byte of the region, with the checks and failures of <see cref="ReadAll"/>.</summary>
    /// <remarks>The bytes are read and discarded: only a read can show that a source has no hole in the range.</remarks>
    /// <param name="context">Shared budget charged by every underlying source read.</param>
    /// <exception cref="ArgumentOutOfRangeException">The region is longer than an array can hold.</exception>
    /// <exception cref="MemoryAccessException">The region exceeds the byte budget, or its bytes are unavailable.</exception>
    internal void EnsureReadable(MemoryAccessContext context)
    {
        this.ReadAll(context);
    }

    /// <summary>
    ///     Fills <paramref name="destination"/> from the start of the region: a short read continues, and a read of
    ///     zero bytes inside the region is <see cref="MemoryFailure.MissingBytes"/>, never padding.
    /// </summary>
    /// <param name="destination">The bytes to fill; it must not be longer than the region.</param>
    /// <param name="context">Shared budget charged by every underlying source read, and the cancellation token.</param>
    /// <exception cref="MemoryAccessException">The source returned an invalid count or no bytes.</exception>
    /// <exception cref="OperationCanceledException">The context's token was cancelled between reads.</exception>
    internal void ReadExactly(Span<byte> destination, MemoryAccessContext context)
    {
        for (int offset = 0; offset < destination.Length;)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            offset += this.ReadAt(offset, destination[offset..], context);
        }
    }

    /// <summary>Performs one source read at a local offset and checks the count the source returned.</summary>
    /// <remarks>This is the single read step of both <see cref="ReadExactly"/> and the region's stream view. Inside
    /// the region a zero count is not end of file: the bytes were never captured.</remarks>
    /// <param name="offset">Byte offset from the region's start; the caller keeps the read inside the region.</param>
    /// <param name="destination">Non-empty destination, no longer than the region's remaining bytes.</param>
    /// <param name="context">Shared budget charged by the source read.</param>
    /// <returns>The number of bytes read, between one and the destination length.</returns>
    /// <exception cref="MemoryAccessException">The source returned an invalid count or no bytes.</exception>
    internal int ReadAt(long offset, Span<byte> destination, MemoryAccessContext context)
    {
        ulong address = checked(this.Address + (ulong)offset);
        int read = this.Source.Read(address, destination, context);
        MemorySourceChecks.ThrowIfInvalidReadCount(read, destination.Length, this.Source.Id, address);
        MemorySourceChecks.ThrowIfUnavailable(read, destination.Length, this.Source.Id, address);
        return read;
    }

    /// <summary>Resolves the region through every <see cref="MappedMemorySource"/> layer into final-source fragments, in logical order.</summary>
    /// <remarks>Only <see cref="MappedMemorySource"/> exposes a translation table, so only it is descended into;
    /// every other source, including caches, overlays, and custom adapters, is a terminal coordinate even if it
    /// delegates internally. This is why an overlay placed beneath the mappings yields image offsets in a patch,
    /// while one placed above them yields the overlay's logical coordinates.</remarks>
    /// <param name="context">Shared budget charged for each mapping lookup and depth level.</param>
    /// <returns>A new list of the terminal fragments; a region over an unmapped source is its only element.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A region along the way is longer than <see cref="int.MaxValue"/> bytes.</exception>
    /// <exception cref="MemoryAccessException">A mapping layer has a hole in the range, or the budget or depth limit is exceeded.</exception>
    internal List<MemoryRegion> FlattenMappings(MemoryAccessContext context)
    {
        var fragments = new List<MemoryRegion>();
        this.FlattenMappings(fragments, context, 0);
        return fragments;
    }

    /// <summary>Appends this region's terminal fragments to <paramref name="output"/>, descending one mapping layer per level.</summary>
    /// <param name="output">Receives the terminal fragments in order.</param>
    /// <param name="context">Shared budget charged for each mapping lookup and depth level.</param>
    /// <param name="depth">Current layer depth, checked against <see cref="MemoryAccessContext.MaxNestingDepth"/>.</param>
    private void FlattenMappings(List<MemoryRegion> output, MemoryAccessContext context, int depth)
    {
        context.CheckNestingDepth(depth);
        if (this.Length > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException("region");
        }

        if (this.Source is MappedMemorySource mapped)
        {
            foreach (MemoryRegion fragment in mapped.Describe(this.Address, (int)this.Length, context))
            {
                fragment.FlattenMappings(output, context, depth + 1);
            }
        }
        else
        {
            output.Add(this);
        }
    }
}
