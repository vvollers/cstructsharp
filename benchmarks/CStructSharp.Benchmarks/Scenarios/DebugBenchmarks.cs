namespace CStructSharp.Benchmarks.Scenarios;

using BenchmarkDotNet.Attributes;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>S-DEBUG: byte-range capture on a 1k-record array and on a real format header.</summary>
[BenchmarkCategory("Scenario", "Debug")]
public class DebugBenchmarks
{
    private FixtureCase fixture = null!;
    private MemoryStream stream = null!;

    /// <summary>Gets or sets the fixture id.</summary>
    [Params("prim-le-x1k", "real-png", "nested-x256")]
    public string Fixture { get; set; } = null!;

    /// <summary>Loads the fixture and opens a stream over its bytes.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.fixture = FixtureCase.Load(this.Fixture);
        this.stream = new MemoryStream(this.fixture.Bytes, writable: false);
    }

    /// <summary>Disposes the stream.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.stream.Dispose();
    }

    /// <summary>Parses the stream and records the byte range of every value.</summary>
    /// <returns>The value and its ranges.</returns>
    [Benchmark]
    public ParseResult ParseWithDebug()
    {
        this.stream.Position = 0;
        return this.fixture.Layout.ParseWithDebug(this.stream, this.fixture.Root, this.fixture.Variables, this.fixture.ReadOptions);
    }
}
