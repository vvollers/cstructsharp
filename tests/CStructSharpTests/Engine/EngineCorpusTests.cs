namespace CStructSharp.Tests;

using System.Text;
using System.Text.Json;
using CStructSharp.Compilation;
using CStructSharp.FixtureTool;

/// <summary>
///     Runs every case of the repository's layout corpora (<see cref="EngineCorpora"/>) through one data-driven test per
///     case that checks every property the corpus promises: the case is parsed from several sources (which must agree),
///     read as a value, debug-parsed, has paths resolved, and has the value it reads written back, under
///     <see cref="ExecutionPath.Fastest"/> and <see cref="ExecutionPath.NoFastPaths"/> (which must agree), checking every
///     outcome against the golden outcomes (<see cref="EngineCorpusCase.Run"/>, <see cref="EngineGolden"/>); and every
///     public read, write and update of the case runs on the engine (<see cref="EngineCorpusCase.RunEveryOperation"/>).
///     A benchmark fixture is also checked against the expectation recorded in its case file and, when its root is a
///     fixed composite, for its static read plan.
/// </summary>
[TestClass]
public class EngineCorpusTests
{
    /// <summary>The largest benchmark input outside the golden corpus whose sources and execution paths are compared.</summary>
    private const long LargeInputLimit = 2 * 1024 * 1024;

    /// <summary>
    ///     The benchmark fixtures whose roots are fixed composites and so must keep a static read plan: a composite that
    ///     looks dynamic to the planner loses the plan speed-up. An alias resolves at construction, so
    ///     <c>parity-alias-x1k</c> is planned too; a promoted union is read member by member.
    /// </summary>
    private static readonly HashSet<string> StaticPlanFixtures =
    [
        "aligned-x256", "array-struct-100", "array-struct-10000", "array-u32-be-16384", "array-u32-be-256",
        "array-u32-be-262144", "array-u32-le-16384", "array-u32-le-256", "array-u32-le-262144",
        "array-u32-neutral-262144", "array-u64-le-1000000", "array-u8-1024", "array-u8-1048576",
        "array-u8-16m-stream", "array-u8-65536", "compile-large-512", "compile-medium-128", "compile-nested",
        "compile-small", "cond-plain128", "enum-x1k", "malformed-budget-exceeded", "malformed-truncated",
        "mixed-endian-record", "nested-x1", "nested-x256", "parity-alias-x1k", "prim-be-record", "prim-be-x1k",
        "prim-le-record", "prim-le-x1k", "real-bmp", "real-jpg", "real-png", "real-tar", "real-wav",
    ];

    /// <summary>Gets the parity layout ids as data rows.</summary>
    public static IEnumerable<object[]> ParityLayouts => EngineCorpora.Rows(EngineCorpora.Parity);

