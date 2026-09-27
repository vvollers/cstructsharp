namespace CStructSharp.Benchmarks;

using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Benchmarks.Scenarios;
using CStructSharp.Values;

/// <summary>
///     Segmented input and record sequences: a <see cref="ReadOnlySequence{T}"/> of one segment (read in place)
///     and of four segments (copied into a pooled buffer) against the span for the record and the nested fixture,
///     runtime and generated; 256 consecutive records through <c>ParseMany</c> and the generated <c>Records</c>
///     against the loop a caller would write with <c>Parse</c> and an offset; and the view enumerator against the
///     offset loop over views.
/// </summary>
[BenchmarkCategory("Sequences")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class SequenceBenchmarks
{
    private FixtureCase primRecord = null!;
    private FixtureCase nested = null!;
    private ReadOnlySequence<byte> primRecordSingle;
    private ReadOnlySequence<byte> primRecordSplit;
    private ReadOnlySequence<byte> nestedSingle;
    private ReadOnlySequence<byte> nestedSplit;
    private byte[] records256 = null!;
    private int recordSize;

    /// <summary>Loads the fixtures, checks them against their generated layouts, builds the one- and four-segment sequences, and repeats the record 256 times.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.LoadMatching("prim-le-record", typeof(PrimRecordLayout));
        this.nested = FixtureCase.LoadMatching("nested-x256", typeof(NestedLayout));
        this.primRecordSingle = new ReadOnlySequence<byte>(this.primRecord.Bytes);
        this.primRecordSplit = Split(this.primRecord.Bytes, 4);
        this.nestedSingle = new ReadOnlySequence<byte>(this.nested.Bytes);
        this.nestedSplit = Split(this.nested.Bytes, 4);
        this.recordSize = this.primRecord.Layout.GetStructSizeInBytes("root");
        this.records256 = new byte[this.recordSize * 256];
        for (int index = 0; index < 256; index++)
        {
            this.primRecord.Bytes.CopyTo(this.records256, index * this.recordSize);
        }
    }

    // ---- prim-le-record ---------------------------------------------------------------------------------------------

    /// <summary>Runtime <c>Parse</c> of the 28-byte record from a span: the group's reference.</summary>
    /// <returns>The parsed record.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PrimRecord")]
    public StructValue Runtime_PrimRecord_Span() => this.primRecord.Layout.Parse(this.primRecord.Bytes.AsSpan(), "root");

    /// <summary>Runtime <c>Parse</c> of the record from a one-segment sequence, read in place.</summary>
    /// <returns>The parsed record.</returns>
    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public StructValue Runtime_PrimRecord_SingleSegment() => this.primRecord.Layout.Parse(this.primRecordSingle, "root");

    /// <summary>Runtime <c>Parse</c> of the record from four segments, copied into a pooled buffer.</summary>
    /// <returns>The parsed record.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "PrimRecord")]
    public StructValue Runtime_PrimRecord_FourSegments() => this.primRecord.Layout.Parse(this.primRecordSplit, "root");

    /// <summary>Generated <c>Parse</c> of the record from a span.</summary>
    /// <returns>The generated record.</returns>
    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public PrimRecordLayout.Root Generated_PrimRecord_Span() => PrimRecordLayout.Parse(this.primRecord.Bytes);

    /// <summary>Generated <c>Parse</c> of the record from a one-segment sequence.</summary>
    /// <returns>The generated record.</returns>
    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public PrimRecordLayout.Root Generated_PrimRecord_SingleSegment() => PrimRecordLayout.Parse(this.primRecordSingle);

    /// <summary>Generated <c>Parse</c> of the record from four segments.</summary>
    /// <returns>The generated record.</returns>
    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public PrimRecordLayout.Root Generated_PrimRecord_FourSegments() => PrimRecordLayout.Parse(this.primRecordSplit);

    // ---- nested-x256: 6,400 bytes --------------------------------------------------------------------------------------

    /// <summary>Runtime <c>Parse</c> of the 256 nested records from a span: the group's reference.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Nested256")]
    public StructValue Runtime_Nested256_Span() => this.nested.Layout.Parse(this.nested.Bytes.AsSpan(), "root");

    /// <summary>Runtime <c>Parse</c> of the nested records from four segments.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public StructValue Runtime_Nested256_FourSegments() => this.nested.Layout.Parse(this.nestedSplit, "root");

    /// <summary>Generated <c>Parse</c> of the nested records from a span.</summary>
    /// <returns>The generated root.</returns>
    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public NestedLayout.Root Generated_Nested256_Span() => NestedLayout.Parse(this.nested.Bytes);

    /// <summary>Generated <c>Parse</c> of the nested records from a one-segment sequence.</summary>
    /// <returns>The generated root.</returns>
    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public NestedLayout.Root Generated_Nested256_SingleSegment() => NestedLayout.Parse(this.nestedSingle);

    /// <summary>Generated <c>Parse</c> of the nested records from four segments.</summary>
    /// <returns>The generated root.</returns>
    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public NestedLayout.Root Generated_Nested256_FourSegments() => NestedLayout.Parse(this.nestedSplit);

    // ---- 256 prim-le-record records: 7,168 bytes -------------------------------------------------------------------

    /// <summary>Parses 256 consecutive records with runtime <c>Parse</c> calls at increasing offsets: the loop a caller would write.</summary>
    /// <returns>The number of records.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Records256")]
    public int Runtime_Records256_ParseLoop()
    {
        int count = 0;
        for (int offset = 0; offset < this.records256.Length; offset += this.recordSize)
        {
            this.primRecord.Layout.Parse(this.records256.AsSpan(offset, this.recordSize), "root");
            count++;
        }

        return count;
    }

    /// <summary>Enumerates the same records with runtime <c>ParseMany</c>.</summary>
    /// <returns>The number of records.</returns>
    [Benchmark]
    [BenchmarkCategory("Records256")]
    public int Runtime_Records256_ParseMany()
    {
        int count = 0;
        foreach (StructValue record in this.primRecord.Layout.ParseMany(this.records256, "root"))
        {
            count++;
        }

        return count;
    }

    /// <summary>Parses the records with generated <c>Parse</c> calls at increasing offsets.</summary>
    /// <returns>The number of records.</returns>
    [Benchmark]
    [BenchmarkCategory("Records256")]
    public int Generated_Records256_ParseLoop()
    {
        int count = 0;
        ReadOnlySpan<byte> bytes = this.records256;
        for (int offset = 0; offset < bytes.Length; offset += PrimRecordLayout.Sizes.Root)
        {
            PrimRecordLayout.Parse(bytes.Slice(offset, PrimRecordLayout.Sizes.Root));
            count++;
        }

        return count;
    }

    /// <summary>Enumerates the records with the generated <c>Records</c>.</summary>
    /// <returns>The number of records.</returns>
    [Benchmark]
    [BenchmarkCategory("Records256")]
    public int Generated_Records256_Records()
    {
        int count = 0;
        foreach (PrimRecordLayout.Root record in PrimRecordLayout.Records(this.records256))
        {
            count++;
        }

        return count;
    }

    // ---- 256 prim-le-record views: the enumerator against the offset loop a caller writes -------------------------

    /// <summary>Reads two members of each record through a view created at each offset: the loop a caller would write.</summary>
    /// <returns>The sum of the members.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Records256View")]
    public double HandWritten_Records256_ViewLoop()
    {
        double sum = 0;
        ReadOnlySpan<byte> bytes = this.records256;
        for (int offset = 0; offset < bytes.Length; offset += PrimRecordLayout.Sizes.Root)
        {
            var view = new PrimRecordLayout.RootView(bytes.Slice(offset));
            sum += view.C + view.F;
        }

        return sum;
    }

    /// <summary>Reads the same members through the generated view enumerator.</summary>
    /// <returns>The sum of the members.</returns>
    [Benchmark]
    [BenchmarkCategory("Records256View", "Gate")]
    public double Generated_Records256_ViewEnumerator()
    {
        double sum = 0;
        foreach (PrimRecordLayout.RootView view in PrimRecordLayout.RootView.Enumerate(this.records256))
        {
            sum += view.C + view.F;
        }

        return sum;
    }

    /// <summary>Splits bytes into a sequence of equal segments; the last segment holds the remainder.</summary>
    /// <param name="bytes">The bytes to split.</param>
    /// <param name="segments">The number of segments.</param>
    /// <returns>The segmented sequence.</returns>
    private static ReadOnlySequence<byte> Split(byte[] bytes, int segments)
    {
        int size = (bytes.Length + segments - 1) / segments;
        var first = new Segment(bytes.AsMemory(0, Math.Min(size, bytes.Length)), 0);
        Segment last = first;
        for (int offset = size; offset < bytes.Length; offset += size)
        {
            last = last.Append(bytes.AsMemory(offset, Math.Min(size, bytes.Length - offset)));
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    /// <summary>A segment of a <see cref="ReadOnlySequence{T}"/> built from consecutive memory blocks.</summary>
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        /// <summary>Initializes a segment.</summary>
        /// <param name="memory">The segment's bytes.</param>
        /// <param name="runningIndex">The sequence offset of the segment's first byte.</param>
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            this.Memory = memory;
            this.RunningIndex = runningIndex;
        }

        /// <summary>Links a new segment after this one.</summary>
        /// <param name="memory">The new segment's bytes.</param>
        /// <returns>The new segment.</returns>
        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, this.RunningIndex + this.Memory.Length);
            this.Next = next;
            return next;
        }
    }
}
