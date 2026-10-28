namespace CStructSharp.Memory;

using System.Buffers;

/// <summary>Deterministic bounded scan transport over caller-supplied ranges, independent of candidate interpretation.</summary>
public static class MemoryScan
{
    /// <summary>Streams known windows, reusing context bytes and retaining later islands after unavailable reads.</summary>
    /// <remarks>Ranges are supplied in desired order. Adjacent ranges in the same source are continuous; overlapping or descending adjacent ranges in that source are rejected. Unknown intervals break continuity. A positive short read is retried. UnavailableStride explicitly permits skipping to the next aligned unit after a failed byte; use 1 for arbitrary byte-level holes or the source's known page granularity. Coverage counts that whole skipped unit as unavailable. Align candidates using window.Address, not buffer offset zero.</remarks>
    /// <param name="ranges">Explicit finite logical ranges, with caller-owned sources.</param>
    /// <param name="visitor">Synchronous borrowed-window callback. It must enforce candidate/hit/result limits.</param>
    /// <param name="context">Shared CPU, translation and transport budget; cancellation always propagates.</param>
    /// <param name="windowBytes">Owned candidate-start bytes per full window, between 1 and 64 MiB.</param>
    /// <param name="overlapBytes">Context on either side, at most windowBytes. Complete candidates must fit the chosen context bound.</param>
    /// <param name="unavailableStride">Explicit unavailable-unit size in bytes, aligned to logical coordinates.</param>
    /// <param name="maxWindows">Maximum callbacks, also bounding retained consumer work.</param>
    /// <param name="maxRanges">Maximum supplied ranges, before coalescing.</param>
    /// <returns>Coverage and explicit completion reason. Exhausted ranges can still contain unavailable coverage.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Limits or scratch sizes are invalid.</exception>
    /// <exception cref="ArgumentException">Neighboring ranges in one source overlap or descend.</exception>
    /// <exception cref="MemoryAccessException">The shared budget is exhausted.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static MemoryScanResult Run(IEnumerable<MemoryRegion> ranges, MemoryScanVisitor visitor, MemoryAccessContext context, int windowBytes = 1 << 20, int overlapBytes = 0, int unavailableStride = 1, int maxWindows = 1_000_000, int maxRanges = 100_000)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(visitor);
        ArgumentNullException.ThrowIfNull(context);
        if (windowBytes is <= 0 or > 64 * 1024 * 1024 || overlapBytes < 0 || overlapBytes > windowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(windowBytes));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unavailableStride);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWindows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRanges);
        context.CancellationToken.ThrowIfCancellationRequested();
        byte[] buffer = ArrayPool<byte>.Shared.Rent(checked(windowBytes + (2 * overlapBytes)));
        ScanState state = default;
        try
        {
            MemoryRegion? pending = null;
            var observed = new Dictionary<IMemorySource, (ulong Address, long Length, long Generation)>(ReferenceEqualityComparer.Instance);
            int count = 0;
            foreach (MemoryRegion region in ranges)
            {
                context.Charge(region.Source.Id, region.Address, 0);
                if (++count > maxRanges)
                {
                    throw new ArgumentOutOfRangeException(nameof(ranges), "Range count exceeds the explicit limit.");
                }

                if (region.Length == 0)
                {
                    continue;
                }

                if (observed.TryGetValue(region.Source, out var previous))
                {
                    if (region.Address < previous.Address || region.Address - previous.Address < (ulong)previous.Length)
                    {
                        throw new ArgumentException("Ranges in one source must be ordered and disjoint.", nameof(ranges));
                    }

                    if (region.Source.Generation != previous.Generation)
                    {
                        state.Stop = MemoryScanStop.SourceChanged;
                        return state.Result;
                    }
                }

                observed[region.Source] = (region.Address, region.Length, region.Source.Generation);
                if (pending is not null && ReferenceEquals(pending.Source, region.Source))
                {
                    if (region.Address < pending.Address || region.Address - pending.Address < (ulong)pending.Length)
                    {
                        throw new ArgumentException("Ranges in one source must be ordered and disjoint.", nameof(ranges));
                    }

                    if (region.Address - pending.Address == (ulong)pending.Length && region.Length <= long.MaxValue - pending.Length)
                    {
                        pending = new MemoryRegion(pending.Source, pending.Address, pending.Length + region.Length);
                        continue;
                    }
                }

                if (pending is not null && !ScanRegion(pending, buffer, visitor, context, windowBytes, overlapBytes, unavailableStride, maxWindows, observed[pending.Source].Generation, ref state))
                {
                    return state.Result;
                }

                pending = region;
            }

            if (pending is not null)
            {
                ScanRegion(pending, buffer, visitor, context, windowBytes, overlapBytes, unavailableStride, maxWindows, observed[pending.Source].Generation, ref state);
            }

            return state.Result;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>Scans one continuous declared extent, dropping overlap whenever the source exposes a hole.</summary>
    /// <param name="region">Finite range, possibly ending at the final unsigned address.</param>
    /// <param name="buffer">Operation-owned bounded scratch.</param>
    /// <param name="visitor">Borrowed-window consumer.</param>
    /// <param name="context">Shared operation context.</param>
    /// <param name="windowBytes">Owned starts per window.</param>
    /// <param name="overlap">Maximum context bytes on each side.</param>
    /// <param name="stride">Unavailable-unit granularity.</param>
    /// <param name="maxWindows">Maximum callbacks.</param>
    /// <param name="generation">Generation observed for this source at operation entry.</param>
    /// <param name="state">Accumulates coverage across ranges.</param>
    /// <returns>Whether to continue with another declared extent.</returns>
    private static bool ScanRegion(MemoryRegion region, byte[] buffer, MemoryScanVisitor visitor, MemoryAccessContext context, int windowBytes, int overlap, int stride, int maxWindows, long generation, ref ScanState state)
    {
        long next = 0;
        long bufferOffset = 0;
        int available = 0;
        int transferLimit = windowBytes + overlap;
        while (next < region.Length)
        {
            context.Charge(region.Source.Id, region.Address + (ulong)next, 0);
            if (region.Source.Generation != generation)
            {
                state.Stop = MemoryScanStop.SourceChanged;
                return false;
            }

            if (state.Windows >= maxWindows)
            {
                state.Stop = MemoryScanStop.WindowLimit;
                return false;
            }

            int before = (int)(next - bufferOffset);
            int owned = (int)Math.Min(windowBytes, region.Length - next);
            int after = (int)Math.Min(overlap, region.Length - next - owned);
            int wanted = before + owned + after;
            int request = wanted - available;
            var read = ReadWindow(region, bufferOffset + available, buffer.AsSpan(available, request), context, ref transferLimit);
            available += read.BytesRead;
            state.Readable = checked(state.Readable + read.BytesRead);
            if (region.Source.Generation != generation)
            {
                state.Stop = MemoryScanStop.SourceChanged;
                return false;
            }

            int starts = read.IsComplete ? owned : Math.Max(0, available - before);
            if (starts != 0)
            {
                state.Windows++;
                bool keepScanning = visitor(new MemoryScanWindow(region.Source, region.Address + (ulong)bufferOffset, buffer.AsSpan(0, available), before, starts), context);
                context.CancellationToken.ThrowIfCancellationRequested();
                if (region.Source.Generation != generation)
                {
                    state.Stop = MemoryScanStop.SourceChanged;
                    return false;
                }

                if (!keepScanning)
                {
                    state.Stop = MemoryScanStop.VisitorStopped;
                    return false;
                }
            }

            if (!read.IsComplete)
            {
                long hole = bufferOffset + available;
                ulong logicalHole = region.Address + (ulong)hole;
                long skip = Math.Min(region.Length - hole, stride - (long)(logicalHole % (ulong)stride));
                state.Unavailable = checked(state.Unavailable + skip);
                next = hole + skip;
                bufferOffset = next;
                available = 0;
                transferLimit = stride;
                continue;
            }

            next += owned;
            int keepAt = Math.Max(0, before + owned - overlap);
            buffer.AsSpan(keepAt, available - keepAt).CopyTo(buffer);
            available -= keepAt;
            bufferOffset += keepAt;
        }

        return true;
    }

    /// <summary>Retries positive short reads with bounded request overhead rather than repeatedly requesting a large remaining suffix.</summary>
    /// <param name="region">Declared source extent.</param>
    /// <param name="offset">Local destination origin.</param>
    /// <param name="destination">Window storage to fill.</param>
    /// <param name="context">Shared requested-byte accounting and cancellation.</param>
    /// <param name="limit">Adaptive next request size, retained across windows and reset to unavailable granularity at a hole.</param>
    /// <returns>Confirmed prefix and exact storage failure. No unread bytes are accepted as evidence.</returns>
    private static (int BytesRead, bool IsComplete) ReadWindow(MemoryRegion region, long offset, Span<byte> destination, MemoryAccessContext context, ref int limit)
    {
        int copied = 0;
        try
        {
            while (copied < destination.Length)
            {
                ulong address = region.Address + (ulong)offset + (ulong)copied;
                context.Charge(region.Source.Id, address, 0);
                int requested = Math.Min(destination.Length - copied, limit);
                int count = region.Source.Read(address, destination.Slice(copied, requested), context);
                if (count <= 0 || count > requested)
                {
                    return (copied, false);
                }

                copied += count;

                // Doubling the confirmed prefix adapts to small transports without locking an
                // unaligned page's one-byte first read into one-byte requests for the whole range.
                limit = checked(count * 2);
            }
        }
        catch (MemoryAccessException exception) when (exception.Failure != MemoryFailure.BudgetExceeded)
        {
            return (copied, false);
        }

        return (copied, true);
    }

    /// <summary>Operation-local counters, independent of prepared plans or source lifetime.</summary>
    private struct ScanState
    {
        /// <summary>Termination reason; zero denotes range exhaustion.</summary>
        internal MemoryScanStop Stop;

        /// <summary>Newly copied bytes.</summary>
        internal long Readable;

        /// <summary>Skipped unavailable bytes.</summary>
        internal long Unavailable;

        /// <summary>Callbacks issued.</summary>
        internal int Windows;

        /// <summary>Gets the owned coverage summary.</summary>
        internal readonly MemoryScanResult Result => new(this.Stop, this.Readable, this.Unavailable, this.Windows);
    }
}
