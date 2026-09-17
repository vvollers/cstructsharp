namespace CStructSharp.Memory;

/// <summary>Why a <see cref="MemoryWalker"/> traversal stopped.</summary>
/// <remarks>
/// Only two values mean normal completion: <see cref="Sentinel"/> for a list that returned to its head and
/// <see cref="Complete"/> for a tree with no pending children. The others describe bounded partial results and
/// must be presented as such. Revisiting a data node is deliberately distinct from reaching the sentinel, because
/// the first means the image is corrupt and the second means the list ended. Budget exhaustion and cancellation
/// are exceptions, not stop values, so they cannot be mistaken for an ordinary end of traversal.
/// </remarks>
public enum MemoryWalkStop
{
    /// <summary>The list's link returned to the head node it started from: normal completion.</summary>
    Sentinel,

    /// <summary>The list's link pointed at a data node that was already returned: a corrupt cycle.</summary>
    RepeatedNode,

    /// <summary>Another distinct node was found but returning it would exceed the configured node limit.</summary>
    NodeLimit,

    /// <summary>A callback hit an unmapped address or missing bytes; the result's failure describes where.</summary>
    Unavailable,

    /// <summary>Every distinct reachable tree node was visited: normal completion.</summary>
    Complete,
}
