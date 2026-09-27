namespace CStructSharp.Benchmarks;

using System.Text;
using BenchmarkDotNet.Attributes;

/// <summary>Compiles the largest definition the default compilation options accept.</summary>
[BenchmarkCategory("Compile")]
public class CompilationBenchmarks
{
    private string maximumDefinition = null!;

    /// <summary>Builds a 512-field struct and pads it with spaces to the maximum definition length.</summary>
    [GlobalSetup]
    public void Setup()
    {
        int maximumLength = new CStructCompilationOptions().MaxDefinitionLength;
        string structuredPrefix = CreateDefinition(512);
        this.maximumDefinition = structuredPrefix + new string(' ', maximumLength - structuredPrefix.Length);
    }

    /// <summary>Compiles the maximum-length definition.</summary>
    /// <returns>The compiled layout.</returns>
    [Benchmark]
    [BenchmarkCategory("Gate")]
    public CStruct CompileMaximumSource()
    {
        return new CStruct(this.maximumDefinition);
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
