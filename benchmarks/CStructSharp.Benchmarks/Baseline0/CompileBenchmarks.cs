namespace CStructSharp.Benchmarks.Baseline0;

using BenchmarkDotNet.Attributes;
using CStructSharp.FixtureTool;

/// <summary>S-COMPILE: schema parsing and compiled-layout construction, including the repeated-schema workload.</summary>
[BenchmarkCategory("Baseline0", "Compile")]
public class CompileBenchmarks
{
    private string definition = null!;
    private FixtureOptions options = null!;
    private string[] roundRobin = null!;

    [Params("compile-small", "compile-medium-128", "compile-large-512", "compile-nested", "real-png", "real-pe-exe", "real-tar")]
    public string Fixture { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        FixtureDocument document = FixtureCase.LoadDocument(this.Fixture);
        this.definition = document.Definition;
        this.options = document.Options;
        this.roundRobin = FixtureCase.LoadDocument("compile-k100").Definitions!.ToArray();
    }

    [Benchmark]
    public CStruct Compile()
    {
        return new CStruct(this.definition, this.options.PointerSize, this.options.Aligned, this.options.LittleEndian);
    }

    /// <summary>Compiles 100 distinct schemas once each; reported time is for the whole round (÷100 per schema).</summary>
    [Benchmark]
    public CStruct CompileK100RoundRobin()
    {
        CStruct last = null!;
        foreach (string source in this.roundRobin)
        {
            last = new CStruct(source);
        }

        return last;
    }
}
