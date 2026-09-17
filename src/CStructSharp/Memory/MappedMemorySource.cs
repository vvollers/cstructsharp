namespace CStructSharp.Memory;

/// <summary>An address space assembled from a table of mappings, each translating a logical range to a range of another source.</summary>
/// <remarks>
/// <para>
/// This is the type that turns a scattered capture into something that looks like a process. A process sees
/// consecutive pages; the capture stores them at arbitrary offsets, in one file or several. Each
/// <see cref="MemoryMapping"/> records one placement, and this source answers a read by finding the mapping that
/// contains the address, asking the backing source for as many bytes as that mapping can supply, and continuing
/// with the next mapping until the request is complete. A read that crosses a boundary is therefore split and
/// reassembled in logical order without the caller noticing.
/// </para>
/// <para>
/// The table is sorted once at construction so each lookup is a binary search. Logical ranges must not overlap,
/// because an overlapping table would make translation ambiguous. Several logical ranges may point at the same
/// backing bytes (aliasing, as when two processes share a library page); reads permit it, and
/// <see cref="MemoryPatch"/> separately rejects overlapping physical fragments within one write plan. Backing
/// regions may themselves belong to another mapped source, forming layers that <see cref="MemoryPatch"/> and
/// <see cref="MemorySession.Inspect"/> flatten when they report physical coordinates.
/// </para>
/// <para>
/// Missing intervals are errors (<see cref="MemoryFailure.Unmapped"/>), never zero bytes. The source does not
/// decode values, change byte order, discover hardware page tables, or take ownership of its backing sources.
/// </para>
/// </remarks>
public sealed class MappedMemorySource : IMemorySource
{
    private readonly MemoryMapping[] mappings;
    private readonly IMemorySource[] sources;
    private readonly long[] sourceGenerations;
    private readonly object generationGate = new();
    private long generation;

    /// <summary>Snapshots and sorts the mapping table and rejects logical overlap.</summary>
    /// <param name="id">Diagnostic label; source objects, not labels, distinguish address spaces.</param>
    /// <param name="mappings">Mappings with nonoverlapping logical ranges, copied at construction.</param>
    public MappedMemorySource(string id, IEnumerable<MemoryMapping> mappings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(mappings);
        this.Id = id;

        // Order once so each address lookup is logarithmic and deterministic.
        this.mappings = mappings.OrderBy(mapping => mapping.Address).ToArray();
        for (int i = 1; i < this.mappings.Length; i++)
        {
            // After sorting, overlap can only occur between neighbors: the next start must be at or past the previous end.
            MemoryMapping previous = this.mappings[i - 1];
            if (this.mappings[i].Address - previous.Address < (ulong)previous.Backing.Length)
            {
                throw new ArgumentException("Mappings overlap.", nameof(mappings));
            }
        }

        // Track each distinct backing source once: summing per-page generation numbers could overflow for valid snapshot IDs.
        this.sources = this.mappings.Select(mapping => mapping.Backing.Source).Distinct<IMemorySource>(ReferenceEqualityComparer.Instance).ToArray();
        this.sourceGenerations = new long[this.sources.Length];
        for (int index = 0; index < this.sources.Length; index++)
        {
            this.sourceGenerations[index] = this.sources[index].Generation;
        }
    }

    /// <summary>Gets the diagnostic label for this source.</summary>
    public string Id { get; }

    /// <summary>Gets a local counter that advances whenever any distinct backing source reports a changed generation.</summary>
    /// <remarks>The mapped source has no bytes of its own, so its generation is derived: each read of this property
    /// compares every backing source's current generation with the last one observed and bumps the local counter
    /// once per observed change. Backing generation numbers are compared, not summed, so unrelated snapshot labels
    /// cannot collide or overflow.</remarks>
    public long Generation
    {
        get
        {
            lock (this.generationGate)
            {
                for (int index = 0; index < this.sources.Length; index++)
                {
                    long observed = this.sources[index].Generation;
                    if (observed != this.sourceGenerations[index])
                    {
                        this.generation = checked(this.generation + 1);
                        this.sourceGenerations[index] = observed;
                    }
                }

                return this.generation;
            }
        }
    }

