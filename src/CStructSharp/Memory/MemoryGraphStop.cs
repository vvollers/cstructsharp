namespace CStructSharp.Memory;

/// <summary>Explicit bounded graph completion; exceptions, cancellation and shared budgets propagate.</summary>
public enum MemoryGraphStop
{
    /// <summary>All reachable accepted nodes were visited.</summary>
    Exhausted,

    /// <summary>The visitor stopped at a selected result.</summary>
    VisitorStopped,

    /// <summary>The node limit prevented further visits.</summary>
    NodeLimit,

    /// <summary>The edge enumeration limit prevented further exploration.</summary>
    EdgeLimit,

    /// <summary>At least one branch exceeded the depth limit; other branches were completed.</summary>
    DepthLimit,

    /// <summary>The pending frontier limit prevented enqueuing another node.</summary>
    PendingLimit,
}
