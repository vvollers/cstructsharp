namespace CStructSharp.Memory;

/// <summary>A copy-on-write layer: reads come from an unchanged backing snapshot except where this overlay holds replacement bytes.</summary>
/// <remarks>
/// <para>
/// Editing a capture in place is risky and often impossible (the file may be read-only or gigabytes large). An
/// overlay solves both problems: it forwards every read to the backing source and then substitutes the bytes it
/// has recorded, so the original is never modified and the memory cost is proportional to the edit rather than to
/// the image. Discarding the overlay discards the experiment. This is the recommended target for
/// <see cref="MemoryPatch"/> commits when the original must survive.
/// </para>
/// <para>
/// Changed bytes are kept in a dictionary keyed by address. Only distinct changed addresses count against
/// <c>maxChangedBytes</c>; rewriting an address replaces its entry. A write first verifies that the backing source
/// can supply every byte in the range, so an overlay cannot create bytes where its backing has a hole and cannot
/// extend an image. The backing generation is checked before and after each operation, because an overlay over a
/// snapshot that silently changed would mix two states.
/// </para>
/// <para>
/// Where the overlay sits in a stack matters for previews: below a <see cref="MappedMemorySource"/>, patch
/// fragments identify physical image offsets; above it, they identify logical addresses. Export a chosen finite
/// range through <see cref="MemoryRegion.OpenRead"/>; there is no whole-address-space export.
/// </para>
/// </remarks>
public sealed class OverlayMemorySource : IWritableMemorySource
{
    private readonly IMemorySource backing;
    private readonly long backingGeneration;
    private readonly int maxChangedBytes;
    private readonly Dictionary<ulong, byte> changes = new();
    private readonly object gate = new();
    private long generation;

    /// <summary>Creates an empty overlay over a caller-owned backing source, remembering its current generation as the snapshot.</summary>
    /// <param name="id">Diagnostic label; source objects, not labels, distinguish address spaces.</param>
    /// <param name="backing">Caller-owned snapshot that must not change while the overlay is in use.</param>
    /// <param name="maxChangedBytes">Maximum number of distinct addresses the overlay will retain.</param>
    public OverlayMemorySource(string id, IMemorySource backing, int maxChangedBytes = 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(backing);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxChangedBytes);
        this.Id = id;
        this.backing = backing;
        this.backingGeneration = backing.Generation;
        this.maxChangedBytes = maxChangedBytes;
    }

    /// <summary>Gets the diagnostic label for this source.</summary>
    public string Id { get; }

    /// <summary>Gets the overlay's own mutation counter, advanced by each overlay write; backing stability is checked separately.</summary>
    public long Generation => Interlocked.Read(ref this.generation);

    /// <inheritdoc/>
    public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
    {
        _ = new MemoryRegion(this, address, destination.Length);
        context.Charge(this.Id, address, 0);
        lock (this.gate)
        {
            // Check before and after the backing read: a change during the read would mix two snapshots.
            this.CheckSnapshot(address, destination.Length);
            int read = this.backing.Read(address, destination, context);
            if (read < 0 || read > destination.Length)
            {
                throw new MemoryAccessException(MemoryFailure.SourceFailure, this.Id, address, destination.Length, "Backing source returned an invalid read count.");
            }

            this.CheckSnapshot(address, destination.Length);

            // Substitute recorded bytes over the backing bytes that were actually returned.
            for (int i = 0; i < read; i++)
            {
                if (this.changes.TryGetValue(checked(address + (ulong)i), out byte value))
                {
                    destination[i] = value;
                }
            }

            return read;
        }
    }

    /// <inheritdoc/>
    public void Write(ulong address, ReadOnlySpan<byte> bytes, MemoryAccessContext context)
    {
        _ = new MemoryRegion(this, address, bytes.Length);
        context.Charge(this.Id, address, bytes.Length);
        lock (this.gate)
        {
            this.CheckSnapshot(address, bytes.Length);

            // Only addresses not yet recorded consume capacity; rewriting a recorded address is free.
            int additional = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                if (!this.changes.ContainsKey(checked(address + (ulong)i)))
                {
                    additional++;
                }
            }

            if (additional > this.maxChangedBytes - this.changes.Count)
            {
                throw new MemoryAccessException(MemoryFailure.BudgetExceeded, this.Id, address, bytes.Length, "Overlay capacity exceeded.");
            }

            // Validate all backing bytes before mutating the overlay; holes cannot be synthesized by writes.
            _ = MemorySession.ReadBytes(new MemoryRegion(this.backing, address, bytes.Length), context);
            this.CheckSnapshot(address, bytes.Length);
            for (int i = 0; i < bytes.Length; i++)
            {
                this.changes[checked(address + (ulong)i)] = bytes[i];
            }

            this.generation++;
        }
    }

    /// <summary>Throws <see cref="MemoryFailure.StaleSource"/> if the backing source's generation moved since construction.</summary>
    private void CheckSnapshot(ulong address, int length)
    {
        if (this.backing.Generation != this.backingGeneration)
        {
            throw new MemoryAccessException(MemoryFailure.StaleSource, this.Id, address, length, "Overlay backing snapshot changed.");
        }
    }
}
