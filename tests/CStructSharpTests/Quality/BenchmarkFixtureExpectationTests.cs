namespace CStructSharp.Tests;

using System.Text.Json;
using CStructSharp.FixtureTool;

/// <summary>
///     Checks every benchmark fixture (<c>benchmarks/fixtures</c>) against the expectation recorded in its case file,
///     with the fixture tool's own evaluation and comparison (<see cref="FixtureVerification"/>), so the ordinary test
///     run catches what <c>CStructSharp.FixtureTool verify</c> catches.
/// </summary>
/// <remarks>
///     The expectations are reviewed files, not the output of the reader under test: a successful read must produce
///     canonical JSON with the recorded SHA-256 and length (and, when stored inline, the recorded value), and a failing
///     read must throw the recorded exception type. They are an oracle independent of any one read path.
/// </remarks>
[TestClass]
public class BenchmarkFixtureExpectationTests
{
    /// <summary>The number of fixtures the corpus holds; a loader that loses cases fails the inventory test.</summary>
    private const int FixtureCount = 63;

    /// <summary>Gets every fixture id listed in the manifest as a data row.</summary>
    public static IEnumerable<object[]> FixtureIds => ManifestIds().Select(id => new object[] { id, });

    /// <summary>
    ///     The manifest lists exactly the case files, and every case records an expectation: a hash of its value, an
    ///     exception type, or no bytes at all (a compile-only case), so no case can pass by recording nothing.
    /// </summary>
    [TestMethod]
    public void Manifest_ListsEveryCaseWithARecordedExpectation()
    {
        string directory = TestFixtures.BenchmarkFixtures;
        List<string> ids = ManifestIds();
        List<string> files = Directory.GetFiles(Path.Combine(directory, "cases"), "*.json")
                                      .Select(Path.GetFileNameWithoutExtension)
                                      .Select(name => name!)
                                      .ToList();

        Assert.HasCount(FixtureCount, ids);
        CollectionAssert.AreEquivalent(files, ids);
        foreach (string id in ids)
        {
            FixtureDocument fixture = FixtureLoader.Load(directory, id);
            bool compileOnly = fixture.Bytes is null || fixture.Definitions is { Count: > 0 };
            Assert.IsTrue(compileOnly || fixture.ExpectedSha256 is not null || fixture.ExpectedError is not null, id + " records no expectation");
        }
    }

    /// <summary>
    ///     A fixture, read with its layout, variables, and read options, produces exactly the recorded outcome.
    /// </summary>
    /// <param name="id">The fixture id, which is also its case file name.</param>
    [TestMethod]
    [DynamicData(nameof(FixtureIds))]
    public void Fixture_MatchesItsRecordedExpectation(string id)
    {
        string directory = TestFixtures.BenchmarkFixtures;
        FixtureDocument fixture = FixtureLoader.Load(directory, id);

        FixtureOutcome outcome = FixtureVerification.Evaluate(directory, fixture);

        string? mismatch = FixtureVerification.Compare(fixture, outcome);
        Assert.IsNull(mismatch, $"{id} ({outcome.Describe()}): {mismatch}");
    }

    /// <summary>Reads the fixture ids from <c>manifest.json</c> in their listed order.</summary>
    /// <returns>The ids.</returns>
    private static List<string> ManifestIds()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestFixtures.BenchmarkFixtures, "manifest.json")));
        return manifest.RootElement.GetProperty("fixtures").EnumerateArray().Select(item => item.GetProperty("id").GetString()!).ToList();
    }
}
