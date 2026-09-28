namespace CStructSharp.Fuzzing;

/// <summary>Describes one stable managed fuzz run.</summary>
public sealed class FuzzReport
{
    /// <summary>Gets the report format version; the harness writes 1.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>Gets the hexadecimal mutation seed the run used, prefixed with <c>0x</c>.</summary>
    public string Seed { get; init; } = string.Empty;

    /// <summary>Gets the number of mutated inputs generated for each target; 0 for a single-input run.</summary>
    public int IterationsPerTarget { get; init; }

    /// <summary>Gets the maximum size in bytes of one generated input.</summary>
    public int MaxInputBytes { get; init; }

    /// <summary>Gets the per-target outcome summaries, one for each target that ran.</summary>
    public FuzzTargetReport[] Targets { get; init; } = [];
}
