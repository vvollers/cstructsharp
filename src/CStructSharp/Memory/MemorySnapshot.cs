namespace CStructSharp.Memory;

/// <summary>Owned selected storage with explicit validity and observed source generation.</summary>
/// <remarks>Unselected storage is presentation padding and cannot supply evidence. Equal generation observations are not a promise of an atomic snapshot for live sources. The captured owned bytes themselves remain stable.</remarks>
public sealed class MemorySnapshot
{
    private readonly byte[] bytes;

    /// <summary>Stores operation-owned bytes and their confirmed validity.</summary>
    /// <param name="schema">Schema identity used to prepare the capture.</param>
    /// <param name="region">Original source identity and logical coordinate.</param>
    /// <param name="bytes">Transferred owned capture storage.</param>
    /// <param name="validity">Confirmed bytes only.</param>
    /// <param name="generationBefore">Observed source generation before capture.</param>
    /// <param name="generationAfter">Observed source generation after capture.</param>
    internal MemorySnapshot(MemorySchema schema, MemoryRegion region, byte[] bytes, MemoryValidity validity, long generationBefore, long generationAfter)
    {
        this.Schema = schema;
        this.Region = region;
        this.bytes = bytes;
        this.Validity = validity;
        this.GenerationBefore = generationBefore;
        this.GenerationAfter = generationAfter;
    }

    /// <summary>Gets the exact schema instance owning the prepared fields.</summary>
    public MemorySchema Schema { get; }

    /// <summary>Gets original source identity and logical origin, without taking source ownership.</summary>
    public MemoryRegion Region { get; }

    /// <summary>Gets owned stable bytes, including explicitly invalid presentation padding.</summary>
    public ReadOnlyMemory<byte> Bytes => this.bytes;

    /// <summary>Gets confirmed readable intervals. Padding and failed bytes are never known zeroes.</summary>
    public MemoryValidity Validity { get; }

    /// <summary>Gets the source generation observed before reads.</summary>
    public long GenerationBefore { get; }

    /// <summary>Gets the source generation observed after reads.</summary>
    public long GenerationAfter { get; }

    /// <summary>Decodes a prepared field from known captured bytes, rejecting a different schema instance.</summary>
    /// <typeparam name="T">Exact managed field result.</typeparam>
    /// <param name="reader">Reader bound to the capture's identical schema.</param>
    /// <param name="context">Shared local decoding budget and cancellation.</param>
    /// <param name="value">Decoded value, or default for unavailable storage.</param>
    /// <returns>Whether all required field bytes were captured.</returns>
    /// <exception cref="ArgumentException">The reader belongs to another schema.</exception>
    public bool TryRead<T>(MemoryFieldReader<T> reader, MemoryAccessContext context, out T value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (!ReferenceEquals(reader.Selection.Schema, this.Schema))
        {
            throw new ArgumentException("A snapshot reader requires the identical schema instance.", nameof(reader));
        }

        return reader.TryReadBuffer(this.bytes, context, out value, this.Validity);
    }
}
