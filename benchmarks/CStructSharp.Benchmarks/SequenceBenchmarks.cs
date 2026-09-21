namespace CStructSharp.Benchmarks;

using System.Buffers;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CStructSharp.Benchmarks.Baseline0;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Values;

/// <summary>
///     Segmented input and record sequences: a <see cref="ReadOnlySequence{T}"/> of one segment (read in place)
///     and of four segments (copied into a pooled buffer) against the span for the record and the nested fixture,
///     runtime and generated.
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

    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.Load("prim-le-record");
        this.nested = FixtureCase.Load("nested-x256");
        this.primRecordSingle = new ReadOnlySequence<byte>(this.primRecord.Bytes);
        this.primRecordSplit = Split(this.primRecord.Bytes, 4);
        this.nestedSingle = new ReadOnlySequence<byte>(this.nested.Bytes);
        this.nestedSplit = Split(this.nested.Bytes, 4);
    }

    // ---- prim-le-record ---------------------------------------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PrimRecord")]
    public StructValue Runtime_PrimRecord_Span() => this.primRecord.Layout.Parse(this.primRecord.Bytes.AsSpan(), "root");

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public StructValue Runtime_PrimRecord_SingleSegment() => this.primRecord.Layout.Parse(this.primRecordSingle, "root");

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public StructValue Runtime_PrimRecord_FourSegments() => this.primRecord.Layout.Parse(this.primRecordSplit, "root");

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public PrimRecordLayout.Root Generated_PrimRecord_Span() => PrimRecordLayout.Parse(this.primRecord.Bytes);

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public PrimRecordLayout.Root Generated_PrimRecord_SingleSegment() => PrimRecordLayout.Parse(this.primRecordSingle);

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public PrimRecordLayout.Root Generated_PrimRecord_FourSegments() => PrimRecordLayout.Parse(this.primRecordSplit);

    // ---- nested-x256: 6,400 bytes --------------------------------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Nested256")]
    public StructValue Runtime_Nested256_Span() => this.nested.Layout.Parse(this.nested.Bytes.AsSpan(), "root");

    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public StructValue Runtime_Nested256_FourSegments() => this.nested.Layout.Parse(this.nestedSplit, "root");

    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public NestedLayout.Root Generated_Nested256_Span() => NestedLayout.Parse(this.nested.Bytes);

    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public NestedLayout.Root Generated_Nested256_SingleSegment() => NestedLayout.Parse(this.nestedSingle);

    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public NestedLayout.Root Generated_Nested256_FourSegments() => NestedLayout.Parse(this.nestedSplit);

    /// <summary>A sequence of <paramref name="segments"/> equal parts of <paramref name="bytes"/>.</summary>
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

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            this.Memory = memory;
            this.RunningIndex = runningIndex;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, this.RunningIndex + this.Memory.Length);
            this.Next = next;
            return next;
        }
    }
}