    /// <summary>Gets every benchmark fixture id the manifest lists as data rows, the fixtures too large for the golden corpus included.</summary>
    public static IEnumerable<object[]> BenchmarkFixtures => BenchmarkManifestIds().Order(StringComparer.Ordinal).Select(id => new object[] { id, });

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
    ///     Every corpus holds the cases it is expected to: the 207 parity layouts and the 63 benchmark fixtures (the
    ///     manifest lists exactly the case files) minus the oversized inputs, one valid case per manual feature pair plus
    ///     its read-stage invalid forms, the Portable examples and primitives, the well-known formats, a detection schema
    ///     per inspector extension plus its samples, and the fuzz corpus's seeds and mutations; so a loader cannot
    ///     silently lose cases.
    /// </summary>
    [TestMethod]
    public void Corpora_HoldEveryCase()
    {
        List<string> manifest = BenchmarkManifestIds();
        IEnumerable<string> caseFiles = Directory.GetFiles(Path.Combine(TestFixtures.BenchmarkFixtures, "cases"), "*.json").Select(file => Path.GetFileNameWithoutExtension(file));
        CollectionAssert.AreEquivalent(caseFiles.ToList(), manifest, "the benchmark manifest lists exactly the case files");
        Assert.HasCount(63, manifest);

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
    ///     A layout of the generated-parity index, over the input that suite uses, reads and writes its golden outcomes
    ///     from every source and on both execution paths, and runs every operation on the engine.
    /// </summary>
    /// <param name="id">The layout's source and id.</param>
    [TestMethod]
    [DynamicData(nameof(ParityLayouts))]
    public void ParityLayout_ReadsAndWritesOnEverySourceAndPath(string id) => AssertCase(EngineCorpora.Parity[id]);

    /// <summary>
    ///     A benchmark fixture, over its bytes, variables and read options, produces the outcome its case file records (a
    ///     value's canonical JSON hash and length, or an exception type; an oracle independent of any read path); a fixed
    ///     root keeps its static read plan; and it reads and writes its golden outcomes from every source and on both
    ///     execution paths, and runs every operation on the engine. A fixture too large for the golden corpus instead
    ///     parses alike from a span, a stream, a stream returning seven bytes per read and member by member, and
    ///     re-serializes alike with and without the static write plan.
    /// </summary>
    /// <param name="id">The fixture id, which is also its case file name.</param>
    [TestMethod]
    [DynamicData(nameof(BenchmarkFixtures))]
    public void BenchmarkFixture_MatchesItsExpectationOnEverySourceAndPath(string id)
    {
        FixtureDocument fixture = FixtureLoader.Load(TestFixtures.BenchmarkFixtures, id);
        bool compileOnly = fixture.Bytes is null || fixture.Definitions is { Count: > 0 };
        Assert.IsTrue(compileOnly || fixture.ExpectedSha256 is not null || fixture.ExpectedError is not null, id + " records no expectation");
        FixtureOutcome outcome = FixtureVerification.Evaluate(TestFixtures.BenchmarkFixtures, fixture);
        string? mismatch = FixtureVerification.Compare(fixture, outcome);
        Assert.IsNull(mismatch, $"{id} ({outcome.Describe()}): {mismatch}");

        if (StaticPlanFixtures.Contains(id))
        {
            Assert.IsTrue(HasStaticPlan(FixtureLoader.CreateLayout(fixture), fixture.Root), id + ": the fixture root lost its static read plan");
        }

        if (EngineCorpora.Benchmarks.TryGetValue(id, out EngineCorpusCase? item))
        {
            AssertCase(item);
        }
        else
        {
            AssertLargeInputPathsAgree(fixture);
        }
    }

    /// <summary>
    ///     A manual feature example, or its read-stage invalid form, reads and writes its golden outcomes, including its
    ///     offset paths, and runs every operation on the engine.
    /// </summary>
    /// <param name="id">The fixture kind and feature id.</param>
    [TestMethod]
    [DynamicData(nameof(ManualFixtures))]
    public void ManualFixture_ReadsAndWritesOnEverySourceAndPath(string id) => AssertCase(EngineCorpora.Manual[id]);

    /// <summary>
    ///     A Portable layout example or primitive reads and writes its golden outcomes from every source and on both
    ///     execution paths, and runs every operation on the engine.
    /// </summary>
    /// <param name="id">The example or primitive.</param>
    [TestMethod]
    [DynamicData(nameof(PortableCases))]
    public void PortableCase_ReadsAndWritesOnEverySourceAndPath(string id) => AssertCase(EngineCorpora.Portable[id]);

    /// <summary>
    ///     A well-known-format layout, over the bytes its test verifies, reads and writes its golden outcomes from every
    ///     source and on both execution paths, and runs every operation on the engine.
    /// </summary>
    /// <param name="id">The test and input.</param>
    [TestMethod]
    [DynamicData(nameof(WellKnownFormats))]
    public void WellKnownFormat_ReadsAndWritesOnEverySourceAndPath(string id) => AssertCase(EngineCorpora.WellKnown[id]);

    /// <summary>
    ///     An inspector detection schema or sample reads and writes its golden outcomes from every source and on both
    ///     execution paths, and runs every operation on the engine.
    /// </summary>
    /// <param name="id">The schema.</param>
    [TestMethod]
    [DynamicData(nameof(InspectorSchemas))]
    public void InspectorSchema_ReadsAndWritesOnEverySourceAndPath(string id) => AssertCase(EngineCorpora.Inspector[id]);

    /// <summary>
    ///     Every seed and mutation of a fuzz target reads and writes its golden outcomes and runs every operation on the
    ///     engine; a definition that does not compile is skipped, since there is nothing to read. The failures of all
    ///     inputs are reported together.
    /// </summary>
    /// <param name="target">The fuzz target.</param>
    [TestMethod]
    [DynamicData(nameof(FuzzTargets))]
    public void FuzzTarget_ReadsAndWritesEverySeedAndMutation(string target)
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
                _ = item.RunEveryOperation();
            }
            catch (AssertFailedException failure)
            {
                failures.Append(item.Id).Append(": ").Append(failure.Message).Append("\n\n");
            }
        }

        Assert.AreEqual(0, failures.Length, failures.ToString());
        Assert.IsGreaterThan(0, ran, target + ": no input compiled");
    }

    /// <summary>
    ///     Checks one corpus case: its golden outcomes on every source and both execution paths
    ///     (<see cref="EngineCorpusCase.Run"/>), then every public operation on the engine
    ///     (<see cref="EngineCorpusCase.RunEveryOperation"/>).
    /// </summary>
    /// <param name="item">The case.</param>
    /// <exception cref="AssertFailedException">A check fails, or the case has nothing to read.</exception>
    private static void AssertCase(EngineCorpusCase item)
    {
        Assert.IsTrue(item.Run(), item.Id + " has nothing to read");

        // A root the layout cannot declare, such as the only root of a compile-only fixture, has no operation to run.
        _ = item.RunEveryOperation();
    }

    /// <summary>
    ///     Checks a benchmark fixture that is too large for the golden corpus (up to <see cref="LargeInputLimit"/> bytes):
    ///     it parses to the same value, failure and final position from a span, a <see cref="MemoryStream"/>, a stream
    ///     that returns seven bytes per read, and a stream read member by member (<see cref="ExecutionPath.NoFastPaths"/>);
    ///     and a value it parses re-serializes to the same bytes with and without the static write plan.
    /// </summary>
    /// <param name="fixture">The fixture; one without bytes, or with more than the limit, is not compared.</param>
    private static void AssertLargeInputPathsAgree(FixtureDocument fixture)
    {
        if (fixture.Bytes is null || fixture.ByteLength > LargeInputLimit)
        {
            return;
        }

        string id = fixture.Id;
        byte[] bytes = FixtureLoader.MaterializeBytes(TestFixtures.BenchmarkFixtures, fixture);
        CStruct layout = FixtureLoader.CreateLayout(fixture);
        ReadOptions read = FixtureLoader.CreateReadOptions(fixture);
        OperationOutcome span = OperationOutcome.Of(() => layout.Parse(bytes.AsSpan(), fixture.Root, options: read));
        using var memoryStream = new MemoryStream(bytes, writable: false);
        OperationOutcome memory = OperationOutcome.Of(() => layout.Parse(memoryStream, fixture.Root, options: read));
        using var chunked = new ChunkedMemoryStream(bytes, 7, writable: false);
        OperationOutcome chunkedOutcome = OperationOutcome.Of(() => layout.Parse(chunked, fixture.Root, options: read));
        using var memberByMember = new MemoryStream(bytes, writable: false);
        OperationOutcome memberByMemberOutcome = OperationOutcome.Of(() => layout.Parse(memberByMember, fixture.Root, options: read with { ExecutionPath = ExecutionPath.NoFastPaths, }));
        OperationOutcome.AssertSame(span, memory, id + ": span vs MemoryStream");
        OperationOutcome.AssertSame(span, chunkedOutcome, id + ": span vs chunked stream");
        OperationOutcome.AssertSame(span, memberByMemberOutcome, id + ": fast paths vs member by member");
        Assert.AreEqual(chunked.Position, memoryStream.Position, id + ": final position");
        Assert.AreEqual(memberByMember.Position, memoryStream.Position, id + ": final position member by member");

        if (fixture.ExpectedError is not null || span.Result is not { } parsed)
        {
            return;
        }

        OperationOutcome planned = OperationOutcome.Of(() => layout.Serialize(fixture.Root, parsed));
        OperationOutcome unplanned = OperationOutcome.Of(() => layout.Serialize(fixture.Root, parsed, options: ExecutionPaths.NoFastPathsWrite()));
        OperationOutcome.AssertSame(unplanned, planned, id + ": serialize with and without the static write plan", compareOffsets: false);
    }

    /// <summary>Returns whether a layout's root is a composite with a static read plan.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="root">The root's name.</param>
    /// <returns><see langword="true"/> when the root has a plan.</returns>
    private static bool HasStaticPlan(CStruct layout, string root)
        => layout.CompiledModel.Symbols.TryGetValue(root, out CompiledTypeReference reference) &&
           reference.Symbol.Definition is CompiledCompositeType { StaticPlan: not null, };

    /// <summary>Reads the benchmark fixture ids from <c>benchmarks/fixtures/manifest.json</c> in their listed order.</summary>
    /// <returns>The ids.</returns>
    private static List<string> BenchmarkManifestIds()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestFixtures.BenchmarkFixtures, "manifest.json")));
        return [.. manifest.RootElement.GetProperty("fixtures").EnumerateArray().Select(item => item.GetProperty("id").GetString()!)];
    }
}
