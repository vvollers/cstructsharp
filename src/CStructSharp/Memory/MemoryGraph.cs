namespace CStructSharp.Memory;

/// <summary>Streaming breadth- or depth-first exploration with explicit identity and independently bounded work.</summary>
public static class MemoryGraph
{
    /// <summary>Visits graph nodes in deterministic BFS or preorder DFS, preserving supplied root and edge order.</summary>
    /// <remarks>Canonical identity must include the source/process and generation when they matter. Duplicate edges still charge work. No results, sources or caches survive the operation. The visitor decides whether an unavailable branch is skipped; exceptions, cancellation and budget exhaustion always propagate. Depth-limited branches do not prevent visits to other branches.</remarks>
    /// <typeparam name="TNode">Application node and coordinate representation.</typeparam>
    /// <typeparam name="TKey">Explicit canonical identity.</typeparam>
    /// <param name="roots">Ordered initial nodes.</param>
    /// <param name="identity">Canonical identity, preserving source/process/generation distinctions.</param>
    /// <param name="visit">Streams each distinct node and its zero-based depth. Stop preserves the caller's chosen first result.</param>
    /// <param name="edges">Ordered outgoing nodes; the iterator is disposed on every stop.</param>
    /// <param name="context">Shared CPU, callback, translation and read budget.</param>
    /// <param name="breadthFirst">True for BFS; false for preorder DFS.</param>
    /// <param name="maxNodes">Maximum distinct visited identities.</param>
    /// <param name="maxEdges">Maximum enumerated outgoing edges, counting duplicates and rejected-depth edges.</param>
    /// <param name="maxDepth">Maximum included depth; roots have depth zero.</param>
    /// <param name="maxPending">Maximum retained frontier, including supplied roots.</param>
    /// <returns>Explicit completion and work counts; partial findings remain owned by the caller.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A limit is invalid.</exception>
    /// <exception cref="MemoryAccessException">The shared operation budget is exhausted.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public static MemoryGraphResult Run<TNode, TKey>(IEnumerable<TNode> roots, Func<TNode, TKey> identity, Func<TNode, int, MemoryAccessContext, MemoryGraphVisit> visit, Func<TNode, MemoryAccessContext, IEnumerable<TNode>> edges, MemoryAccessContext context, bool breadthFirst = true, int maxNodes = 100_000, int maxEdges = 1_000_000, int maxDepth = 64, int maxPending = 100_000)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(visit);
        ArgumentNullException.ThrowIfNull(edges);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxNodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEdges);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPending);
        context.CancellationToken.ThrowIfCancellationRequested();
        var queue = new Queue<(TNode Node, int Depth)>();
        var stack = new Stack<(TNode Node, int Depth)>();
        var staging = new List<(TNode Node, int Depth)>();
        var seen = new HashSet<TKey>();
        int peak = 0;
        long edgeCount = 0;
        bool depthLimited = false;
        foreach (TNode root in roots)
        {
            context.Charge(null, null, 0);
            if (staging.Count >= maxPending)
            {
                return new MemoryGraphResult(MemoryGraphStop.PendingLimit, 0, 0, maxPending);
            }

            staging.Add((root, 0));
        }

        Schedule(staging, queue, stack, breadthFirst);
        peak = staging.Count;
        staging.Clear();
        while (breadthFirst ? queue.Count != 0 : stack.Count != 0)
        {
            context.Charge(null, null, 0);
            (TNode node, int depth) = breadthFirst ? queue.Dequeue() : stack.Pop();
            TKey key = identity(node);
            context.CancellationToken.ThrowIfCancellationRequested();
            if (seen.Contains(key))
            {
                continue;
            }

            if (seen.Count >= maxNodes)
            {
                return new MemoryGraphResult(MemoryGraphStop.NodeLimit, seen.Count, edgeCount, peak);
            }

            seen.Add(key);
            MemoryGraphVisit decision = visit(node, depth, context);
            context.CancellationToken.ThrowIfCancellationRequested();
            if (decision == MemoryGraphVisit.Stop)
            {
                return new MemoryGraphResult(MemoryGraphStop.VisitorStopped, seen.Count, edgeCount, peak);
            }

            if (decision == MemoryGraphVisit.SkipBranch)
            {
                continue;
            }

            foreach (TNode child in edges(node, context))
            {
                context.Charge(null, null, 0);
                if (edgeCount >= maxEdges)
                {
                    return new MemoryGraphResult(MemoryGraphStop.EdgeLimit, seen.Count, edgeCount, peak);
                }

                edgeCount++;
                if (depth == maxDepth)
                {
                    depthLimited = true;
                    continue;
                }

                int pending = (breadthFirst ? queue.Count : stack.Count) + staging.Count;
                if (pending >= maxPending)
                {
                    return new MemoryGraphResult(MemoryGraphStop.PendingLimit, seen.Count, edgeCount, Math.Max(peak, pending));
                }

                staging.Add((child, depth + 1));
            }

            context.CancellationToken.ThrowIfCancellationRequested();
            Schedule(staging, queue, stack, breadthFirst);
            peak = Math.Max(peak, breadthFirst ? queue.Count : stack.Count);
            staging.Clear();
        }

        context.CancellationToken.ThrowIfCancellationRequested();
        return new MemoryGraphResult(depthLimited ? MemoryGraphStop.DepthLimit : MemoryGraphStop.Exhausted, seen.Count, edgeCount, peak);
    }

    /// <summary>Appends an ordered frontier for BFS or reverses the push order so DFS visits its first child first.</summary>
    /// <typeparam name="TNode">Application node representation.</typeparam>
    /// <param name="nodes">Bounded staging frontier.</param>
    /// <param name="queue">BFS frontier.</param>
    /// <param name="stack">DFS frontier.</param>
    /// <param name="breadthFirst">Selected ordering.</param>
    private static void Schedule<TNode>(List<(TNode Node, int Depth)> nodes, Queue<(TNode Node, int Depth)> queue, Stack<(TNode Node, int Depth)> stack, bool breadthFirst)
    {
        if (breadthFirst)
        {
            foreach ((TNode node, int depth) in nodes)
            {
                queue.Enqueue((node, depth));
            }
        }
        else
        {
            for (int index = nodes.Count - 1; index >= 0; index--)
            {
                stack.Push(nodes[index]);
            }
        }
    }
}
