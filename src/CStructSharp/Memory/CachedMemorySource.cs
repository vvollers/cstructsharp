namespace CStructSharp.Memory;

/// <summary>A read-only adapter that remembers the bytes of exact requests until the backing source's generation changes.</summary>
/// <remarks>
/// <para>
/// Analyzers read the same few ranges repeatedly: the link field of every list node, the header of every page.
/// When the backing source is a file or a remote transport, answering those repeats from memory is much cheaper.
/// This cache stores the result of each complete read under the key <c>(address, requested length)</c> and
/// returns it for an identical later request.
/// </para>
/// <para>
/// The design is deliberately simple so its behavior can be predicted. There is no page prefetch and no merging
/// of overlapping ranges, so the cache never reads neighboring bytes that might be unavailable; reading four bytes
/// and then two bytes at the same address are two different entries. Capacity is a retained byte count with
/// first-in, first-out eviction; a hit does not refresh an entry's position. Requests larger than the capacity are
/// answered but not stored, and short reads are not stored because they do not describe the whole range. A hit
/// still charges one request (so loops remain bounded) but charges no backing bytes.
/// </para>
/// <para>
/// Correctness depends entirely on <see cref="IMemorySource.Generation"/>: the cache empties itself when the
/// backing generation differs from the one it last saw. Use it only over an immutable snapshot or over a source
/// that advances its generation on every change. A live source with unreported changes must not be cached, because
/// stale entries would be returned indefinitely. The adapter is read-only; plan writes through the writable source
/// beneath it, whose generation change then invalidates the cache.
/// </para>
/// </remarks>
public sealed class CachedMemorySource : IMemorySource
{
    private readonly IMemorySource source;
    private readonly int capacity;
    private readonly Dictionary<(ulong Address, int Length), byte[]> entries = new();
    private readonly Queue<(ulong Address, int Length)> order = new();
    private readonly object gate = new();
    private long generation;
    private int retained;

    /// <summary>Creates an empty cache in front of a caller-owned source.</summary>
    /// <param name="id">Diagnostic label; source objects, not labels, distinguish address spaces.</param>
    /// <param name="source">Caller-owned backing source; ownership is not transferred.</param>
    /// <param name="capacity">Maximum number of bytes retained across all entries; larger single requests bypass storage.</param>
    public CachedMemorySource(string id, IMemorySource source, int capacity = 1024 * 1024)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        this.Id = id;
        this.source = source;
        this.capacity = capacity;
        this.generation = source.Generation;
    }

    /// <summary>Gets the diagnostic label for this source.</summary>
    public string Id { get; }

    /// <summary>Gets the backing source's generation; a change is acted on at the next read.</summary>
    public long Generation => this.source.Generation;

    /// <summary>Answers from the cache when the exact range is present; otherwise reads the backing source and stores a complete result.</summary>
    /// <param name="address">Unsigned address in the backing source's coordinate system.</param>
    /// <param name="destination">Caller-owned destination used only during this call.</param>
    /// <param name="context">Shared operation budget and cancellation; a hit charges one request and zero bytes.</param>
    /// <returns>The number of bytes copied into the destination.</returns>
    public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Charge(this.Id, address, 0);
        lock (this.gate)
        {
            // Invalidate everything when the backing source reports a change; per-entry tracking is not possible
            // because a generation says that something changed, not what.
            long current = this.source.Generation;
            if (this.generation != current)
            {
                this.entries.Clear();
                this.order.Clear();
                this.retained = 0;
                this.generation = current;
            }

            var key = (address, destination.Length);
            if (this.entries.TryGetValue(key, out byte[]? cached))
            {
                cached.CopyTo(destination);
                return cached.Length;
            }

            int read = this.source.Read(address, destination, context);
            if (read < 0 || read > destination.Length)
            {
                throw new MemoryAccessException(MemoryFailure.SourceFailure, this.Id, address, destination.Length, "Backing source returned an invalid read count.");
            }

            // The generation was sampled before the read; if it moved during the read, the bytes may mix two states
            // and must not be cached or returned as consistent.
            if (current != this.source.Generation)
            {
                throw new MemoryAccessException(MemoryFailure.StaleSource, this.Id, address, destination.Length, "Backing source changed during a cached read.");
            }

            // Store only complete reads that fit; evict oldest entries until this one fits.
            if (read == destination.Length && read > 0 && read <= this.capacity)
            {
                while (read > this.capacity - this.retained)
                {
                    (ulong Address, int Length) oldest = this.order.Dequeue();
                    this.entries.Remove(oldest);
                    this.retained -= oldest.Length;
                }

                this.entries.Add(key, destination.ToArray());
                this.order.Enqueue(key);
                this.retained += read;
            }

            return read;
        }
    }
}
