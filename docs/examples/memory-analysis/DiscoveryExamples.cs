namespace CStructSharp.Examples.MemoryAnalysis;

using CStructSharp.Memory;

/// <summary>Checks prepared recognition, explicit validity and selected capture over a tiny synthetic header.</summary>
internal static class DiscoveryExamples
{
    /// <summary>Executes the worked discovery example and fails on invented zero evidence or duplicate window ownership.</summary>
    internal static void Run()
    {
        var layout = new CStruct("struct Header { uint32 tag @magic(tag == 7); uint32 reserved; };");
        byte[] bytes = [7, 0, 0, 0, 0, 0, 0, 0];
        PreparedMatcher matcher = layout.PrepareMatcher("Header");
        MemorySchema schema = PortableMemorySchema.Create(layout, "Header");
        MemoryFieldReader<uint> reserved = schema.Bind<uint>("Header", "reserved");

        // Recognition and the required-zero predicate have separate purposes and coverage.
        var probe = new MemoryFieldProbe<uint>("reserved-zero", reserved, value => value == 0);
        var context = new MemoryAccessContext();
        var partial = new MemoryValidity(8, [new(0, 4)]);
        if (!matcher.Matches(bytes) || probe.Evaluate(bytes, context, partial) != MemoryProbeOutcome.Unavailable)
        {
            throw new InvalidOperationException("Unknown storage became evidence.");
        }

        var source = new ByteArrayMemorySource("synthetic-header", bytes);
        var region = new MemoryRegion(source, 0, bytes.Length);
        var set = new MemoryReadSet(schema, "Header", ["tag", "reserved"]);
        MemorySnapshot snapshot = set.Capture(region, context, MemoryReadMode.Coalesced);
        if (!snapshot.TryRead(reserved, context, out uint value) || value != 0)
        {
            throw new InvalidOperationException("Complete selected capture differs.");
        }

        int owned = 0;

        // Each logical candidate start belongs to exactly one callback, even with overlap.
        MemoryScanResult scan = MemoryScan.Run([region], (window, budget) =>
        {
            budget.CancellationToken.ThrowIfCancellationRequested();
            owned += window.OwnedLength;
            return true;
        }, context, windowBytes: 3, overlapBytes: 2);
        if (owned != 8 || scan.ReadableBytes != 8 || scan.Stop != MemoryScanStop.RangeExhausted)
        {
            throw new InvalidOperationException("Scan ownership differs.");
        }
    }
}
