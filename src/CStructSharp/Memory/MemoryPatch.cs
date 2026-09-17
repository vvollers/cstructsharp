namespace CStructSharp.Memory;

/// <summary>A prepared, immutable write: the logical range to change and the physical fragments, with expected and replacement bytes, that realize it.</summary>
/// <remarks>
/// <para>
/// A patch separates deciding what to write from writing it. Planning (<see cref="Create"/>, or
/// <see cref="MemorySession.PlanUpdate"/> which calls it) resolves every known mapping layer into the final
/// physical fragments, records each fragment's current bytes and its source's generation, and stages the
/// replacement bytes. Nothing is modified. An editor can show those fragments to a user before
/// <see cref="Commit"/> performs the writes.
/// </para>
/// <para>
/// Two safety rules follow from the design. Physical overlap between fragments is rejected, so a logical edit
/// cannot write the same file bytes twice through aliased mappings. And commit re-validates every fragment
/// (writable source, unchanged generation, unchanged expected bytes) before its first write, so a patch prepared
/// against an image that has since changed fails with <see cref="MemoryFailure.StaleSource"/> rather than
/// corrupting it.
/// </para>
/// <para>
/// What a patch cannot promise is atomicity. Arbitrary sources share no transaction or lock, so a later fragment
/// can fail after earlier ones succeeded, and the failing write may be partial. <see cref="Commit"/> reports that
/// state precisely through <see cref="MemoryPatchCommitException"/> and never claims rollback. When the original
/// must survive, commit into an <see cref="OverlayMemorySource"/>.
/// </para>
/// </remarks>
public sealed class MemoryPatch
{
    /// <summary>Stores the logical range and its already-validated fragments.</summary>
    /// <param name="logicalRegion">The range the caller asked to change.</param>
    /// <param name="fragments">Physical fragments in logical order.</param>
    private MemoryPatch(MemoryRegion logicalRegion, IReadOnlyList<MemoryPatchFragment> fragments)
    {
        this.LogicalRegion = logicalRegion;
        this.Fragments = fragments;
    }

    /// <summary>Gets the logical range the patch changes, before mapping layers were resolved.</summary>
    public MemoryRegion LogicalRegion { get; }

    /// <summary>Gets the physical fragments in logical order; concatenating their replacements reproduces the logical replacement.</summary>
    public IReadOnlyList<MemoryPatchFragment> Fragments { get; }

    /// <summary>Plans a byte-for-byte replacement of a region, flattening mappings and snapshotting each fragment's current bytes and generation.</summary>
    /// <remarks>
    /// The replacement must be exactly as long as the region. When <paramref name="expected"/> is supplied, each
    /// fragment's current bytes are also compared with it, so a caller can assert that the bytes it read earlier
    /// are still there. Planning does not require a writable source: previewing an edit against a read-only capture
    /// is valid, and the writable check is deferred to <see cref="Commit"/>.
    /// </remarks>
    /// <param name="region">Finite caller-owned region to replace.</param>
    /// <param name="replacement">New bytes, exactly <c>region.Length</c> of them.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <param name="expected">Bytes the caller believes the region currently holds, or null to skip that check.</param>
    /// <returns>An immutable patch describing the fragments and their bytes.</returns>
    public static MemoryPatch Create(MemoryRegion region, ReadOnlySpan<byte> replacement, MemoryAccessContext? context = null, byte[]? expected = null)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (region.Length != replacement.Length || (expected is not null && expected.Length != replacement.Length))
        {
            throw new ArgumentException("Patch bytes must exactly match the selected extent.");
        }

        context ??= new MemoryAccessContext();
        var regions = new List<MemoryRegion>();
        Flatten(region, regions, context, 0);
        ValidateDistinctRanges(regions);
        var fragments = new List<MemoryPatchFragment>();
        int offset = 0;
        foreach (MemoryRegion fragment in regions)
        {
            // Sample the generation before reading and compare after, so bytes read across a change are rejected.
            long generation = fragment.Source.Generation;
            byte[] original = MemorySession.ReadBytes(fragment, context);
            if (generation != fragment.Source.Generation || (expected is not null && !original.AsSpan().SequenceEqual(expected.AsSpan(offset, original.Length))))
            {
                throw new MemoryAccessException(MemoryFailure.StaleSource, fragment.Source.Id, fragment.Address, original.Length, "Source changed while planning the patch.");
            }

            fragments.Add(new MemoryPatchFragment(fragment, original, replacement.Slice(offset, original.Length).ToArray(), generation));
            offset += original.Length;
        }

