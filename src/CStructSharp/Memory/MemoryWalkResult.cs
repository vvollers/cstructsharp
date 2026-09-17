namespace CStructSharp.Memory;

/// <summary>What a bounded traversal visited and why it stopped, with the structured failure when memory was unavailable.</summary>
/// <remarks>
/// <see cref="Nodes"/> lists the regions the walk visited, in order, before it stopped. It is a list of locations,
/// not of decoded records; the callback decides which bytes it reads at each one. In a tree, a node is recorded
/// before its children are requested, so a node whose child lookup failed still appears. Always look at
/// <see cref="Stop"/> before treating the list as complete: a partial list is useful evidence about a corrupt or
/// truncated image, but it must never silently stand in for a full inventory. <see cref="Failure"/> is set only for
/// an <see cref="MemoryWalkStop.Unavailable"/> stop.
/// </remarks>
/// <param name="Nodes">Visited regions in traversal order.</param>
/// <param name="Stop">Why the traversal stopped.</param>
/// <param name="Failure">The memory failure that ended the walk, or null for every other stop reason.</param>
public sealed record MemoryWalkResult(IReadOnlyList<MemoryRegion> Nodes, MemoryWalkStop Stop, MemoryAccessException? Failure = null);
