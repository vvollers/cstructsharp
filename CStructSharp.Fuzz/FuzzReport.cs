namespace CStructSharp.Fuzzing;

/// <summary>Describes one stable managed fuzz run.</summary>
public sealed class FuzzReport
{
    public int SchemaVersion { get; init; }

    public string Seed { get; init; } = string.Empty;

    public int IterationsPerTarget { get; init; }

    public int MaxInputBytes { get; init; }

    public FuzzTargetReport[] Targets { get; init; } = [];
}
