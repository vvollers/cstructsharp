namespace CStructSharp.Memory;

/// <summary>Bounded traversals over linked structures in memory: circular sentinel lists and trees with shared children.</summary>
/// <remarks>
/// <para>
/// <see cref="MemorySession"/> follows one explicit path. Real analysis repeats that step across many nodes: walk
/// the task list, visit every child of a tree. Repetition over untrusted bytes needs safeguards that a single read
/// does not: a corrupt link can form a cycle, a shared child can appear twice, and a list can be far longer than
/// expected. This class supplies those safeguards (node limits, one shared work budget, and identity tracking by
/// source object and address) while a caller-supplied callback supplies the format-specific part: how to read a
/// link, or which entries of a node are children.
/// </para>
/// <para>
/// The helpers impose no operating-system conventions on the schema language. A sentinel list excludes its head
/// from the result because the head is not a data item; a tree includes its root and visits each distinct node
/// once. Only unavailable memory (<see cref="MemoryFailure.Unmapped"/> or <see cref="MemoryFailure.MissingBytes"/>)
/// becomes a partial <see cref="MemoryWalkResult"/>; cancellation, budget exhaustion, and other failures propagate
/// as exceptions so an incomplete traversal can never be mistaken for a complete one.
/// </para>
/// </remarks>
public static class MemoryWalker
{
    /// <summary>Recovers the address of a record from the address of one of its members, as C's <c>container_of</c> does.</summary>
    /// <remarks>Intrusive lists store their link field inside the data record, so a walk yields link addresses
    /// and the record starts <paramref name="memberOffset"/> bytes earlier. The subtraction is checked; choosing the
    /// right containing type and confirming its bytes exist remain the caller's job.</remarks>
    /// <param name="memberAddress">Address of the embedded member.</param>
    /// <param name="memberOffset">Byte offset of that member within its containing record, from the schema.</param>
    /// <returns>The unsigned address of the containing record.</returns>
    public static ulong ContainingRecord(ulong memberAddress, ulong memberOffset) => checked(memberAddress - memberOffset);

