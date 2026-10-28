namespace CStructSharp.Memory;

/// <summary>Immutable selected fixed-field intervals, with explicit elementwise or coalesced capture.</summary>
/// <remarks>Preparation retains schema decisions, never bytes or contexts. The root-relative scratch envelope is explicitly bounded; choose individual field reads for widely separated fields. A missing merged gap does not invalidate independently readable fields. Pointer traversal is deliberately excluded.</remarks>
public sealed class MemoryReadSet
{
    private readonly MemoryByteRange[] intervals;
    private readonly MemoryByteRange[] merged;

    /// <summary>Prepares selected field intervals and a bounded scratch envelope.</summary>
    /// <param name="schema">Exact immutable schema identity.</param>
    /// <param name="typeId">Root record type.</param>
    /// <param name="paths">Fields in elementwise request order, at most 4096.</param>
    /// <param name="maxBufferBytes">Maximum root-relative scratch extent, at most 64 MiB.</param>
    /// <param name="maxGapBytes">Maximum extra gap read only in explicit Coalesced mode.</param>
    /// <exception cref="ArgumentOutOfRangeException">Bounds, field count or envelope are exceeded.</exception>
    /// <exception cref="NotSupportedException">A selected path follows a pointer.</exception>
    public MemoryReadSet(MemorySchema schema, string typeId, IEnumerable<string> paths, int maxBufferBytes = 1024 * 1024, int maxGapBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(paths);
        if (maxBufferBytes is <= 0 or > 64 * 1024 * 1024 || maxGapBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBufferBytes));
        }

        this.Schema = schema;
        var fields = new List<MemoryPath>();
        var intervals = new List<MemoryByteRange>();
        foreach (string path in paths)
        {
            if (fields.Count >= 4096)
            {
                throw new ArgumentOutOfRangeException(nameof(paths));
            }

            MemoryPath selected = schema.Select(typeId, path);
            if (selected.Offset is not { } offset)
            {
                throw new NotSupportedException("A read set requires fixed fields without pointer traversal.");
            }

            if (offset > maxBufferBytes || selected.Type.Size > maxBufferBytes - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBufferBytes), "Selected fields exceed the bounded scratch envelope.");
            }

            fields.Add(selected);
            intervals.Add(new MemoryByteRange((int)offset, selected.Type.Size));
            this.BufferLength = Math.Max(this.BufferLength, (int)offset + selected.Type.Size);
        }

        this.Fields = fields.AsReadOnly();
        this.intervals = intervals.ToArray();
        var merged = new List<MemoryByteRange>();

        // Sorting affects only opt-in transport, never the default field request order.
        foreach (MemoryByteRange interval in intervals.OrderBy(interval => interval.Offset))
        {
            if (merged.Count != 0 && interval.Offset - (long)(merged[^1].Offset + merged[^1].Length) <= maxGapBytes)
            {
                MemoryByteRange previous = merged[^1];
                merged[^1] = new MemoryByteRange(previous.Offset, Math.Max(previous.Offset + previous.Length, interval.Offset + interval.Length) - previous.Offset);
            }
            else
            {
                merged.Add(interval);
            }
        }

        this.merged = merged.ToArray();
    }

    /// <summary>Gets the exact owning schema.</summary>
    public MemorySchema Schema { get; }

    /// <summary>Gets selected paths, offsets and types in requested order for compatibility reporting.</summary>
    public IReadOnlyList<MemoryPath> Fields { get; }

    /// <summary>Gets the bounded owned scratch extent in bytes, including invalid gaps.</summary>
    public int BufferLength { get; }

    /// <summary>Captures selected bytes with independent validity and original logical coordinates.</summary>
    /// <param name="region">Permitted storage beginning at the root. Only the selected envelope is required.</param>
    /// <param name="context">Shared transport budget and cancellation, including retries and cache hits.</param>
    /// <param name="mode">Explicit transport shape; defaults to original field-by-field requests.</param>
    /// <returns>Owned bytes, known intervals and generation observations. No source ownership or atomicity is implied.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The region cannot hold the selected envelope or mode is invalid.</exception>
    /// <exception cref="MemoryAccessException">The shared budget is exhausted.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public MemorySnapshot Capture(MemoryRegion region, MemoryAccessContext context, MemoryReadMode mode = MemoryReadMode.Elementwise)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(context);
        if (region.Length < this.BufferLength || mode is not (MemoryReadMode.Elementwise or MemoryReadMode.Coalesced))
        {
            throw new ArgumentOutOfRangeException(nameof(region));
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        long before = region.Source.Generation;
        var bytes = new byte[this.BufferLength];
        var known = new List<MemoryByteRange>();
        foreach (MemoryByteRange interval in mode == MemoryReadMode.Elementwise ? this.intervals : this.merged)
        {
            MemoryReadResult read = region.Slice(interval.Offset, interval.Length).ReadPartial(bytes.AsSpan(interval.Offset, interval.Length), context);
            known.Add(new MemoryByteRange(interval.Offset, read.BytesRead));
            if (!read.IsComplete && mode == MemoryReadMode.Coalesced)
            {
                // A merged gap may be unavailable although every requested field is readable. Retry
                // only fields contained in this interval; all attempts remain charged to the context.
                foreach (MemoryByteRange field in this.intervals)
                {
                    if (field.Offset >= interval.Offset && field.Offset + field.Length <= interval.Offset + interval.Length)
                    {
                        MemoryReadResult retry = region.Slice(field.Offset, field.Length).ReadPartial(bytes.AsSpan(field.Offset, field.Length), context);
                        known.Add(new MemoryByteRange(field.Offset, retry.BytesRead));
                    }
                }
            }
        }

        return new MemorySnapshot(this.Schema, region, bytes, new MemoryValidity(bytes.Length, known), before, region.Source.Generation);
    }
}
