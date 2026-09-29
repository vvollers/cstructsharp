namespace CStructSharp.Tests;

using System.Text;

/// <summary>
///     Runs the differential harness over the repository's layout corpora (<see cref="EngineCorpora"/>): every case is
///     parsed from several sources (which must agree), read as a value, debug-parsed, has paths resolved, and has the
///     value it reads written back, under <see cref="ExecutionPath.Fastest"/> and
///     <see cref="ExecutionPath.GeneralOnly"/>, checking automatic engine selection against the golden outcomes
///     (<see cref="EngineCorpusCase.Run"/>, <see cref="EngineGolden"/>).
/// </summary>
[TestClass]
public class EngineCorpusTests
{
    /// <summary>Gets the parity layout ids as data rows.</summary>
    public static IEnumerable<object[]> ParityLayouts => EngineCorpora.Rows(EngineCorpora.Parity);

    /// <summary>Gets the benchmark fixture ids as data rows.</summary>
    public static IEnumerable<object[]> BenchmarkFixtures => EngineCorpora.Rows(EngineCorpora.Benchmarks);

    /// <summary>Gets the manual fixture ids as data rows.</summary>
    public static IEnumerable<object[]> ManualFixtures => EngineCorpora.Rows(EngineCorpora.Manual);

    /// <summary>Gets the Portable contract case ids as data rows.</summary>
    public static IEnumerable<object[]> PortableCases => EngineCorpora.Rows(EngineCorpora.Portable);

    /// <summary>Gets the well-known-format case ids as data rows.</summary>
    public static IEnumerable<object[]> WellKnownFormats => EngineCorpora.Rows(EngineCorpora.WellKnown);

    /// <summary>Gets the inspector catalog ids as data rows.</summary>
    public static IEnumerable<object[]> InspectorSchemas => EngineCorpora.Rows(EngineCorpora.Inspector);

    /// <summary>Gets the fuzz target names as data rows.</summary>
    public static IEnumerable<object[]> FuzzTargets => EngineCorpora.Fuzz.Keys.Order(StringComparer.Ordinal).Select(target => new object[] { target, });

    /// <summary>
    ///     Every corpus holds the cases it is expected to: the 207 parity layouts and 63 benchmark fixtures minus the
    ///     oversized inputs, one valid case per manual feature pair plus its read-stage invalid forms, the Portable
    ///     examples and primitives, the well-known formats, a detection schema per inspector extension plus its samples,
    ///     and the fuzz corpus's seeds and mutations; so a loader cannot silently lose cases.
    /// </summary>
    [TestMethod]
    public void Corpora_HoldEveryCase()
    {
        Assert.AreEqual(207, EngineCorpora.Parity.Count + EngineCorpora.SkippedParity.Count);
        Assert.AreEqual(63, EngineCorpora.Benchmarks.Count + EngineCorpora.SkippedBenchmarks.Count);
        CollectionAssert.AreEquivalent(EngineCorpora.SkippedBenchmarks, EngineCorpora.SkippedParity.Select(id => id["Benchmarks/".Length..]).ToList());
        Assert.IsLessThanOrEqualTo(8, EngineCorpora.SkippedBenchmarks.Count, string.Join(", ", EngineCorpora.SkippedBenchmarks));
        Assert.AreEqual(48, EngineCorpora.Manual.Keys.Count(id => id.StartsWith("valid/", StringComparison.Ordinal)));
        Assert.AreEqual(15, EngineCorpora.Portable.Keys.Count(id => id.StartsWith("example/", StringComparison.Ordinal)));
        Assert.AreEqual(90, EngineCorpora.Portable.Keys.Count(id => id.StartsWith("primitive/", StringComparison.Ordinal)));
        Assert.IsGreaterThanOrEqualTo(10, EngineCorpora.WellKnown.Count);
        Assert.IsGreaterThanOrEqualTo(100, EngineCorpora.Inspector.Keys.Count(id => id.StartsWith("detected-", StringComparison.Ordinal)));
        Assert.IsGreaterThanOrEqualTo(9, EngineCorpora.Inspector.Keys.Count(id => !id.StartsWith("detected-", StringComparison.Ordinal)));
        Assert.AreEqual(6, EngineCorpora.Fuzz.Count);
    }

