namespace CStructSharp.Tests;

using CStructSharp.Memory;

/// <summary>Adapter conformance tests for short reads, generation changes, independent byte ownership, and bounded traversal.</summary>
[TestClass]
public class MemorySourceContractTests
{
    /// <summary>Mapped generations do not overflow when several pages share a large externally supplied snapshot number.</summary>
    [TestMethod]
    public void MappedGenerations_TrackDistinctSourcesWithoutSummingSnapshots()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2, });
        var snapshot = new StreamMemorySource("snapshot", stream, long.MaxValue);
        var mutable = new ByteArrayMemorySource("mutable", new byte[] { 3, });
        var mapped = new MappedMemorySource("map", [new(100, new MemoryRegion(snapshot, 0, 1)), new(101, new MemoryRegion(snapshot, 1, 1)), new(102, new MemoryRegion(mutable, 0, 1)),]);
        var cache = new CachedMemorySource("cache", mapped);
        byte[] bytes = new byte[3];
        Assert.AreEqual(0L, mapped.Generation);
        Assert.AreEqual(3, cache.Read(100, bytes, new MemoryAccessContext()));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, bytes);
        mutable.Write(0, new byte[] { 9, }, new MemoryAccessContext());
        Assert.AreEqual(1L, mapped.Generation);
        Assert.AreEqual(1L, mapped.Generation);
        Assert.AreEqual(3, cache.Read(100, bytes, new MemoryAccessContext()));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 9, }, bytes);
    }

    /// <summary>Short reads continue across a mapping, while an absent byte and malformed adapter count are distinct failures.</summary>
    [TestMethod]
    public void MappedSources_HandleShortAndInvalidReads()
    {
        var source = new ControlledSource();
        var mapped = new MappedMemorySource("logical", [new(100, new MemoryRegion(source, 0, 4)),]);
        byte[] buffer = new byte[4];
        Assert.AreEqual(4, mapped.Read(100, buffer, new MemoryAccessContext()));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, }, buffer);
        Assert.AreEqual(4, source.Reads);
        Assert.AreEqual(0L, mapped.Generation);
        source.ChangeGeneration = true;
        mapped.Read(100, buffer.AsSpan(0, 1), new MemoryAccessContext());
        Assert.AreEqual(1L, mapped.Generation);
        source.ReturnCount = 0;
        MemoryAccessException missing = Assert.Throws<MemoryAccessException>(() => mapped.Read(100, buffer, new MemoryAccessContext()));
        Assert.AreEqual(MemoryFailure.MissingBytes, missing.Failure);
        StringAssert.Contains(missing.Message, "unavailable");
        source.ReturnCount = 5;
        Assert.Throws<MemoryAccessException>(() => mapped.Read(100, buffer, new MemoryAccessContext()));
        source.ReturnCount = -1;
        using Stream view = new MemoryRegion(source, 0, 4).OpenRead();
        Assert.Throws<MemoryAccessException>(() => view.Read(buffer));
        Assert.Throws<ArgumentOutOfRangeException>(() => mapped.Describe(100, -1));
        Assert.AreEqual(0, mapped.Describe(100, 0).Count);
        MemoryAccessException hole = Assert.Throws<MemoryAccessException>(() => mapped.Describe(99, 1));
        Assert.AreEqual(MemoryFailure.Unmapped, hole.Failure);
        Assert.AreEqual("logical", hole.SourceId);
        Assert.AreEqual(99UL, hole.Address);
    }

    /// <summary>Cache misses, bypasses, short reads, stale generations, and invalid counts obey the backing contract.</summary>
    [TestMethod]
    public void Cache_HandlesPartialReadsAndChangingSnapshots()
    {
        var source = new ControlledSource();
        var cached = new CachedMemorySource("cache", source, 1);
        byte[] bytes = new byte[2];
        Assert.AreEqual(1, cached.Read(0, bytes, new MemoryAccessContext()));
        Assert.AreEqual(1, cached.Read(0, bytes, new MemoryAccessContext()));
        Assert.AreEqual(2, source.Reads);
        source.ReturnCount = 2;
        cached.Read(0, bytes, new MemoryAccessContext());
        cached.Read(0, bytes, new MemoryAccessContext());
        Assert.AreEqual(4, source.Reads);
        source.ReturnCount = 1;
        source.ChangeGeneration = true;
        MemoryAccessException stale = Assert.Throws<MemoryAccessException>(() => cached.Read(0, bytes, new MemoryAccessContext()));
        Assert.AreEqual(MemoryFailure.StaleSource, stale.Failure);
        Assert.AreEqual(source.Generation, cached.Generation);
        source.ChangeGeneration = false;
        source.ReturnCount = 3;
        Assert.AreEqual(MemoryFailure.SourceFailure, Assert.Throws<MemoryAccessException>(() => cached.Read(0, bytes, new MemoryAccessContext())).Failure);
        source.ReturnCount = -1;
        Assert.AreEqual(MemoryFailure.SourceFailure, Assert.Throws<MemoryAccessException>(() => cached.Read(0, bytes, new MemoryAccessContext())).Failure);
    }

    /// <summary>Overlays reject stale backing, check capacity before mutation, and do not leak patch byte arrays.</summary>
    [TestMethod]
    public void Overlay_RejectsStaleBackingAndPreservesPreparedBytes()
    {
        var source = new ByteArrayMemorySource("image", new byte[4]);
        var overlay = new OverlayMemorySource("copy", source, 2);
        var region = new MemoryRegion(overlay, 0, 2);
        byte[] replacement = [7, 9,];
        MemoryPatch patch = MemoryPatch.Create(region, replacement);
        replacement[0] = 1;
        patch.Fragments[0].Expected[0] = 2;
        patch.Fragments[0].Replacement[0] = 3;
        Assert.AreEqual(region, patch.LogicalRegion);
        Assert.AreEqual(0L, patch.Fragments[0].Generation);
        CollectionAssert.AreEqual(new byte[2], patch.Fragments[0].Expected);
        CollectionAssert.AreEqual(new byte[] { 7, 9, }, patch.Fragments[0].Replacement);
        patch.Commit();
        Assert.AreEqual(1L, overlay.Generation);
        overlay.Write(0, new byte[] { 8, 6, }, new MemoryAccessContext());
        Assert.AreEqual(2L, overlay.Generation);
        byte[] bytes = new byte[4];
        Assert.AreEqual(4, overlay.Read(0, bytes, new MemoryAccessContext()));
        CollectionAssert.AreEqual(new byte[] { 8, 6, 0, 0, }, bytes);
        Assert.AreEqual(MemoryFailure.BudgetExceeded, Assert.Throws<MemoryAccessException>(() => overlay.Write(2, new byte[] { 1, }, new MemoryAccessContext())).Failure);
        source.Write(3, new byte[] { 1, }, new MemoryAccessContext());
        Assert.AreEqual(MemoryFailure.StaleSource, Assert.Throws<MemoryAccessException>(() => overlay.Read(0, bytes, new MemoryAccessContext())).Failure);
        Assert.AreEqual(MemoryFailure.StaleSource, Assert.Throws<MemoryAccessException>(() => overlay.Write(0, new byte[] { 1, }, new MemoryAccessContext())).Failure);
    }

    /// <summary>Every traversal termination category is observable, and children cannot exceed the shared work budget.</summary>
    [TestMethod]
    public void Walkers_BoundNodesAndRetainUnavailableDetails()
    {
        var source = new ByteArrayMemorySource("image", new byte[8]);
        var root = new MemoryRegion(source, 0, 1);

        // Incrementing links never reach the head, so a node limit must stop the walk.
        MemoryWalkResult list = MemoryWalker.SentinelList(root, (node, _) => new MemoryRegion(source, node.Address + 1, 1), maxNodes: 2);
        Assert.AreEqual(MemoryWalkStop.NodeLimit, list.Stop);
        Assert.AreEqual(2, list.Nodes.Count);
        Assert.AreEqual(1UL, list.Nodes[0].Address);

        // A self-loop is deduplicated by the tree walker.
        MemoryWalkResult cycle = MemoryWalker.Tree(root, (_, _) => [root,]);
        Assert.AreEqual(MemoryWalkStop.Complete, cycle.Stop);
        Assert.AreEqual(1, cycle.Nodes.Count);

        // A fresh child beyond the node cap is not returned.
        MemoryWalkResult tree = MemoryWalker.Tree(root, (_, _) => [new MemoryRegion(source, 1, 1),], maxNodes: 1);
        Assert.AreEqual(MemoryWalkStop.NodeLimit, tree.Stop);
        Assert.AreEqual(1, tree.Nodes.Count);
        var failure = new MemoryAccessException(MemoryFailure.Unmapped, "image", 3, 1, "missing page");

        // Acquisition failures retain both the completed prefix and the original structured error.
        MemoryWalkResult missing = MemoryWalker.Tree(root, (_, _) => throw failure);
        Assert.AreEqual(MemoryWalkStop.Unavailable, missing.Stop);
        Assert.AreSame(failure, missing.Failure);

        // A list head failure returns an empty prefix.
        MemoryWalkResult missingList = MemoryWalker.SentinelList(root, (_, _) => throw failure);
        Assert.AreEqual(MemoryWalkStop.Unavailable, missingList.Stop);
        Assert.AreEqual(0, missingList.Nodes.Count);
        Assert.AreSame(failure, missingList.Failure);
        Assert.Throws<ArgumentOutOfRangeException>(() => MemoryWalker.Tree(root, (_, _) => [], maxNodes: 0));
        Assert.Throws<MemoryAccessException>(() => MemoryWalker.Tree(root, (_, _) => [root, root, root,], context: new MemoryAccessContext(maxRequests: 2)));
    }

    /// <summary>Configurable positional source for short reads and generation races; owns four deterministic bytes.</summary>
    private sealed class ControlledSource : IMemorySource
    {
        private readonly byte[] bytes = [1, 2, 3, 4,];

        public string Id => "controlled";

        public long Generation { get; private set; }

        internal int ReturnCount { get; set; } = 1;

        internal int Reads { get; private set; }

        internal bool ChangeGeneration { get; set; }

        /// <inheritdoc/>
        public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
        {
            context.Charge(this.Id, address, destination.Length);
            this.Reads++;
            if (this.ChangeGeneration)
            {
                this.Generation++;
            }

            if (this.ReturnCount > 0 && this.ReturnCount <= destination.Length)
            {
                this.bytes.AsSpan((int)address, this.ReturnCount).CopyTo(destination);
            }

            return this.ReturnCount;
        }
    }
}
