namespace CStructSharp.Memory;

/// <summary>Optional demand-driven logical-to-backing provenance for a source, including dynamic translations.</summary>
/// <remarks>Source objects distinguish address spaces; Id is diagnostic only. Generation and source stability remain caller contracts. This capability describes read provenance, without promising reverse mapping, atomic snapshots or writable dynamic mappings.</remarks>
public interface IMemoryMappingSource : IMemorySource
{
    /// <summary>Describes a fully mapped finite range in logical byte order, without reading payload bytes.</summary>
    /// <param name="address">First logical byte in this source.</param>
    /// <param name="length">Finite byte count; a final byte at ulong.MaxValue is allowed.</param>
    /// <param name="context">Shared cancellation and mapping budget, or a new default operation.</param>
    /// <returns>Positive-length backing fragments covering exactly the requested count; an empty list for zero length.</returns>
    /// <exception cref="MemoryAccessException">A mapping is absent or work limits are exhausted.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    IReadOnlyList<MemoryRegion> Describe(ulong address, int length, MemoryAccessContext? context = null);
}
