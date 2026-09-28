namespace CStructSharp.Fuzzing;

/// <summary>Summarizes deterministic outcomes for one target.</summary>
public sealed class FuzzTargetReport
{
    /// <summary>Gets the target name, such as <c>definition</c> or <c>path</c>.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the number of retained corpus seeds executed unchanged.</summary>
    public int SeedCases { get; init; }

    /// <summary>Gets the number of mutated inputs executed after the seeds.</summary>
    public int MutationCases { get; init; }

    /// <summary>Gets the number of cases the target completed without an exception.</summary>
    public int Successes { get; init; }

    /// <summary>
    ///     Gets the number of cases that threw an exception the target documents as an expected rejection;
    ///     any other exception stops the run instead of being counted.
    /// </summary>
    public int DocumentedFailures { get; init; }

    /// <summary>
    ///     Gets the uppercase hexadecimal SHA-256 over every case's target name, input length, input bytes and
    ///     outcome, so two runs with the same seed can be compared for identical behavior.
    /// </summary>
    public string Digest { get; init; } = string.Empty;
}