    /// <summary>
    ///     With the engine required, every public read, write and update of every corpus case whose root the engine can read
    ///     runs on the engine (<see cref="EngineCorpusCase.RunRequiringEngine"/>): after the update stage nothing is declined
    ///     for the kind of operation, source, destination or options - only for a root or member the programs cannot
    ///     compile, and for a path that selects nothing writable, which the public operation rejects too.
    /// </summary>
    [TestMethod]
    public void EveryCorpus_RunsEveryOperationOnTheEngineWhenRequired()
    {
        IEnumerable<EngineCorpusCase> cases = EngineCorpora.Parity.Values
                                                           .Concat(EngineCorpora.Benchmarks.Values)
                                                           .Concat(EngineCorpora.Manual.Values)
                                                           .Concat(EngineCorpora.Portable.Values)
                                                           .Concat(EngineCorpora.WellKnown.Values)
                                                           .Concat(EngineCorpora.Inspector.Values)
                                                           .Concat(EngineCorpora.Fuzz.Values.SelectMany(items => items));
        var failures = new StringBuilder();
        int ran = 0;
        foreach (EngineCorpusCase item in cases)
        {
            try
            {
                ran += item.RunRequiringEngine();
            }
            catch (AssertFailedException failure)
            {
                failures.Append(failure.Message).Append('\n');
            }
        }

        Assert.AreEqual(0, failures.Length, failures.ToString());
        Assert.IsGreaterThan(5000, ran);
    }

    /// <summary>A layout of the generated-parity index, over the input that suite uses, reads and writes identically.</summary>
    /// <param name="id">The layout's source and id.</param>
    [TestMethod]
    [DynamicData(nameof(ParityLayouts))]
    public void ParityLayout_ReadsAndWritesIdentically(string id)
    {
        Assert.IsTrue(EngineCorpora.Parity[id].Run(), id + " has nothing to read");
    }

    /// <summary>A benchmark fixture, over its bytes and read options, reads and writes identically.</summary>
    /// <param name="id">The fixture id.</param>
    [TestMethod]
    [DynamicData(nameof(BenchmarkFixtures))]
    public void BenchmarkFixture_ReadsAndWritesIdentically(string id)
    {
        Assert.IsTrue(EngineCorpora.Benchmarks[id].Run(), id + " has nothing to read");
    }

    /// <summary>A manual feature example, or its read-stage invalid form, reads and writes identically, including its offset paths.</summary>
    /// <param name="id">The fixture kind and feature id.</param>
    [TestMethod]
    [DynamicData(nameof(ManualFixtures))]
    public void ManualFixture_ReadsAndWritesIdentically(string id)
    {
        Assert.IsTrue(EngineCorpora.Manual[id].Run(), id + " has nothing to read");
    }

    /// <summary>A Portable layout example or primitive reads and writes identically.</summary>
    /// <param name="id">The example or primitive.</param>
    [TestMethod]
    [DynamicData(nameof(PortableCases))]
    public void PortableCase_ReadsAndWritesIdentically(string id)
    {
        Assert.IsTrue(EngineCorpora.Portable[id].Run(), id + " has nothing to read");
    }

    /// <summary>A well-known-format layout, over the bytes its test verifies, reads and writes identically.</summary>
    /// <param name="id">The test and input.</param>
    [TestMethod]
    [DynamicData(nameof(WellKnownFormats))]
    public void WellKnownFormat_ReadsAndWritesIdentically(string id)
    {
        Assert.IsTrue(EngineCorpora.WellKnown[id].Run(), id + " has nothing to read");
    }

    /// <summary>An inspector detection schema or sample reads and writes identically.</summary>
    /// <param name="id">The schema.</param>
    [TestMethod]
    [DynamicData(nameof(InspectorSchemas))]
    public void InspectorSchema_ReadsAndWritesIdentically(string id)
    {
        Assert.IsTrue(EngineCorpora.Inspector[id].Run(), id + " has nothing to read");
    }

    /// <summary>
    ///     Every seed and mutation of a fuzz target reads and writes identically; a definition that does not compile is
    ///     skipped, since there is nothing to read. The failures of all inputs are reported together.
    /// </summary>
    /// <param name="target">The fuzz target.</param>
    [TestMethod]
    [DynamicData(nameof(FuzzTargets))]
    public void FuzzTarget_ReadsAndWritesIdentically(string target)
    {
        var failures = new StringBuilder();
        int ran = 0;
        foreach (EngineCorpusCase item in EngineCorpora.Fuzz[target])
        {
            try
            {
                // Each input is its own golden group, so a difference names the input.
                using IDisposable part = EngineGolden.Part(item.Id);
                ran += item.Run() ? 1 : 0;
            }
            catch (AssertFailedException failure)
            {
                failures.Append(item.Id).Append(": ").Append(failure.Message).Append("\n\n");
            }
        }

        Assert.AreEqual(0, failures.Length, failures.ToString());
        Assert.IsGreaterThan(0, ran, target + ": no input compiled");
    }
}
