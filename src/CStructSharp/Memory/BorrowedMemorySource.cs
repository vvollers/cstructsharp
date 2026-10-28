namespace CStructSharp.Memory;

/// <summary>A non-owning read-only view of caller-managed memory at an unsigned base address.</summary>
/// <remarks>The caller keeps the backing memory alive and stable for every operation and any cache lifetime. Read-only access does not freeze a mutable array. Generation is constant; this source creates no snapshot. Reads charge requested bytes once as a leaf source, independently of any earlier capture operation.</remarks>
public sealed class BorrowedMemorySource : IMemorySource
{
    private readonly ReadOnlyMemory<byte> bytes;

    /// <summary>Borrows a readable prefix without copying or taking ownership.</summary>
    /// <param name="id">Diagnostic source label.</param>
    /// <param name="bytes">Caller-owned memory; an unreadable suffix is excluded by readableLength.</param>
    /// <param name="baseAddress">Unsigned coordinate of the first buffer byte.</param>
    /// <param name="readableLength">Readable prefix in bytes; null means the whole memory.</param>
    /// <exception cref="ArgumentException">The label is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The prefix is outside the memory or its addressed range wraps.</exception>
    public BorrowedMemorySource(string id, ReadOnlyMemory<byte> bytes, ulong baseAddress = 0, int? readableLength = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        int length = readableLength ?? bytes.Length;
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, bytes.Length);
        MemoryRegion.ValidateRange(baseAddress, bytes.Length);
        this.Id = id;
        this.BaseAddress = baseAddress;
        this.bytes = bytes[..length];
    }

    /// <inheritdoc/>
    public string Id { get; }

    /// <summary>Gets zero; externally managed changes are not observable through a generation counter.</summary>
    public long Generation => 0;

    /// <summary>Gets the unsigned source coordinate corresponding to buffer offset zero.</summary>
    public ulong BaseAddress { get; }

    /// <summary>Gets the readable prefix length in bytes.</summary>
    public int Length => this.bytes.Length;

    /// <inheritdoc/>
    public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        MemoryRegion.ValidateRange(address, destination.Length);
        context.Charge(this.Id, address, destination.Length);
        if (address < this.BaseAddress || address - this.BaseAddress >= (ulong)this.bytes.Length)
        {
            return 0;
        }

        int offset = (int)(address - this.BaseAddress);
        int count = Math.Min(destination.Length, this.bytes.Length - offset);
        this.bytes.Span.Slice(offset, count).CopyTo(destination);
        return count;
    }
}
