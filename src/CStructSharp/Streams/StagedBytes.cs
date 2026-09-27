namespace CStructSharp.Streams;

using System;
using System.Buffers;
using CStructSharp.Reading;

/// <summary>
///     The next bytes of a read, taken within the read budget: borrowed straight from a memory-backed source, or copied
///     from a stream into a pooled block of at most <see cref="ReadBlock.Size"/> bytes. Dispose returns the block.
/// </summary>
/// <remarks>
///     When the bytes are not <see cref="Available"/>, nothing was consumed or charged, so the caller can fall back to
///     the general reader, which reports any shortage or limit at its usual place.
/// </remarks>
internal ref struct StagedBytes
{
    private byte[]? rented;

    /// <summary>Creates staged bytes over <paramref name="bytes"/>, owning <paramref name="rented"/> when it is not null.</summary>
    /// <param name="bytes">The staged bytes.</param>
    /// <param name="rented">The pooled block holding them, or <see langword="null"/> when they are borrowed from the source.</param>
    private StagedBytes(ReadOnlySpan<byte> bytes, byte[]? rented)
    {
        this.Bytes = bytes;
        this.rented = rented;
        this.Available = true;
    }

    /// <summary>Gets whether the bytes were staged; when false, nothing was consumed.</summary>
    public bool Available { get; }

    /// <summary>Gets the staged bytes; empty when not <see cref="Available"/>.</summary>
    public ReadOnlySpan<byte> Bytes { get; }

    /// <summary>Takes the next <paramref name="count"/> bytes of <paramref name="stream"/> when the source holds them and the budget allows.</summary>
    /// <param name="stream">The budgeted source.</param>
    /// <param name="count">The nonnegative number of bytes.</param>
    /// <returns>The staged bytes, or an unavailable instance that consumed nothing.</returns>
    public static StagedBytes Take(ReadBudgetStream stream, int count)
    {
        if (stream.TryReadSpanWithinBudget(count, out ReadOnlySpan<byte> bytes))
        {
            return new StagedBytes(bytes, null);
        }

        if (count <= ReadBlock.Size)
        {
            // The block is returned here unless the staged bytes take ownership, also when the source throws.
            byte[]? block = ArrayPool<byte>.Shared.Rent(count);
            try
            {
                if (stream.TryReadBlockWithinBudget(block.AsSpan(0, count)))
                {
                    var staged = new StagedBytes(block.AsSpan(0, count), block);
                    block = null;
                    return staged;
                }
            }
            finally
            {
                if (block is not null)
                {
                    ArrayPool<byte>.Shared.Return(block);
                }
            }
        }

        return default;
    }

    /// <summary>Returns the pooled block, if any; the staged bytes must not be used afterwards.</summary>
    public void Dispose()
    {
        if (this.rented is not null)
        {
            ArrayPool<byte>.Shared.Return(this.rented);
            this.rented = null;
        }
    }
}
