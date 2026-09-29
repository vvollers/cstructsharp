namespace CStructSharp.Tests;

/// <summary>
///     The golden outcomes of one test (a test method, or one data row of it) in a <see cref="GoldenManifest"/>: either
///     every outcome readable under its case key, or one hash per group of outcomes.
/// </summary>
internal sealed class GoldenSection
{
    /// <summary>Creates a section.</summary>
    /// <param name="testId">The test's id (<see cref="GoldenTestIds.Of(TestContext)"/>).</param>
    /// <param name="hashed">Whether the entries are hashed groups rather than readable outcomes.</param>
    /// <param name="entries">The entries in the order the test produced them; keys are unique.</param>
    /// <exception cref="FormatException">Two entries share a key.</exception>
    public GoldenSection(string testId, bool hashed, IReadOnlyList<GoldenEntry> entries)
    {
        this.TestId = testId;
        this.Hashed = hashed;
        this.Entries = entries;
        foreach (GoldenEntry entry in entries)
        {
            if (!this.ByKey.TryAdd(entry.Key, entry))
            {
                throw new FormatException(testId + ": the golden key '" + entry.Key + "' appears twice.");
            }
        }
    }

    /// <summary>Gets the test's id.</summary>
    public string TestId { get; }

    /// <summary>Gets whether the entries are hashed groups rather than readable outcomes.</summary>
    public bool Hashed { get; }

    /// <summary>Gets the entries in the order the test produced them.</summary>
    public IReadOnlyList<GoldenEntry> Entries { get; }

    /// <summary>Gets the entries by key.</summary>
    public Dictionary<string, GoldenEntry> ByKey { get; } = new(StringComparer.Ordinal);
}
