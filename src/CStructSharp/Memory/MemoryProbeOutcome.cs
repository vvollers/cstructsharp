namespace CStructSharp.Memory;

/// <summary>Local recognition evidence; a match never proves allocation identity or liveness.</summary>
public enum MemoryProbeOutcome
{
    /// <summary>All required bytes were known and the predicate passed.</summary>
    Match,

    /// <summary>All required bytes were known and the predicate rejected them.</summary>
    Mismatch,

    /// <summary>Some required storage bytes were absent or unknown.</summary>
    Unavailable,
}
