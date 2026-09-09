namespace CStructSharp.Fuzzing;

/// <summary>Groups the retained seeds for one named fuzz target.</summary>
public sealed class FuzzTargetCorpus
{
    public string Id { get; init; } = string.Empty;

    public FuzzSeed[] Seeds { get; init; } = [];
}