    /// <summary>Translates one logical range into its backing fragments, in logical order, without reading any bytes.</summary>
    /// <remarks>This describes one mapping layer only. A returned fragment can itself belong to another mapped
    /// source; session inspection and patch planning follow those layers recursively. A hole anywhere in the range
    /// throws before a partial description is returned. Describing a range proves that the table covers it, not
    /// that a backing file will supply the bytes at read time.</remarks>
    /// <param name="address">First logical address to translate.</param>
    /// <param name="length">Number of logical bytes to translate.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>Backing fragments covering the range, in logical order.</returns>
    public IReadOnlyList<MemoryRegion> Describe(ulong address, int length, MemoryAccessContext? context = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        _ = new MemoryRegion(this, address, length);
        context ??= new MemoryAccessContext();
        var regions = new List<MemoryRegion>();
        int done = 0;
        while (done < length)
        {
            ulong current = checked(address + (ulong)done);
            MemoryMapping mapping = this.Find(current, context);
            long offset = checked((long)(current - mapping.Address));
            int count = (int)Math.Min(length - done, mapping.Backing.Length - offset);
            regions.Add(mapping.Backing.Slice(offset, count));
            done += count;
        }

        return regions.AsReadOnly();
    }

    /// <inheritdoc/>
    public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
    {
        _ = new MemoryRegion(this, address, destination.Length);
        context.EnterSource();
        try
        {
            return this.ReadMapped(address, destination, context);
        }
        finally
        {
            context.ExitSource();
        }
    }

    /// <summary>Fills the destination from successive mappings, advancing by the bytes each backing read actually returned.</summary>
    /// <remarks>Two different events can end a backing read early, and they must be told apart. Reaching the end of
    /// a mapping is normal: the loop looks up the next mapping and continues. A backing source returning fewer bytes
    /// than asked is a short read: the loop asks again from the next byte. A backing source returning zero before
    /// the mapping is exhausted means the capture is missing data, which is an error. A failure part-way through may
    /// leave a prefix of the destination filled.</remarks>
    private int ReadMapped(ulong address, Span<byte> destination, MemoryAccessContext context)
    {
        int done = 0;
        while (done < destination.Length)
        {
            ulong current = checked(address + (ulong)done);
            MemoryMapping mapping = this.Find(current, context);
            long offset = checked((long)(current - mapping.Address));

            // Ask for no more than this mapping can supply; the next iteration handles the remainder.
            int count = (int)Math.Min(destination.Length - done, mapping.Backing.Length - offset);
            int read = mapping.Backing.Source.Read(checked(mapping.Backing.Address + (ulong)offset), destination.Slice(done, count), context);
            if (read < 0 || read > count)
            {
                throw new MemoryAccessException(MemoryFailure.SourceFailure, this.Id, current, count, "Backing source returned an invalid read count.");
            }

            if (read == 0)
            {
                throw new MemoryAccessException(MemoryFailure.MissingBytes, this.Id, current, count, "Mapped backing bytes are unavailable.");
            }

            done += read;
        }

        return done;
    }

    /// <summary>Binary-searches the sorted table for the mapping containing <paramref name="address"/>, charging one lookup request.</summary>
    /// <remarks>The lookup is charged even when it fails, so an address-guessing loop over unmapped space still
    /// consumes budget. A miss throws <see cref="MemoryFailure.Unmapped"/> naming the address.</remarks>
    private MemoryMapping Find(ulong address, MemoryAccessContext context)
    {
        context.Charge(this.Id, address, 0);
        int low = 0;
        int high = this.mappings.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            MemoryMapping mapping = this.mappings[middle];
            if (address < mapping.Address)
            {
                high = middle - 1;
            }
            else if (address - mapping.Address >= (ulong)mapping.Backing.Length)
            {
                low = middle + 1;
            }
            else
            {
                return mapping;
            }
        }

        throw new MemoryAccessException(MemoryFailure.Unmapped, this.Id, address, 1, "No mapping contains the address.");
    }
}
