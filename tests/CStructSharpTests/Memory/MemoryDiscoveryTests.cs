namespace CStructSharp.Tests;

using CStructSharp.Memory;

/// <summary>Known-byte, boundary, lifetime, identity and bounded-work contracts for discovery primitives.</summary>
[TestClass]
public class MemoryDiscoveryTests
{
    /// <summary>Unknown scan bytes are aggregate coverage, without one diagnostic object per rejected byte.</summary>
    [TestMethod]
    public void Windows_UnknownCoverageDoesNotAllocatePerByte()
    {
        var source = new IslandSource(0, new byte[10_000], [], 1);
        MemoryRegion[] ranges = [new(source, 0, 10_000)];

        // Warm the bounded pool before observing operation-owned allocations.
        MemoryScan.Run(ranges, static (_, _) => true, new MemoryAccessContext());
        var context = new MemoryAccessContext();
        long before = GC.GetAllocatedBytesForCurrentThread();

        // No callback runs, because every byte is unknown. Each skipped byte still pays request work.
        MemoryScanResult result = MemoryScan.Run(ranges, static (_, _) => throw new InvalidOperationException(), context);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(10_000L, result.UnavailableBytes);
        Assert.IsLessThan(16_384L, allocated, "Transport holes must not allocate a diagnostic for each byte.");
    }

    /// <summary>Callback-triggered cancellation terminates a probe even if its predicate returns a passing value.</summary>
    [TestMethod]
    public void BufferProbe_CallbackCancellationCannotReturnAMatch()
    {
        MemorySchema schema = PortableMemorySchema.Create(new CStruct("struct r { uint8 value; };"), "r");
        using var cancellation = new CancellationTokenSource();

        // A callback may cancel the shared operation while completing its own pure value check.
        var probe = new MemoryFieldProbe<byte>("cancel", schema.Bind<byte>("r", "value"), value =>
        {
            cancellation.Cancel();
            return value == 1;
        });
        Assert.Throws<OperationCanceledException>(() => probe.Evaluate(new byte[] { 1, }, new MemoryAccessContext { CancellationToken = cancellation.Token }));
    }

    /// <summary>Interleaved source ranges retain per-source order validation; final-window callbacks cannot hide a generation change.</summary>
    [TestMethod]
    public void Windows_ValidateInterleavedSourcesAndFinalGeneration()
    {
        var first = new ByteArrayMemorySource("same", new byte[8]);
        var second = new ByteArrayMemorySource("same", new byte[8]);

        // A repeated source cannot overlap a previous interval merely because another source was between them.
        Assert.Throws<ArgumentException>(() => MemoryScan.Run([new(first, 0, 4), new(second, 0, 4), new(first, 2, 4)], static (_, _) => true, new MemoryAccessContext()));

        // Cancellation and generation are checked after the last callback as well as before reads.
        MemoryScanVisitor change = (_, _) =>
        {
            first.Write(0, new byte[] { 1, }, new MemoryAccessContext());
            return false;
        };
        MemoryScanResult result = MemoryScan.Run([new(first, 0, 4)], change, new MemoryAccessContext());
        Assert.AreEqual(MemoryScanStop.SourceChanged, result.Stop);
    }

