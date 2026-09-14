namespace CStructSharp.Benchmarks.Baseline0;

using BenchmarkDotNet.Attributes;

/// <summary>S-DEBUG: byte-range capture on a 1k-record array and on a real format header.</summary>
[BenchmarkCategory("Baseline0", "Debug")]
public class DebugBenchmarks
{
    private FixtureCase fixture = null!;
    private MemoryStream stream = null!;

    [Params("prim-le-x1k", "real-png", "nested-x256")]
    public string Fixture { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.fixture = FixtureCase.Load(this.Fixture);
        this.stream = new MemoryStream(this.fixture.Bytes, writable: false);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.stream.Dispose();
    }

    [Benchmark]
    public (List<DebugData> DebugData, dynamic Result) ParseWithDebug()
    {
        this.stream.Position = 0;
        return this.fixture.Layout.ParseStreamWithDebug(this.stream, this.fixture.Root, this.fixture.Variables, this.fixture.ReadOptions);
    }
}
