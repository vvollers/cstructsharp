namespace CStructSharp.Memory;

/// <summary>A synchronous borrowed scan window in one source's logical coordinates.</summary>
/// <remarks>Bytes are valid only during the callback. OwnedOffset/OwnedLength assign candidate starts exactly once; before/after bytes are context for complete records. Windows never join unknown bytes or different sources. No callback may retain the pooled storage.</remarks>
public readonly ref struct MemoryScanWindow
{
    /// <summary>Stores one callback's borrowed view.</summary>
    /// <param name="source">Address-space identity.</param>
    /// <param name="address">Logical coordinate of Bytes[0].</param>
    /// <param name="bytes">Contiguous confirmed readable bytes.</param>
    /// <param name="ownedOffset">First owned candidate start relative to bytes.</param>
    /// <param name="ownedLength">Number of owned candidate-start bytes.</param>
    internal MemoryScanWindow(IMemorySource source, ulong address, ReadOnlySpan<byte> bytes, int ownedOffset, int ownedLength)
    {
        this.Source = source;
        this.Address = address;
        this.Bytes = bytes;
        this.OwnedOffset = ownedOffset;
        this.OwnedLength = ownedLength;
    }

    /// <summary>Gets the source object distinguishing this address space.</summary>
    public IMemorySource Source { get; }

    /// <summary>Gets the logical address of the first supplied byte.</summary>
    public ulong Address { get; }

    /// <summary>Gets borrowed known bytes; do not retain them beyond the callback.</summary>
    public ReadOnlySpan<byte> Bytes { get; }

    /// <summary>Gets the first owned candidate-start offset.</summary>
    public int OwnedOffset { get; }

    /// <summary>Gets the number of owned candidate-start positions. Complete records can extend into following context bytes.</summary>
    public int OwnedLength { get; }
}
