namespace CStructSharp.Tests;

using CStructSharp.Memory;

/// <summary>Selective snapshots and streamed graph order, bounded work and partial completion.</summary>
[TestClass]
public class MemoryReadSetAndGraphTests
{
    /// <summary>A missing merged gap never defeats independently readable selected fields.</summary>
    [TestMethod]
    public void ReadSets_RetryFieldsAcrossUnavailableMergedGaps()
    {
        MemorySchema schema = PortableMemorySchema.Create(new CStruct("struct r { uint8 first; uint8 gap[6]; uint8 last; };"), "r");
        var set = new MemoryReadSet(schema, "r", ["first", "last"], maxGapBytes: 6);
        var physical = new ByteArrayMemorySource("physical", new byte[] { 12, 34, });
        var source = new MappedMemorySource("sparse", [new(100, new(physical, 0, 1)), new(107, new(physical, 1, 1))]);
        MemorySnapshot snapshot = set.Capture(new MemoryRegion(source, 100, 8), new MemoryAccessContext(), MemoryReadMode.Coalesced);
        Assert.IsTrue(snapshot.TryRead(schema.Bind<byte>("r", "first"), new MemoryAccessContext(), out byte first));
        Assert.IsTrue(snapshot.TryRead(schema.Bind<byte>("r", "last"), new MemoryAccessContext(), out byte last));
        Assert.AreEqual((byte)12, first);
        Assert.AreEqual((byte)34, last);
        Assert.IsFalse(snapshot.Validity.IsKnown(1, 1));
        Assert.Throws<ArgumentException>(() => snapshot.TryRead(PortableMemorySchema.Create(new CStruct("struct r { uint8 first; };"), "r").Bind<byte>("r", "first"), new MemoryAccessContext(), out _));
    }

    /// <summary>Coalescing is explicit, reduces adjacent source requests, and captures owned bytes with exact schema decisions.</summary>
    [TestMethod]
    public void ReadSets_PreserveDefaultTransportAndOwnedStorage()
    {
        MemorySchema schema = PortableMemorySchema.Create(new CStruct("struct r { uint8 a; uint8 b; };"), "r");
        var set = new MemoryReadSet(schema, "r", ["b", "a"]);
        var source = new ByteArrayMemorySource("source", new byte[] { 1, 2, });
        var region = new MemoryRegion(source, 0, 2);
        var separate = new MemoryAccessContext();
        MemorySnapshot first = set.Capture(region, separate);
        var merged = new MemoryAccessContext();
        MemorySnapshot second = set.Capture(region, merged, MemoryReadMode.Coalesced);
        Assert.AreEqual(2, separate.Requests);
        Assert.AreEqual(1, merged.Requests);
        CollectionAssert.AreEqual(first.Bytes.ToArray(), second.Bytes.ToArray());
        source.Write(0, new byte[] { 9, }, new MemoryAccessContext());
        Assert.AreEqual((byte)1, first.Bytes.Span[0]);
        Assert.AreEqual("b", set.Fields[0].Path);
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryReadSet(schema, "r", ["b"], maxBufferBytes: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => set.Capture(new MemoryRegion(source, 0, 1), new MemoryAccessContext()));
    }

    /// <summary>Streaming BFS and preorder DFS preserve child order, canonical alias identity and the visitor's first result.</summary>
    [TestMethod]
    public void Graphs_PreserveOrderCyclesAliasesAndFirstResult()
    {
        Dictionary<int, int[]> edges = new() { [0] = [1, 2], [1] = [3, 0], [2] = [3], [3] = [], };
        var visited = new List<int>();

        // Repeated references and cycles share canonical identity but still charge edge work.
        Func<int, int, MemoryAccessContext, MemoryGraphVisit> visitBfs = (node, _, _) =>
        {
            visited.Add(node);
            return MemoryGraphVisit.Expand;
        };
        MemoryGraphResult bfs = MemoryGraph.Run([0], static node => node, visitBfs, (node, _) => edges[node], new MemoryAccessContext());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, }, visited.ToArray());
        Assert.AreEqual(5L, bfs.Edges);
        Assert.AreEqual(MemoryGraphStop.Exhausted, bfs.Stop);
        visited.Clear();

        // DFS must reverse stack push order, rather than reversing the application's children.
        Func<int, int, MemoryAccessContext, MemoryGraphVisit> visitDfs = (node, _, _) =>
        {
            visited.Add(node);
            return node == 3 ? MemoryGraphVisit.Stop : MemoryGraphVisit.Expand;
        };
        MemoryGraphResult dfs = MemoryGraph.Run([0], static node => node, visitDfs, (node, _) => edges[node], new MemoryAccessContext(), breadthFirst: false);
        CollectionAssert.AreEqual(new[] { 0, 1, 3, }, visited.ToArray());
        Assert.AreEqual(MemoryGraphStop.VisitorStopped, dfs.Stop);
    }

    /// <summary>An empty final edge sequence cannot hide callback-triggered cancellation.</summary>
    [TestMethod]
    public void Graphs_ObserveCancellationAfterEmptyFinalEdges()
    {
        using var cancellation = new CancellationTokenSource();

        // An empty iterator runs no loop body; the callback boundary still terminates the operation.
        IEnumerable<int> CancelEdges(int node, MemoryAccessContext context)
        {
            cancellation.Cancel();
            return Array.Empty<int>();
        }

        Assert.Throws<OperationCanceledException>(() => MemoryGraph.Run([0], static node => node, static (_, _, _) => MemoryGraphVisit.Expand, CancelEdges, new MemoryAccessContext { CancellationToken = cancellation.Token }));
    }

    /// <summary>Depth, edge, node and frontier limits remain distinguishable; cache hits cannot bypass cancellation.</summary>
    [TestMethod]
    public void Graphs_ReportEveryLimitAndPropagateCancellation()
    {
        // A chain exceeds depth while retaining all already visited nodes.
        MemoryGraphResult depth = MemoryGraph.Run([0], static node => node, static (_, _, _) => MemoryGraphVisit.Expand, static (node, _) => new[] { node + 1, }, new MemoryAccessContext(), maxDepth: 1);
        Assert.AreEqual(MemoryGraphStop.DepthLimit, depth.Stop);
        Assert.AreEqual(2, depth.Nodes);

        // Every edge, including a duplicate, is budgeted independently of the visited set.
        Assert.AreEqual(MemoryGraphStop.EdgeLimit, MemoryGraph.Run([0], static node => node, static (_, _, _) => MemoryGraphVisit.Expand, static (_, _) => new[] { 0, 0, }, new MemoryAccessContext(), maxEdges: 1).Stop);
        Assert.AreEqual(MemoryGraphStop.NodeLimit, MemoryGraph.Run([0, 1], static node => node, static (_, _, _) => MemoryGraphVisit.SkipBranch, static (_, _) => Array.Empty<int>(), new MemoryAccessContext(), maxNodes: 1).Stop);
        Assert.AreEqual(MemoryGraphStop.PendingLimit, MemoryGraph.Run([0, 1], static node => node, static (_, _, _) => MemoryGraphVisit.SkipBranch, static (_, _) => Array.Empty<int>(), new MemoryAccessContext(), maxPending: 1).Stop);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Even an empty/cached graph observes cancellation before enumeration.
        Assert.Throws<OperationCanceledException>(() => MemoryGraph.Run(Array.Empty<int>(), static node => node, static (_, _, _) => MemoryGraphVisit.SkipBranch, static (_, _) => Array.Empty<int>(), new MemoryAccessContext { CancellationToken = cancellation.Token }));
    }
}
