namespace CStructSharp.Memory;

/// <summary>Why a sequential scan ended. Cancellation and exhausted shared budgets terminate by throwing.</summary>
public enum MemoryScanStop
{
    /// <summary>Every declared range was attempted; unavailable bytes are reported separately.</summary>
    RangeExhausted,

    /// <summary>The visitor stopped, for example at its explicit hit or retained-result limit.</summary>
    VisitorStopped,

    /// <summary>The configured maximum number of windows was reached before range exhaustion.</summary>
    WindowLimit,

    /// <summary>A source generation changed during this scan; earlier partial results remain caller-owned.</summary>
    SourceChanged,
}
