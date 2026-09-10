namespace CStructSharp.Benchmarks;

using BenchmarkDotNet.Attributes;

[BenchmarkCategory("Address")]
public class AddressBenchmarks
{
    private CStruct fixedLayout = null!;
    private CStruct runtimeLayout = null!;
    private MemoryStream stream = null!;
    private IReadOnlyDictionary<string, int> runtimeVariables = null!;
    private string path = null!;

    [Params(0, 127, 255)]
    public int Index { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        const string item = "struct item { uint16 value; }; ";
        this.fixedLayout = new CStruct(item + "struct root { item groups[256]; };");
        this.runtimeLayout = new CStruct(item + "struct root { item groups[COUNT]; };");
        this.stream = new MemoryStream(new byte[512], writable: false);
        this.runtimeVariables = new Dictionary<string, int> { ["COUNT"] = 256, };
        this.path = $"root.groups[{this.Index}].value";
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.stream.Dispose();
    }

    [Benchmark]
    [BenchmarkCategory("ReleaseGate")]
    public long ResolveFixedNestedArray()
    {
        return this.fixedLayout.ResolveAddress(this.stream, this.path);
    }

    [Benchmark]
    public long ResolveRuntimeNestedArray()
    {
        return this.runtimeLayout.ResolveAddress(this.stream, this.path, this.runtimeVariables);
    }
}
