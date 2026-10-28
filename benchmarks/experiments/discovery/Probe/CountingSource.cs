namespace DiscoveryLibraryProbe;

using CStructSharp.Memory;

/// <summary>Counts real source calls and bytes, without simulating transport latency.</summary>
/// <param name="bytes">Stable synthetic storage.</param>
internal sealed class CountingSource(byte[] bytes) : IMemorySource
{
    private readonly ByteArrayMemorySource source = new("synthetic", bytes);

    /// <inheritdoc/>
    public string Id => this.source.Id;

    /// <inheritdoc/>
    public long Generation => this.source.Generation;

    /// <summary>Gets leaf read calls, including warmup.</summary>
    public long Requests { get; private set; }

    /// <summary>Gets requested leaf bytes, including warmup.</summary>
    public long Bytes { get; private set; }

    /// <inheritdoc/>
    public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
    {
        this.Requests++;
        this.Bytes += destination.Length;
        return this.source.Read(address, destination, context);
    }
}
