namespace CStructSharp.Benchmarks;

using System.Text;
using BenchmarkDotNet.Attributes;

[BenchmarkCategory("Compile")]
public class CompilationBenchmarks
{
    private string smallDefinition = null!;
    private string mediumDefinition = null!;
    private string maximumDefinition = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.smallDefinition = "struct root { uint8 kind; uint32 value; };";
        this.mediumDefinition = CreateDefinition(128);

        int maximumLength = new CStructCompilationOptions().MaxDefinitionLength;
        string structuredPrefix = CreateDefinition(512);
        this.maximumDefinition = structuredPrefix + new string(' ', maximumLength - structuredPrefix.Length);
    }

    [Benchmark]
    [BenchmarkCategory("ReleaseGate")]
    public CStruct CompileSmall()
    {
        return new CStruct(this.smallDefinition);
    }

    [Benchmark]
    [BenchmarkCategory("ReleaseGate")]
    public CStruct CompileMedium()
    {
        return new CStruct(this.mediumDefinition);
    }

    [Benchmark]
    [BenchmarkCategory("ReleaseGate")]
    public CStruct CompileMaximumSource()
    {
        return new CStruct(this.maximumDefinition);
    }

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
