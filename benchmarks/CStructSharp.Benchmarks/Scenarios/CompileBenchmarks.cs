namespace CStructSharp.Benchmarks.Scenarios;

using System.Text;
using BenchmarkDotNet.Attributes;
using CStructSharp.FixtureTool;

/// <summary>
///     S-COMPILE: schema parsing and compiled-layout construction, including the repeated-schema workload and the
///     longest definition the default compilation options accept.
/// </summary>
[BenchmarkCategory("Scenario", "Compile")]
public class CompileBenchmarks
{
    /// <summary>The <see cref="Fixture"/> value that is not a fixture file: a definition of the maximum accepted length.</summary>
    private const string MaximumLength = "maximum-length";

    private string definition = null!;
    private FixtureOptions options = null!;
    private string[] roundRobin = null!;

    /// <summary>
    ///     Gets or sets the fixture whose definition is compiled: synthetic structs of growing size, nesting, a Windows
    ///     header, real file formats, and <c>maximum-length</c>, which <see cref="Setup"/> builds instead of loading.
    /// </summary>
    [Params(
        "compile-small",
        "compile-medium-128",
        "compile-large-512",
        "compile-nested",
        "compile-windows-header",
        "real-png",
        "real-pe-exe",
        "real-tar",
        MaximumLength)]
    public string Fixture { get; set; } = null!;

    /// <summary>
    ///     Loads the fixture's definition and options, and the 100 distinct definitions of <c>compile-k100</c>. For
    ///     <c>maximum-length</c> it builds a struct of 512 <c>uint32</c> fields padded with spaces to
    ///     <see cref="CStructCompilationOptions.MaxDefinitionLength"/> characters, with the default options.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        if (this.Fixture == MaximumLength)
        {
            int maximumLength = new CStructCompilationOptions().MaxDefinitionLength;
            string structuredPrefix = CreateDefinition(512);
            this.definition = structuredPrefix + new string(' ', maximumLength - structuredPrefix.Length);
            this.options = new FixtureOptions();
        }
        else
        {
            FixtureDocument document = FixtureCase.LoadDocument(this.Fixture);
            this.definition = document.Definition;
            this.options = document.Options;
        }

        this.roundRobin = FixtureCase.LoadDocument("compile-k100").Definitions!.ToArray();
    }

    /// <summary>Compiles the fixture's definition with its options.</summary>
    /// <returns>The compiled layout.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact")]
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

    /// <summary>Builds <c>struct root</c> with the given number of <c>uint32</c> fields.</summary>
    /// <param name="fieldCount">The number of fields.</param>
    /// <returns>The definition text.</returns>
    private static string CreateDefinition(int fieldCount)
    {
        var result = new StringBuilder("struct root { ");
        for (int index = 0; index < fieldCount; index++)
        {
            result.Append("uint32 field");
            result.Append(index);
            result.Append("; ");
        }

        result.Append("};");
        return result.ToString();
    }
}
