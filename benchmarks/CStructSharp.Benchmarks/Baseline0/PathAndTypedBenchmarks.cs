namespace CStructSharp.Benchmarks.Baseline0;

using BenchmarkDotNet.Attributes;

/// <summary>S-PATH and typed reads: selected-value reads, address resolution by index, and POCO binding.</summary>
[BenchmarkCategory("Baseline0", "Path")]
public class PathAndTypedBenchmarks
{
    private FixtureCase primRecord = null!;
    private FixtureCase nested = null!;
    private FixtureCase structArray = null!;
    private MemoryStream structArrayStream = null!;
    private string indexedPath = null!;

    [Params(0, 127, 9999)]
    public int Index { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.Load("prim-le-record");
        this.nested = FixtureCase.Load("nested-x256");
        this.structArray = FixtureCase.Load("array-struct-10000");
        this.structArrayStream = new MemoryStream(this.structArray.Bytes, writable: false);
        this.indexedPath = $"root.items[{this.Index}].id";
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.structArrayStream.Dispose();
    }

    [Benchmark]
    public object? ReadValue_Scalar_Natural()
    {
        return this.primRecord.Layout.ReadValue(this.primRecord.Bytes.AsSpan(), "root.c");
    }

    [Benchmark]
    public uint ReadValue_Scalar_Typed()
    {
        return this.primRecord.Layout.ReadValue<uint>(this.primRecord.Bytes.AsSpan(), "root.c");
    }

    [Benchmark]
    public long ResolveAddress_Index()
    {
        this.structArrayStream.Position = 0;
        return this.structArray.Layout.ResolveAddress(this.structArrayStream, this.indexedPath);
    }

    [Benchmark]
    public uint ReadValue_Indexed_Typed()
    {
        return this.structArray.Layout.ReadValue<uint>(this.structArray.Bytes.AsSpan(), this.indexedPath);
    }

    [Benchmark]
    public PrimRecord ReadTyped_PrimRecord()
    {
        return this.primRecord.Layout.ReadValue<PrimRecord>(this.primRecord.Bytes.AsSpan(), "root");
    }

    [Benchmark]
    public NestedRoot ReadTyped_Nested256()
    {
        return this.nested.Layout.ReadValue<NestedRoot>(this.nested.Bytes.AsSpan(), "root");
    }

    public sealed class PrimRecord
    {
        public byte A { get; set; }

        public short B { get; set; }

        public uint C { get; set; }

        public long D { get; set; }

        public float E { get; set; }

        public double F { get; set; }

        public bool G { get; set; }
    }

    public sealed class NestedLeaf
    {
        public byte Kind { get; set; }

        public uint Value { get; set; }
    }

    public sealed class NestedMid
    {
        public NestedLeaf First { get; set; } = new();

        public NestedLeaf Second { get; set; } = new();

        public ushort Tail { get; set; }
    }

    public sealed class NestedTop
    {
        public NestedMid Left { get; set; } = new();

        public NestedMid Right { get; set; } = new();

        public byte Mark { get; set; }
    }

    public sealed class NestedRoot
    {
        public NestedTop[] Items { get; set; } = [];
    }
}
