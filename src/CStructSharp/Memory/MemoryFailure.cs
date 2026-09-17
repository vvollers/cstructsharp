namespace CStructSharp.Memory;

/// <summary>The stable categories of <see cref="MemoryAccessException"/>.</summary>
/// <remarks>
/// An analyzer uses the category to decide what to do next: show a partial result, raise an explicit work limit,
/// discard a stale edit, or report a transport problem. Missing data is never represented as an implicit zero
/// value. Argument errors, codec errors, and cooperative cancellation use other exception types and therefore
/// have no category here.
/// </remarks>
public enum MemoryFailure
{
    /// <summary>No mapping covers the requested address, or a write falls outside the source's extent.</summary>
    Unmapped,

    /// <summary>A backing source returned zero bytes before the requested range was complete, for example a truncated file.</summary>
    MissingBytes,

    /// <summary>The operation's byte, request, or depth limit was reached, or an overlay's changed-byte capacity was exceeded.</summary>
    BudgetExceeded,

    /// <summary>A source's generation or bytes differ from the state a patch or cache expected.</summary>
    StaleSource,

    /// <summary>A transport or backing store failed, or an adapter returned an invalid byte count.</summary>
    SourceFailure,

    /// <summary>An addressed operation cannot use the value it found, such as following a null pointer.</summary>
    InvalidValue,
}
