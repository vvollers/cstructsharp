namespace CStructSharp.Memory;

/// <summary>Application decision after probing a streamed graph node.</summary>
public enum MemoryGraphVisit
{
    /// <summary>Enumerate this node's outgoing edges.</summary>
    Expand,

    /// <summary>This branch has no usable evidence; continue other branches.</summary>
    SkipBranch,

    /// <summary>Stop immediately, preserving the visitor's retained partial results and first-result selection.</summary>
    Stop,
}
