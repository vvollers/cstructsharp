namespace CStructSharp.Tests;

using System.Buffers;

/// <summary>Builds multi-segment <see cref="ReadOnlySequence{T}"/> inputs, so a read crosses segment boundaries.</summary>
internal static class ChunkedSequence
{
    /// <summary>Splits <paramref name="data"/> into segments of at most <paramref name="chunk"/> bytes.</summary>
    /// <param name="data">The bytes; each segment copies its part.</param>
    /// <param name="chunk">The largest segment length, at least one.</param>
    /// <returns>The sequence; empty input gives an empty sequence.</returns>
    public static ReadOnlySequence<byte> Of(byte[] data, int chunk = 3)
    {
        if (data.Length == 0)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        var first = new Segment(data.AsSpan(0, Math.Min(chunk, data.Length)).ToArray(), 0);
        Segment last = first;
        for (int start = chunk; start < data.Length; start += chunk)
        {
            last = last.Append(data.AsSpan(start, Math.Min(chunk, data.Length - start)).ToArray());
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    /// <summary>One segment of a chunked sequence.</summary>
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        /// <summary>Creates a segment.</summary>
        /// <param name="bytes">The segment's bytes.</param>
        /// <param name="runningIndex">The sequence offset of the segment's first byte.</param>
        public Segment(byte[] bytes, long runningIndex)
        {
            this.Memory = bytes;
            this.RunningIndex = runningIndex;
        }

        /// <summary>Links a new segment after this one.</summary>
        /// <param name="bytes">The new segment's bytes.</param>
        /// <returns>The new segment.</returns>
        public Segment Append(byte[] bytes)
        {
            var next = new Segment(bytes, this.RunningIndex + this.Memory.Length);
            this.Next = next;
            return next;
        }
    }
}
