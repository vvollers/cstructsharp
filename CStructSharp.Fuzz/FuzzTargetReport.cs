namespace CStructSharp.Fuzzing;

/// <summary>Summarizes deterministic outcomes for one target.</summary>
public sealed class FuzzTargetReport
{
    public string Id { get; init; } = string.Empty;

    public int SeedCases { get; init; }

    public int MutationCases { get; init; }

    public int Successes { get; init; }

    public int DocumentedFailures { get; init; }

    public string Digest { get; init; } = string.Empty;
}
