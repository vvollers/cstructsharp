namespace CStructSharp.Tests;

using CStructSharp.Fuzzing;

/// <summary>Executes the retained fuzz corpus and stable mutation engine on every supported framework.</summary>
[TestClass]
public class ManagedFuzzTests
{
    /// <summary>
    ///     The reviewed corpus must retain five targets, 20 starting seeds, 128 mutations per target, and inputs no
    ///     larger than 256 bytes.
    /// </summary>
    /// <remarks>
    ///     Small read, write, and array limits keep runs bounded. This test checks the agreed test configuration rather
    ///     than parsing one particular struct.
    /// </remarks>
    [TestMethod]
    public void Corpus_DefinesEveryBoundedManagedTarget()
    {
        FuzzCorpus corpus = LoadCorpus();

        Assert.AreEqual(1, corpus.SchemaVersion);
        Assert.AreEqual("0x46555A5A51413034", corpus.Seed);
        Assert.AreEqual(128, corpus.IterationsPerTarget);
        Assert.AreEqual(256, corpus.MaxInputBytes);
        CollectionAssert.AreEquivalent(
            new[] { "binary-roundtrip", "definition", "expression", "generated-differential", "path", "pointer-union", },
            corpus.Targets.Select(target => target.Id).ToArray());
        Assert.AreEqual(24, corpus.Targets.Sum(target => target.Seeds.Length));
        Assert.IsTrue(corpus.Targets.All(target => target.Seeds.Length >= 4));
        Assert.IsTrue(corpus.Limits.MaxArrayElements <= 256);
        Assert.IsTrue(corpus.Limits.MaxTotalBytesRead <= 4096);
        Assert.IsTrue(corpus.Limits.MaxTotalBytesWritten <= 4096);
    }

    /// <summary>
    ///     Running the reviewed corpus produces 792 seed and mutation outcomes.
    /// </summary>
    /// <remarks>
    ///     Success counts, documented-failure counts, and digests must match the saved expectations for every target.
    ///     Each digest covers every input and its outcome: the canonical rendering of the value a success produced, or
    ///     the failure's type, message, path and offset, so a changed value or diagnostic changes the digest even when
    ///     the counts stay the same. Stable results make unexpected behavior changes visible and let the same cases be
    ///     reproduced across supported .NET targets; the fuzz CLI (<c>--target all</c> with the corpus's iteration
    ///     count) prints the digests of a deliberate behavior change for review. The expression target's recorded
    ///     outcomes include the <c>%</c>, <c>^</c> and <c>?:</c> operators. The generated-differential target (the
    ///     generated readers and writers against the runtime) has no documented failures by construction: every input
    ///     must produce the same outcome on both paths.
    /// </remarks>
    [TestMethod]
    public void ReviewedRun_MatchesTheFrozenReplayManifest()
    {
        FuzzReport report = new FuzzSession(LoadCorpus()).Run();
        var expected = new Dictionary<string, (int Successes, int Failures, string Digest)>(StringComparer.Ordinal)
        {
            ["binary-roundtrip"] = (
                17,
                115,
                "784CCA52D4652FDC2ACD77B65411EF2C2AE2AD77AF41F6C6FBDA8F2025509855"),
            ["definition"] = (
                4,
                128,
                "FF52028EDEA5FA1684856EC77144469E3825103DA99176BB169CB24FB8596E0A"),
            ["expression"] = (
                5,
                127,
                "5D04AD2D65FAFE50545C14375A5077D897715D90BC022CE9CCC337ED77256930"),
            ["generated-differential"] = (
                132,
                0,
                "98CBE527CC19DBAF2CE09E15E7989A71A594A6E2E173522C02612A3A1A430D97"),
            ["path"] = (
                0,
                132,
                "775BD3B059217545A7CE36B8540196006D5F741123AC2DC97C13E54A6A7FE6E0"),
            ["pointer-union"] = (
                26,
                106,
                "F44DABA1AEC122889F0017147700F0CC83CC2C9E5F7A554E1D094BC1831CB69A"),
        };

        Assert.AreEqual(1, report.SchemaVersion);
        Assert.AreEqual("0x46555A5A51413034", report.Seed);
        Assert.AreEqual(128, report.IterationsPerTarget);
        Assert.AreEqual(6, report.Targets.Length);
        Assert.AreEqual(792, report.Targets.Sum(target => target.Successes + target.DocumentedFailures));

        foreach (FuzzTargetReport target in report.Targets)
        {
            (int successes, int failures, string digest) = expected[target.Id];
            Assert.AreEqual(4, target.SeedCases, target.Id);
            Assert.AreEqual(128, target.MutationCases, target.Id);
            Assert.AreEqual(successes, target.Successes, target.Id);
            Assert.AreEqual(failures, target.DocumentedFailures, target.Id);
            Assert.AreEqual(digest, target.Digest, target.Id);
        }
    }

