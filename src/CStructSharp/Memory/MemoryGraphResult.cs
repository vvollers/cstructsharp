namespace CStructSharp.Memory;

/// <summary>Bounded traversal work, independent of caller-retained findings.</summary>
/// <param name="Stop">Explicit reason exploration ended.</param>
/// <param name="Nodes">Distinct canonical identities visited.</param>
/// <param name="Edges">Outgoing edges enumerated, including repeats and depth-limited edges.</param>
/// <param name="PeakPending">Largest retained frontier.</param>
public readonly record struct MemoryGraphResult(MemoryGraphStop Stop, int Nodes, long Edges, int PeakPending);
