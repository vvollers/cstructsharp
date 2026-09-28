namespace CStructSharp.Benchmarks.Scenarios;

using BenchmarkDotNet.Attributes;
using CStructSharp.FixtureTool;

/// <summary>S-COMPILE: schema parsing and compiled-layout construction, including the repeated-schema workload.</summary>
[BenchmarkCategory("Scenario", "Compile")]
public class CompileBenchmarks
{
    private string definition = null!;
    private FixtureOptions options = null!;
    private string[] roundRobin = null!;

    /// <summary>Gets or sets the fixture whose definition is compiled: synthetic structs of growing size, nesting, a Windows header, and real file formats.</summary>
    [Params("compile-small", "compile-medium-128", "compile-large-512", "compile-nested", "compile-windows-header", "real-png", "real-pe-exe", "real-tar")]
    public string Fixture { get; set; } = null!;

    /// <summary>Loads the fixture's definition and options, and the 100 distinct definitions of <c>compile-k100</c>.</summary>
    [GlobalSetup]
    public void Setup()
    {
        FixtureDocument document = FixtureCase.LoadDocument(this.Fixture);
        this.definition = document.Definition;
        this.options = document.Options;
        this.roundRobin = FixtureCase.LoadDocument("compile-k100").Definitions!.ToArray();
    }

    /// <summary>Compiles the fixture's definition with its options.</summary>
    /// <returns>The compiled layout.</returns>
    [Benchmark]
    [BenchmarkCategory("Gate", "Impact")]
    public CStruct Compile()
    {
        return new CStruct(this.definition, this.options.PointerSize, this.options.Aligned, this.options.LittleEndian);
    }

    /// <summary>Compiles 100 distinct schemas once each; reported time is for the whole round (÷100 per schema).</summary>
    /// <returns>The last layout compiled in the round.</returns>
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

    /// <summary>A repeat request through the shared cache: the cost of a hit.</summary>
    /// <returns>The cached layout for the fixture's definition and options.</returns>
    [Benchmark]
    public CStruct GetOrCompile_Hit()
    {
        return CStruct.GetOrCompile(this.definition, this.options.PointerSize, this.options.Aligned, this.options.LittleEndian);
    }

    /// <summary>
    ///     100 distinct schemas through a 64-entry cache: every request misses (the working set exceeds capacity),
    ///     so this bounds the overhead of a thrashing cache relative to <see cref="CompileK100RoundRobin"/>.
    /// </summary>
    /// <returns>The last layout returned by the cache in the round.</returns>
    [Benchmark]
    public CStruct GetOrCompileK100RoundRobin_Thrash()
    {
        CStruct last = null!;
        foreach (string source in this.roundRobin)
        {
            last = CStruct.GetOrCompile(source);
        }

        return last;
    }
}
