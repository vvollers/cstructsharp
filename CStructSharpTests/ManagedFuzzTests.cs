namespace CStructSharp.Tests;

using CStructSharp.Fuzzing;

/// <summary>Executes the retained QA-04 corpus and stable mutation engine on every supported framework.</summary>
[TestClass]
public class ManagedFuzzTests
{
    /// <summary>The reviewed corpus keeps all five targets, bounded limits, and at least four seeds per target.</summary>
    [TestMethod]
    public void Corpus_DefinesEveryBoundedManagedTarget()
    {
        FuzzCorpus corpus = LoadCorpus();

        Assert.AreEqual(1, corpus.SchemaVersion);
        Assert.AreEqual("0x46555A5A51413034", corpus.Seed);
        Assert.AreEqual(128, corpus.IterationsPerTarget);
        Assert.AreEqual(256, corpus.MaxInputBytes);
        CollectionAssert.AreEquivalent(
            new[] { "binary-roundtrip", "definition", "expression", "path", "pointer-union", },
            corpus.Targets.Select(target => target.Id).ToArray());
        Assert.AreEqual(20, corpus.Targets.Sum(target => target.Seeds.Length));
        Assert.IsTrue(corpus.Targets.All(target => target.Seeds.Length >= 4));
        Assert.IsTrue(corpus.Limits.MaxArrayElements <= 256);
        Assert.IsTrue(corpus.Limits.MaxTotalBytesRead <= 4096);
        Assert.IsTrue(corpus.Limits.MaxTotalBytesWritten <= 4096);
    }

    /// <summary>The complete reviewed run has stable cross-TFM inputs, classifications, counts, and replay digests.</summary>
    [TestMethod]
    public void ReviewedRun_MatchesTheFrozenReplayManifest()
    {
        FuzzReport report = new FuzzSession(LoadCorpus()).Run();
        var expected = new Dictionary<string, (int Successes, int Failures, string Digest)>(StringComparer.Ordinal)
        {
            ["binary-roundtrip"] = (
                17,
                115,
                "BE7FD9D9D5066B8BF4E67995D1BF32E47E3C952568E53993571A478FC553F40A"),
            ["definition"] = (
                4,
                128,
                "7656099E98432A41718C6821C82A588E492D4271FC4E4AC70AD855E65D313095"),
            ["expression"] = (
                4,
                128,
                "6665169390EA6ED68F9870863353BD085EF27B845ED68765FAFCD88FC3428801"),
            ["path"] = (
                1,
                131,
                "8DF27848550CE141FC27DCAAD7542BE08BC4423739D8666E72B4E7A14D294EA5"),
            ["pointer-union"] = (
                26,
                106,
                "E0CBD9D958149264D0E5481F7477E6C9034DE9E94716381601627AA30C2CBE7C"),
        };

        Assert.AreEqual(1, report.SchemaVersion);
        Assert.AreEqual("0x46555A5A51413034", report.Seed);
        Assert.AreEqual(128, report.IterationsPerTarget);
        Assert.AreEqual(5, report.Targets.Length);
        Assert.AreEqual(660, report.Targets.Sum(target => target.Successes + target.DocumentedFailures));

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

    /// <summary>A custom replay seed produces the same report every time without using System.Random.</summary>
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

    /// <summary>Single-input mode supports exact external-engine replay while retaining target failure policy.</summary>
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

    /// <summary>Callers cannot silently enlarge a run beyond the reviewed input/resource envelope.</summary>
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

    private static FuzzCorpus LoadCorpus()
    {
        return FuzzCorpus.Load(Path.Combine(AppContext.BaseDirectory, "fuzz-corpus.json"));
    }
}