        return new MemoryPatch(region, fragments.AsReadOnly());
    }

    /// <summary>Re-validates every fragment, then writes them in order; a failure during writing reports what completed and what is uncertain.</summary>
    /// <remarks>
    /// <para>
    /// Validation runs over all fragments before any write, so a patch that is entirely stale fails without
    /// touching the image. Each fragment must have a source that implements <see cref="IWritableMemorySource"/>,
    /// a generation equal to the one captured at planning, and current bytes equal to its expected bytes. The
    /// generation is checked both before and after the comparison read, so a change during that read is caught.
    /// </para>
    /// <para>
    /// No lock spans arbitrary sources, so a mutation between validation and writing remains possible. If a write
    /// throws, the exception is wrapped in <see cref="MemoryPatchCommitException"/> with the byte count of the
    /// fragments confirmed before it and the index of the fragment whose completion is uncertain. Do not retry
    /// automatically or assume the original bytes were restored. Passing the planning context here shares one
    /// budget across planning and commit; a fresh context starts a new one.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">A fragment's source does not implement <see cref="IWritableMemorySource"/>.</exception>
    /// <exception cref="MemoryAccessException">Pre-write validation found unavailable bytes, a stale source, or an exhausted budget.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was observed before the write phase.</exception>
    /// <exception cref="MemoryPatchCommitException">A fragment write failed; earlier writes completed and the failing one may be partial.</exception>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    public void Commit(MemoryAccessContext? context = null)
    {
        context ??= new MemoryAccessContext();
        foreach (MemoryPatchFragment fragment in this.Fragments)
        {
            if (fragment.Region.Source is not IWritableMemorySource)
            {
                throw new NotSupportedException($"Source '{fragment.Region.Source.Id}' is read-only.");
            }

            if (fragment.Generation != fragment.Region.Source.Generation || !MemorySession.ReadBytes(fragment.Region, context).AsSpan().SequenceEqual(fragment.ExpectedBytes) || fragment.Generation != fragment.Region.Source.Generation)
            {
                throw new MemoryAccessException(MemoryFailure.StaleSource, fragment.Region.Source.Id, fragment.Region.Address, fragment.ExpectedBytes.Length, "Patch expectation no longer matches the source.");
            }
        }

        // From here on bytes change; a failure is reported as a commit failure with the confirmed progress.
        context.CancellationToken.ThrowIfCancellationRequested();
        long completed = 0;
        for (int i = 0; i < this.Fragments.Count; i++)
        {
            MemoryPatchFragment fragment = this.Fragments[i];
            try
            {
                ((IWritableMemorySource)fragment.Region.Source).Write(fragment.Region.Address, fragment.ReplacementBytes, context);
                completed += fragment.ReplacementBytes.Length;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException)
            {
                throw new MemoryPatchCommitException(completed, i, exception);
            }
        }
    }

    /// <summary>Resolves a region through every <see cref="MappedMemorySource"/> layer into final-source fragments, in logical order.</summary>
    /// <remarks>Only <see cref="MappedMemorySource"/> exposes a translation table, so only it is descended into;
    /// every other source, including caches, overlays, and custom adapters, is a terminal coordinate even if it
    /// delegates internally. This is why an overlay placed beneath the mappings yields image offsets in a patch,
    /// while one placed above them yields the overlay's logical coordinates.</remarks>
    /// <param name="region">Region to resolve.</param>
    /// <param name="output">Receives the terminal fragments in order.</param>
    /// <param name="context">Shared budget charged for each mapping lookup and depth level.</param>
    /// <param name="depth">Current layer depth, bounded by the context.</param>
    internal static void Flatten(MemoryRegion region, List<MemoryRegion> output, MemoryAccessContext context, int depth)
    {
        context.CheckDepth(depth);
        if (region.Length > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(region));
        }

        if (region.Source is MappedMemorySource mapped)
        {
            foreach (MemoryRegion fragment in mapped.Describe(region.Address, (int)region.Length, context))
            {
                Flatten(fragment, output, context, depth + 1);
            }
        }
        else
        {
            output.Add(region);
        }
    }

    /// <summary>Rejects fragments that overlap within one source, so a patch cannot write the same physical bytes twice.</summary>
    /// <remarks>Aliased mappings can legitimately expose the same backing bytes at two logical addresses. A logical
    /// edit spanning both would produce two fragments over the same bytes with possibly different replacements;
    /// the outcome would depend on write order, so such a patch is refused while planning.</remarks>
    /// <param name="regions">Terminal fragments produced by <see cref="Flatten"/>.</param>
    private static void ValidateDistinctRanges(List<MemoryRegion> regions)
    {
        var bySource = new Dictionary<IMemorySource, List<MemoryRegion>>(ReferenceEqualityComparer.Instance);
        foreach (MemoryRegion region in regions)
        {
            if (!bySource.TryGetValue(region.Source, out List<MemoryRegion>? ranges))
            {
                ranges = new List<MemoryRegion>();
                bySource.Add(region.Source, ranges);
            }

            ranges.Add(region);
        }

        foreach (List<MemoryRegion> ranges in bySource.Values)
        {
            // Sorting allows bounded overlap validation even for many discontiguous pages.
            ranges.Sort((left, right) => left.Address.CompareTo(right.Address));
            for (int index = 1; index < ranges.Count; index++)
            {
                if (ranges[index].Address - ranges[index - 1].Address < (ulong)ranges[index - 1].Length)
                {
                    throw new ArgumentException("A patch cannot contain overlapping physical ranges.");
                }
            }
        }
    }
}