    /// <summary>Follows links from a sentinel head until the walk returns to it, repeats a data node, hits the node limit, or finds memory missing.</summary>
    /// <remarks>
    /// <para>
    /// A circular sentinel list has a head node that holds no data; the last data node links back to it. The
    /// callback is called first on the head and then on each data node, returning the region the node's link
    /// points at. Coming back to the head's source object and address is success. Reaching a data node that was
    /// already returned is a cycle and is reported as <see cref="MemoryWalkStop.RepeatedNode"/>. The head is not
    /// included in <see cref="MemoryWalkResult.Nodes"/>.
    /// </para>
    /// <para>
    /// The limit is checked after resolving the next link, so <paramref name="maxNodes"/> bounds the returned
    /// collection, not the exact number of reads. The callback must pass its <see cref="MemoryAccessContext"/>
    /// argument to any session read so the whole walk shares one budget.
    /// </para>
    /// </remarks>
    /// <param name="sentinel">List head whose source and address mark successful completion.</param>
    /// <param name="next">Reads one node's link and returns the next node's region, using the shared context.</param>
    /// <param name="maxNodes">Maximum number of data nodes to return.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>The data nodes visited, in order, and the reason the walk stopped.</returns>
    public static MemoryWalkResult SentinelList(MemoryRegion sentinel, Func<MemoryRegion, MemoryAccessContext, MemoryRegion> next, int maxNodes = 100_000, MemoryAccessContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(sentinel);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNodes);
        context ??= new MemoryAccessContext();
        var nodes = new List<MemoryRegion>();
        var seen = new HashSet<(IMemorySource Source, ulong Address)>(SourceAddressComparer.Instance);
        MemoryRegion current = sentinel;
        try
        {
            while (true)
            {
                // Each step is charged even when the callback itself reads nothing, so a loop cannot be free.
                context.Charge(current.Source.Id, current.Address, 0);
                current = next(current, context);
                if (ReferenceEquals(current.Source, sentinel.Source) && current.Address == sentinel.Address)
                {
                    return new MemoryWalkResult(nodes.AsReadOnly(), MemoryWalkStop.Sentinel);
                }

                if (!seen.Add((current.Source, current.Address)))
                {
                    return new MemoryWalkResult(nodes.AsReadOnly(), MemoryWalkStop.RepeatedNode);
                }

                if (nodes.Count == maxNodes)
                {
                    return new MemoryWalkResult(nodes.AsReadOnly(), MemoryWalkStop.NodeLimit);
                }

                nodes.Add(current);
            }
        }
        catch (MemoryAccessException exception) when (exception.Failure is MemoryFailure.Unmapped or MemoryFailure.MissingBytes)
        {
            return new MemoryWalkResult(nodes.AsReadOnly(), MemoryWalkStop.Unavailable, exception);
        }
    }

    /// <summary>Visits every distinct node reachable from a root, asking the callback for each node's children.</summary>
    /// <remarks>
    /// <para>
    /// This is an iterative depth-first walk over an explicit stack, so a deep tree cannot overflow the call stack.
    /// Because children are pushed in enumeration order and popped last-in first-out, they are visited in reverse
    /// of that order. A node is identified by its source object and address; length and label are not part of the
    /// key. A child shared by several parents is visited once, and a cycle terminates through the same check.
    /// </para>
    /// <para>
    /// A node is added to the result before its children are requested, so a partial result includes the node
    /// whose child lookup failed. The callback owns the interpretation of tagged entries and leaf values; it must
    /// enumerate finitely and pass the shared context to any reads.
    /// </para>
    /// </remarks>
    /// <param name="root">Region of the root node.</param>
    /// <param name="children">Returns the child regions of a node, using the shared context for any reads.</param>
    /// <param name="maxNodes">Maximum number of nodes to return.</param>
    /// <param name="context">Shared operation budget and cancellation, or null to create a default budget.</param>
    /// <returns>The nodes visited, in order, and the reason the walk stopped.</returns>
    public static MemoryWalkResult Tree(MemoryRegion root, Func<MemoryRegion, MemoryAccessContext, IEnumerable<MemoryRegion>> children, int maxNodes = 100_000, MemoryAccessContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(children);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNodes);
        context ??= new MemoryAccessContext();
        var pending = new Stack<MemoryRegion>();
        var nodes = new List<MemoryRegion>();
        var seen = new HashSet<(IMemorySource Source, ulong Address)>(SourceAddressComparer.Instance);
        pending.Push(root);
        try
        {
            while (pending.TryPop(out MemoryRegion? current))
            {
                context.Charge(current.Source.Id, current.Address, 0);
                if (!seen.Add((current.Source, current.Address)))
                {
                    continue;
                }

                if (nodes.Count == maxNodes)
                {
                    return new MemoryWalkResult(nodes.AsReadOnly(), MemoryWalkStop.NodeLimit);
                }

                nodes.Add(current);
                foreach (MemoryRegion child in children(current, context))
                {
                    // Charging each child as it is enumerated bounds a callback that yields endlessly.
                    context.Charge(child.Source.Id, child.Address, 0);
                    pending.Push(child);
                }
            }

            return new MemoryWalkResult(nodes.AsReadOnly(), MemoryWalkStop.Complete);
        }
        catch (MemoryAccessException exception) when (exception.Failure is MemoryFailure.Unmapped or MemoryFailure.MissingBytes)
        {
            return new MemoryWalkResult(nodes.AsReadOnly(), MemoryWalkStop.Unavailable, exception);
        }
    }

    /// <summary>Compares visited nodes by source object identity and address, ignoring any value equality a source type might define.</summary>
    private sealed class SourceAddressComparer : IEqualityComparer<(IMemorySource Source, ulong Address)>
    {
        /// <summary>Gets the shared stateless instance.</summary>
        internal static SourceAddressComparer Instance { get; } = new();

        /// <inheritdoc/>
        public bool Equals((IMemorySource Source, ulong Address) left, (IMemorySource Source, ulong Address) right) => ReferenceEquals(left.Source, right.Source) && left.Address == right.Address;

        /// <inheritdoc/>
        public int GetHashCode((IMemorySource Source, ulong Address) value) => HashCode.Combine(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value.Source), value.Address);
    }
}
