namespace CStructSharp.Memory;

/// <summary>Explicit transport policy for a prepared set of fixed fields.</summary>
public enum MemoryReadMode
{
    /// <summary>Each requested field has its own exact request, preserving elementwise transport.</summary>
    Elementwise,

    /// <summary>Adjacent or permitted-gap intervals are merged. Unavailable merged gaps trigger independent field retries.</summary>
    Coalesced,
}
