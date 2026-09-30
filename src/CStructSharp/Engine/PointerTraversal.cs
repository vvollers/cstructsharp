namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;

/// <summary>
///     The pointer bookkeeping of one read operation: the targets on the active path (for cycle detection) and the
///     pointers whose targets wait for their struct's last field. An operation rents it on its first pointer and
///     returns it on completion, so a thread reuses one instance across reads instead of allocating both collections
///     for every read of a layout with pointers.
/// </summary>
internal sealed class PointerTraversal
{
    // Collections that grew past these sizes are dropped rather than kept alive by the cache.
    private const int MaximumCachedPending = 256;
    private const int MaximumCachedTargets = 256;

    [ThreadStatic]
    private static PointerTraversal? cached;

    /// <summary>The (address, type, depth) keys of the pointer targets being read on the active path.</summary>
    public HashSet<(long Address, string TypeName, int PointerDepth)> ActiveTargets { get; } = new();

    /// <summary>The deferred pointers, innermost struct's entries last.</summary>
    public List<PendingPointer> Pending { get; } = new();

    /// <summary>Takes this thread's cached instance, or creates one when it is in use or was never created.</summary>
    /// <returns>An empty instance owned by the caller until <see cref="Return"/>.</returns>
    public static PointerTraversal Rent()
    {
        PointerTraversal? traversal = cached;
        if (traversal is null)
        {
            return new PointerTraversal();
        }

        cached = null;
        return traversal;
    }

    /// <summary>
    ///     Clears an instance and caches it for the next read on this thread. The caller must not use it afterwards;
    ///     clearing also releases the references to results, fields and debug paths it held.
    /// </summary>
    /// <param name="traversal">The instance returned by <see cref="Rent"/>.</param>
    public static void Return(PointerTraversal traversal)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        bool small = traversal.Pending.Capacity <= MaximumCachedPending && traversal.ActiveTargets.EnsureCapacity(0) <= MaximumCachedTargets;
        traversal.ActiveTargets.Clear();
        traversal.Pending.Clear();
        if (small)
        {
            cached = traversal;
        }
    }
}