    /// <summary>
    ///     The same custom random seed and iteration count are used twice.
    /// </summary>
    /// <remarks>
    ///     Both runs must produce identical digests and outcome counts. Reproducibility matters because a random
    ///     malformed layout or byte sequence is only useful for debugging if the exact failure can be generated again.
    /// </remarks>
    [TestMethod]
    public void CustomSeed_ReplaysIdentically()
    {
        var session = new FuzzSession(LoadCorpus());

        FuzzReport first = session.Run(iterations: 16, seed: 0x0123456789ABCDEF);
        FuzzReport second = session.Run(iterations: 16, seed: 0x0123456789ABCDEF);

        CollectionAssert.AreEqual(
            first.Targets.Select(target => target.Digest).ToArray(),
            second.Targets.Select(target => target.Digest).ToArray());
        CollectionAssert.AreEqual(
            first.Targets.Select(target => target.Successes).ToArray(),
            second.Targets.Select(target => target.Successes).ToArray());
        CollectionAssert.AreEqual(
            first.Targets.Select(target => target.DocumentedFailures).ToArray(),
            second.Targets.Select(target => target.DocumentedFailures).ToArray());
    }

    /// <summary>
    ///     One supplied binary input must round-trip successfully, while the incomplete definition struct root { must
    ///     be classified as a documented failure.
    /// </summary>
    /// <remarks>
    ///     Single-input replay must preserve that distinction. An expected rejection of malformed input is successful
    ///     fuzz handling, not a defect by itself.
    /// </remarks>
    [TestMethod]
    public void SingleInput_ReplaysSuccessAndDocumentedFailure()
    {
        var session = new FuzzSession(LoadCorpus());

        FuzzReport success = session.RunSingle(
            "binary-roundtrip",
            Convert.FromHexString("020102030400"));
        FuzzReport failure = session.RunSingle(
            "definition",
            System.Text.Encoding.UTF8.GetBytes("struct root {"));

        Assert.AreEqual(1, success.Targets.Single().Successes);
        Assert.AreEqual(0, success.Targets.Single().DocumentedFailures);
        Assert.AreEqual(0, failure.Targets.Single().Successes);
        Assert.AreEqual(1, failure.Targets.Single().DocumentedFailures);
    }

    /// <summary>
    ///     Requests that exceed the reviewed input bounds, use negative work counts, or name an unknown target must
    ///     fail before execution.
    /// </summary>
    /// <remarks>
    ///     The fuzz harness must not silently expand its resource use or run a different target from the one requested.
    /// </remarks>
    [TestMethod]
    public void Run_RejectsUnreviewedBoundsAndUnknownTargets()
    {
        var session = new FuzzSession(LoadCorpus());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.Run(maxInputBytes: LoadCorpus().MaxInputBytes + 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.Run(iterations: -1));
        Assert.Throws<InvalidOperationException>(
            () => session.Run("unknown", iterations: 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.RunSingle("definition", new byte[LoadCorpus().MaxInputBytes + 1]));
    }

    /// <summary>Loads the checked-in fuzz corpus copied beside the test assembly.</summary>
    /// <returns>The loaded corpus.</returns>
    private static FuzzCorpus LoadCorpus()
    {
        return FuzzCorpus.Load(Path.Combine(AppContext.BaseDirectory, "fuzz-corpus.json"));
    }
}
