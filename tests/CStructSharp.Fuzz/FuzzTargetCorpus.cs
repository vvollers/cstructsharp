namespace CStructSharp.Fuzzing;

/// <summary>Groups the retained seeds for one named fuzz target.</summary>
public sealed class FuzzTargetCorpus
{
    /// <summary>Gets the name of the fuzz target these seeds belong to.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets the retained seed inputs that start the target's mutation sequence.</summary>
    public FuzzSeed[] Seeds { get; init; } = [];
}
