namespace CStructSharp.Memory;

/// <summary>Immutable known-byte intervals of a captured buffer. Unlisted bytes provide no evidence, including zero-filled presentation bytes.</summary>
public sealed class MemoryValidity
{
    private readonly MemoryByteRange[] ranges;

    /// <summary>Snapshots, validates, sorts and merges known intervals.</summary>
    /// <param name="length">Total buffer length in bytes.</param>
    /// <param name="readable">Known intervals, independent of their stored byte values.</param>
    /// <param name="maxRanges">Bound on input interval count, including duplicates.</param>
    /// <exception cref="ArgumentOutOfRangeException">Lengths, offsets or range limits are invalid.</exception>
    public MemoryValidity(int length, IEnumerable<MemoryByteRange> readable, int maxRanges = 1_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRanges);
        ArgumentNullException.ThrowIfNull(readable);
        this.Length = length;
        var sorted = new List<MemoryByteRange>();
        foreach (MemoryByteRange range in readable)
        {
            if (range.Offset < 0 || range.Length < 0 || range.Offset > length || range.Length > length - range.Offset || sorted.Count >= maxRanges)
            {
                throw new ArgumentOutOfRangeException(nameof(readable));
            }

            sorted.Add(range);
        }

        // Order by coordinate so membership queries need one binary search, regardless of capture order.
        sorted.Sort(static (left, right) => left.Offset.CompareTo(right.Offset));
        var merged = new List<MemoryByteRange>();
        foreach (MemoryByteRange range in sorted)
        {
            if (range.Length == 0)
            {
                continue;
            }

            if (merged.Count != 0 && range.Offset <= merged[^1].Offset + merged[^1].Length)
            {
                MemoryByteRange previous = merged[^1];
                merged[^1] = new MemoryByteRange(previous.Offset, Math.Max(previous.Offset + previous.Length, range.Offset + range.Length) - previous.Offset);
            }
            else
            {
                merged.Add(range);
            }
        }

        this.ranges = merged.ToArray();
        this.ReadableRanges = Array.AsReadOnly(this.ranges);
    }

    /// <summary>Gets the total storage length, including unknown bytes.</summary>
    public int Length { get; }

    /// <summary>Gets disjoint known intervals in buffer order.</summary>
    public IReadOnlyList<MemoryByteRange> ReadableRanges { get; }

    /// <summary>Checks that every byte required by a rule is known.</summary>
    /// <param name="offset">Buffer-relative byte offset.</param>
    /// <param name="length">Required byte count.</param>
    /// <returns>False for invalid bounds or any unknown byte; true for a bounded empty request.</returns>
    public bool IsKnown(int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > this.Length || length > this.Length - offset)
        {
            return false;
        }

        if (length == 0)
        {
            return true;
        }

        int low = 0;
        int high = this.ranges.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) / 2);
            MemoryByteRange range = this.ranges[middle];
            if (offset < range.Offset)
            {
                high = middle - 1;
            }
            else if (offset - range.Offset >= range.Length)
            {
                low = middle + 1;
            }
            else
            {
                return length <= range.Length - (offset - range.Offset);
            }
        }

        return false;
    }
}
