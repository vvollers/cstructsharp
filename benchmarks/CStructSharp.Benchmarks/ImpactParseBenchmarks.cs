namespace CStructSharp.Benchmarks;

using BenchmarkDotNet.Attributes;
using CStructSharp.Benchmarks.Scenarios;

/// <summary>
///     The parse part of the <c>Impact</c> category: one span parse for each kind of layout the general reader and
///     the static plans handle differently, so a change to either shows up in a run of a few minutes. The other
///     Impact cases (compile, generated code, paths, writes, updates, async and segmented input) are tagged on their
///     own benchmark classes.
/// </summary>
[BenchmarkCategory("Impact")]
public class ImpactParseBenchmarks
{
    private FixtureCase fixture = null!;

    /// <summary>
    ///     The fixture: fixed primitives, nested fixed structs, a big-endian array, runtime-counted arrays,
    ///     conditional fields, strings, a real file header, pointers, bitfields, unions and alias spellings.
    /// </summary>
    [Params(
        "prim-le-record",
        "nested-x256",
        "array-u32-be-16384",
        "dynamic-64",
        "cond-if128",
        "strings-1024",
        "real-png",
        "pointer-depth-8",
        "bitfield-x1k",
        "union-x1k",
        "parity-alias-x1k")]
    public string Fixture { get; set; } = null!;

    /// <summary>Loads the fixture's layout, bytes and read options once per case.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.fixture = FixtureCase.Load(this.Fixture);
    }

    /// <summary>Parses the fixture from its in-memory bytes.</summary>
    /// <returns>The parsed root, returned so the work cannot be optimized away.</returns>
    [Benchmark]
    public object ParseSpan()
    {
        return this.fixture.ParseSpan();
    }
}