    /// <summary>Positive short reads continue and an unavailable suffix remains untouched, even at the final unsigned address.</summary>
    [TestMethod]
    public void PartialRead_PreservesConfirmedPrefixAndFailureCoordinate()
    {
        var source = new IslandSource(ulong.MaxValue - 4, [1, 2, 3, 4, 5], [new(0, 3)], 1);
        var bytes = new byte[] { 99, 99, 99, 99, 99, };
        MemoryReadResult result = new MemoryRegion(source, ulong.MaxValue - 4, 5).ReadPartial(bytes, new MemoryAccessContext());
        Assert.AreEqual(3, result.BytesRead);
        Assert.IsFalse(result.IsComplete);
        Assert.AreEqual(ulong.MaxValue - 1, result.Failure!.Address);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 99, 99, }, bytes);
    }

    /// <summary>Presentation zeroes inside a required-zero field are unknown; selected fields do not require an unrelated tail.</summary>
    [TestMethod]
    public void BufferProbe_RequiresEveryStorageByteButOnlyItsOwnExtent()
    {
        MemorySchema schema = PortableMemorySchema.Create(new CStruct("struct record { uint64 data; uint8 tail[100]; };"), "record");

        // The predicate is a pure local null-data gate; a match alone does not identify a live record.
        var probe = new MemoryFieldProbe<ulong>("data-null", schema.Bind<ulong>("record", "data"), static value => value == 0);
        var context = new MemoryAccessContext();
        Assert.AreEqual(MemoryProbeOutcome.Match, probe.Evaluate(new byte[8], context));
        Assert.AreEqual(MemoryProbeOutcome.Unavailable, probe.Evaluate(new byte[7], context));
        var validity = new MemoryValidity(8, [new(0, 3), new(4, 4)]);
        Assert.AreEqual(MemoryProbeOutcome.Unavailable, probe.Evaluate(new byte[8], context, validity));
        Assert.AreEqual(MemoryProbeOutcome.Mismatch, probe.Evaluate(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, }, context));
        Assert.Throws<ArgumentException>(() => probe.Evaluate(new byte[9], context, validity));
    }

    /// <summary>Signed byte arrays preserve bits; the prepared reader leaves excess destination bytes alone and borrows complete fields.</summary>
    [TestMethod]
    public void PreparedBytes_PreserveRawStorageAndSchemaIdentity()
    {
        var layout = new CStruct("struct record { int8 name[4]; uint32 tail; };");
        MemorySchema schema = PortableMemorySchema.Create(layout, "record");
        MemoryByteReader reader = schema.BindBytes("record", "name");
        byte[] bytes = [255, 0, 127, 128, 0, 0, 0, 0];
        var root = new MemoryRegion(new ByteArrayMemorySource("bytes", bytes), 0, 8);
        byte[] destination = [9, 9, 9, 9, 9];
        var context = new MemoryAccessContext();
        Assert.IsTrue(reader.Read(new MemorySession(schema), root, destination, context).IsComplete);
        CollectionAssert.AreEqual(new byte[] { 255, 0, 127, 128, 9, }, destination);
        Assert.IsTrue(reader.TryReadBuffer(bytes.AsSpan(0, 4), context, out ReadOnlySpan<byte> borrowed));
        CollectionAssert.AreEqual(bytes[..4], borrowed.ToArray());
        Assert.IsFalse(reader.TryReadBuffer(bytes, context, out _, new MemoryValidity(8, [new(0, 3)])));
        Assert.Throws<ArgumentException>(() => reader.Read(new MemorySession(PortableMemorySchema.Create(layout, "record")), root, destination, context));
    }

    /// <summary>Raw byte bindings reject wrong types and destinations, follow pointers only with a session and never borrow unknown bytes.</summary>
    [TestMethod]
    public void PreparedBytes_EnforceExtentAndPointerPolicies()
    {
        MemorySchema schema = PortableMemorySchema.Create(new CStruct("struct child { uint8 text[2]; }; struct root { uint32 wide[2]; child *p; uint8 text[2]; };"), "root");
        MemoryByteReader direct = schema.BindBytes("root", "text");
        var session = new MemorySession(schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("bytes", new byte[18]), 0, 18);
        Assert.Throws<ArgumentException>(() => schema.BindBytes("root", "wide"));
        Assert.Throws<ArgumentException>(() => schema.BindBytes("root", "p"));
        Assert.Throws<ArgumentException>(() => direct.Read(session, region, new byte[1], new MemoryAccessContext()));
        Assert.IsFalse(direct.Read(session, new MemoryRegion(region.Source, 0, 8), new byte[2], new MemoryAccessContext()).IsComplete);
        Assert.IsFalse(direct.TryReadBuffer(new byte[1], new MemoryAccessContext(), out _));
        Assert.IsFalse(direct.TryReadBuffer(new byte[17], new MemoryAccessContext(), out _));
        Assert.Throws<ArgumentException>(() => direct.TryReadBuffer(new byte[18], new MemoryAccessContext(), out _, new MemoryValidity(1, [])));
        var physical = new ByteArrayMemorySource("pointer", new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 18, 0, 0, 0, 0, 0, 0, 0, 0, 0, 65, 66, });
        MemoryByteReader indirect = schema.BindBytes("root", "p.value.text");
        Assert.Throws<NotSupportedException>(() => indirect.TryReadBuffer(new byte[20], new MemoryAccessContext(), out _));
        byte[] output = new byte[2];
        Assert.IsTrue(indirect.Read(session, new MemoryRegion(physical, 0, 18), output, new MemoryAccessContext()).IsComplete);
        CollectionAssert.AreEqual(new byte[] { 65, 66, }, output);
    }

    /// <summary>Validity merges adjacency but never accepts a hole, overflow, negative or out-of-buffer extent.</summary>
    [TestMethod]
    public void Validity_UsesExactKnownIntervals()
    {
        var validity = new MemoryValidity(12, [new(4, 2), new(0, 4), new(9, 3)]);
        Assert.IsTrue(validity.IsKnown(0, 6));
        Assert.IsFalse(validity.IsKnown(5, 5));
        Assert.IsTrue(validity.IsKnown(12, 0));
        Assert.IsFalse(validity.IsKnown(int.MaxValue, int.MaxValue));
        Assert.IsFalse(validity.IsKnown(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryValidity(4, [new(3, 2)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryValidity(4, [new(0, 1), new(1, 1)], maxRanges: 1));
    }

    /// <summary>Every boundary-spanning candidate has one owner, adjacent extents can join, and overlap bytes are not reread.</summary>
    [TestMethod]
    public void Windows_ReuseOverlapAndOwnStartsOnce()
    {
        var source = new IslandSource(0x1001, Enumerable.Repeat((byte)1, 13).ToArray(), [new(0, 13)], 2);
        var hits = new List<ulong>();
        var context = new MemoryAccessContext();

        // Retain complete three-byte records beginning in this window's owned interval.
        MemoryScanVisitor visit = (window, _) =>
        {
            for (int offset = window.OwnedOffset; offset < window.OwnedOffset + window.OwnedLength && offset <= window.Bytes.Length - 3; offset++)
            {
                hits.Add(window.Address + (ulong)offset);
            }

            return true;
        };
        MemoryScanResult result = MemoryScan.Run([new(source, 0x1001, 6), new(source, 0x1007, 7)], visit, context, windowBytes: 4, overlapBytes: 2);
        CollectionAssert.AreEqual(Enumerable.Range(0, 11).Select(offset => 0x1001UL + (ulong)offset).ToArray(), hits.ToArray());
        Assert.AreEqual(13L, result.ReadableBytes);
        Assert.AreEqual(13L, source.Copied);
        Assert.AreEqual(0L, result.UnavailableBytes);
        Assert.AreEqual(MemoryScanStop.RangeExhausted, result.Stop);
    }

    /// <summary>Unknown bytes break continuity while later readable islands and the final unsigned byte remain searchable.</summary>
    [TestMethod]
    public void Windows_RetainLaterIslandsWithoutJoiningGaps()
    {
        ulong start = ulong.MaxValue - 11;
        var source = new IslandSource(start, Enumerable.Repeat((byte)7, 12).ToArray(), [new(0, 3), new(5, 7)], 2);
        var hits = new List<ulong>();

        // A two-byte record needs both bytes in the same known window.
        MemoryScanVisitor visit = (window, _) =>
        {
            for (int offset = window.OwnedOffset; offset < window.OwnedOffset + window.OwnedLength && offset + 1 < window.Bytes.Length; offset++)
            {
                hits.Add(window.Address + (ulong)offset);
            }

            return true;
        };
        MemoryScanResult result = MemoryScan.Run([new(source, start, 12)], visit, new MemoryAccessContext(), windowBytes: 4, overlapBytes: 1);
        CollectionAssert.AreEqual(new[] { start, start + 1, start + 5, start + 6, start + 7, start + 8, start + 9, start + 10, }, hits.ToArray());
        Assert.AreEqual(10L, result.ReadableBytes);
        Assert.AreEqual(2L, result.UnavailableBytes);
    }

    /// <summary>Equal diagnostic labels never join distinct sources, and completion reports limits and callback stops explicitly.</summary>
    [TestMethod]
    public void Windows_SeparateIdentitiesAndReportEarlyStops()
    {
        var first = new ByteArrayMemorySource("same-label", new byte[] { 1, 2, });
        var second = new ByteArrayMemorySource("same-label", new byte[] { 3, 4, });
        int callbacks = 0;

        // Each source is a separate address space, despite equal labels and adjacent coordinates.
        MemoryScanVisitor countVisit = (_, _) =>
        {
            callbacks++;
            return true;
        };
        MemoryScanResult result = MemoryScan.Run([new(first, 0, 2), new(second, 0, 2)], countVisit, new MemoryAccessContext(), windowBytes: 2, overlapBytes: 1, maxWindows: 1);
        Assert.AreEqual(1, callbacks);
        Assert.AreEqual(MemoryScanStop.WindowLimit, result.Stop);

        // A consumer's explicit hit limit is distinguishable from range exhaustion.
        Assert.AreEqual(MemoryScanStop.VisitorStopped, MemoryScan.Run([new(first, 0, 2)], (_, _) => false, new MemoryAccessContext()).Stop);
    }

    /// <summary>CPU and transport work remain bounded on holes, cancellation propagates, and generations cannot silently mix.</summary>
    [TestMethod]
    public void Windows_RespectCancellationBudgetsAndGenerations()
    {
        var source = new IslandSource(0, new byte[20], [], 1);

        // A wholly unavailable range cannot become an unbounded retry loop.
        Assert.Throws<MemoryAccessException>(() => MemoryScan.Run([new(source, 0, 20)], (_, _) => true, new MemoryAccessContext { MaxRequests = 4 }));
        using var cancellation = new CancellationTokenSource();
        var context = new MemoryAccessContext { CancellationToken = cancellation.Token };
        var known = new ByteArrayMemorySource("known", new byte[10]);

        // Cancelling in CPU work terminates before the next callback or read.
        MemoryScanVisitor cancelVisit = (_, _) =>
        {
            cancellation.Cancel();
            return true;
        };
        Assert.Throws<OperationCanceledException>(() => MemoryScan.Run([new(known, 0, 10)], cancelVisit, context, windowBytes: 2));
        var changing = new IslandSource(0, new byte[4], [new(0, 4)], 4) { ChangeOnRead = true, };

        // A changed generation prevents delivery of a mixed window.
        Assert.AreEqual(MemoryScanStop.SourceChanged, MemoryScan.Run([new(changing, 0, 4)], (_, _) => throw new InvalidOperationException(), new MemoryAccessContext()).Stop);
    }

    /// <summary>Inspection follows dynamic provenance, preserving aliases, process/source identity and noncontiguous frames.</summary>
    [TestMethod]
    public void DynamicProvenance_IsDemandDrivenAndPreservesFragments()
    {
        MemorySchema schema = PortableMemorySchema.Create(new CStruct("struct record { uint8 bytes[4]; };"), "record");
        var physical = new ByteArrayMemorySource("physical", new byte[] { 1, 2, 9, 9, 3, 4, });
        var mapping = new MappedMemorySource("virtual", [new(100, new(physical, 0, 2)), new(102, new(physical, 4, 2)), new(200, new(physical, 0, 2))]);
        var source = new DynamicSource(mapping);
        var region = new MemoryRegion(source, 100, 4);
        Assert.AreEqual(0, source.Descriptions);
        MemoryInspection inspection = new MemorySession(schema).Inspect(region, "record");
        Assert.AreEqual(1, source.Descriptions);
        CollectionAssert.AreEqual(new[] { 0UL, 4UL, }, inspection.BackingRegions.Select(fragment => fragment.Address).ToArray());
        Assert.AreSame(physical, inspection.BackingRegions[0].Source);
        Assert.AreEqual(0UL, new MemoryRegion(source, 200, 2).DescribeBacking()[0].Address);
    }

    /// <summary>A finite synthetic source returning positive short prefixes and explicit holes.</summary>
    /// <param name="start">Unsigned logical origin.</param>
    /// <param name="bytes">Stable storage.</param>
    /// <param name="ranges">Known bytes only.</param>
    /// <param name="maximumRead">Maximum positive transport result.</param>
    private sealed class IslandSource(ulong start, byte[] bytes, MemoryByteRange[] ranges, int maximumRead) : IMemorySource
    {
        private readonly MemoryValidity validity = new(bytes.Length, ranges);

        /// <inheritdoc/>
        public string Id => "synthetic-islands";

        /// <inheritdoc/>
        public long Generation { get; private set; }

        /// <summary>Gets or sets whether each read changes the generation.</summary>
        public bool ChangeOnRead { get; set; }

        /// <summary>Gets confirmed payload bytes copied, excluding request attempts.</summary>
        public long Copied { get; private set; }

        /// <inheritdoc/>
        public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
        {
            context.Charge(this.Id, address, destination.Length);
            if (this.ChangeOnRead)
            {
                this.Generation++;
            }

            if (address < start || address - start >= (ulong)bytes.Length)
            {
                return 0;
            }

            int offset = (int)(address - start);
            int count = 0;
            while (count < Math.Min(maximumRead, destination.Length) && this.validity.IsKnown(offset + count, 1))
            {
                destination[count] = bytes[offset + count];
                count++;
            }

            this.Copied += count;
            return count;
        }
    }

    /// <summary>Models a dynamically translated source, deliberately distinct from a static mapped-source implementation.</summary>
    /// <param name="mapping">Synthetic page-table result.</param>
    private sealed class DynamicSource(MappedMemorySource mapping) : IMemoryMappingSource
    {
        /// <inheritdoc/>
        public string Id => "dynamic";

        /// <inheritdoc/>
        public long Generation => mapping.Generation;

        /// <summary>Gets demand-driven mapping requests.</summary>
        public int Descriptions { get; private set; }

        /// <inheritdoc/>
        public IReadOnlyList<MemoryRegion> Describe(ulong address, int length, MemoryAccessContext? context = null)
        {
            this.Descriptions++;
            return mapping.Describe(address, length, context);
        }

        /// <inheritdoc/>
        public int Read(ulong address, Span<byte> destination, MemoryAccessContext context) => mapping.Read(address, destination, context);
    }
}
