namespace CStructSharp.Benchmarks.Scenarios;

using BenchmarkDotNet.Attributes;
using CStructSharp.Diagnostics;

/// <summary>S-MALFORMED: the cost of the failure path (exception construction, context attachment) per rejection.</summary>
[BenchmarkCategory("Scenario", "Malformed")]
public class MalformedBenchmarks
{
    private FixtureCase fixture = null!;

    /// <summary>Gets or sets the malformed fixture: truncated input, a negative count, an exceeded budget, or a dangling pointer.</summary>
    [Params("malformed-truncated", "malformed-negative-count", "malformed-budget-exceeded", "malformed-dangling-pointer")]
    public string Fixture { get; set; } = null!;

    /// <summary>Loads the fixture, compiles its layout, and materializes its bytes.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.fixture = FixtureCase.Load(this.Fixture);
    }

    /// <summary>Parses the malformed bytes and catches the rejection, as a caller would.</summary>
    /// <returns>The exception type name, or <c>none</c> when the parse unexpectedly succeeds.</returns>
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
