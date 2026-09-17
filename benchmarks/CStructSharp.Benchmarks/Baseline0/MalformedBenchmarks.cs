namespace CStructSharp.Benchmarks.Baseline0;

using BenchmarkDotNet.Attributes;
using CStructSharp.Diagnostics;

/// <summary>S-MALFORMED: the cost of the failure path (exception construction, context attachment) per rejection.</summary>
[BenchmarkCategory("Baseline0", "Malformed")]
public class MalformedBenchmarks
{
    private FixtureCase fixture = null!;

    [Params("malformed-truncated", "malformed-negative-count", "malformed-budget-exceeded", "malformed-dangling-pointer")]
    public string Fixture { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.fixture = FixtureCase.Load(this.Fixture);
    }

    [Benchmark]
    public string ParseAndCatch()
    {
        try
        {
            _ = this.fixture.ParseSpan();
            return "none";
        }
        catch (CStructException exception)
        {
            return exception.GetType().Name;
        }
    }
}
