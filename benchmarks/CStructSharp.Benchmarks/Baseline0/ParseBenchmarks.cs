namespace CStructSharp.Benchmarks.Baseline0;

using BenchmarkDotNet.Attributes;

/// <summary>
///     Warm steady-state parse of every fixture in the scenario matrix, through both the zero-copy span overload
///     and the seekable-stream overload. Sizes above 64 KiB use the larger read budgets recorded in the fixture.
/// </summary>
[BenchmarkCategory("Baseline0", "Parse")]
public class ParseBenchmarks
{
    private FixtureCase fixture = null!;
    private MemoryStream stream = null!;

    [Params(
        "prim-le-record",
        "prim-le-x1k",
        "prim-be-record",
        "prim-be-x1k",
        "mixed-endian-record",
        "nested-x1",
        "nested-x256",
        "aligned-x256",
        "array-u8-1024",
        "array-u8-65536",
        "array-u8-1048576",
        "array-u32-le-256",
        "array-u32-le-16384",
        "array-u32-le-262144",
        "array-u32-be-256",
        "array-u32-be-16384",
        "array-u32-be-262144",
        "array-u32-neutral-262144",
        "array-struct-100",
        "array-struct-10000",
        "dynamic-1",
        "dynamic-64",
        "dynamic-1024",
        "multidim-16x16",
        "bitfield-x1k",
        "enum-x1k",
        "union-x1k",
        "strings-8",
        "strings-1024",
        "strings-65536",
        "pointer-depth-1",
        "pointer-depth-8",
        "pointer-depth-64",
        "cond-plain128",
        "cond-if128",
        "cond-switch128",
        "real-bmp",
        "real-wav",
        "real-zip",
        "real-png",
        "real-jpg",
        "real-pe-exe",
        "real-pe-dll",
        "real-ico",
        "real-tar")]
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

    [Benchmark(Baseline = true)]
    public object ParseSpan()
    {
        return this.fixture.ParseSpan();
    }

    [Benchmark]
    public object ParseStream()
    {
        return this.fixture.ParseStream(this.stream);
    }
}
