#pragma warning disable RCS1194 // Structured failures require address or commit context; context-free constructors are intentionally absent.
namespace CStructSharp.Memory;

/// <summary>A write failed after a patch commit had started changing bytes. Reports how much is confirmed and which fragment is uncertain.</summary>
/// <remarks>
/// <para>
/// This exception is thrown only from the write phase of <see cref="MemoryPatch.Commit"/>; validation failures
/// before the first write keep their ordinary exception types because nothing was changed. Its meaning is
/// precise: every fragment before <see cref="FragmentIndex"/> returned success and contributes to
/// <see cref="CompletedBytes"/>; the fragment at <see cref="FragmentIndex"/> threw and may have written any prefix
/// of its bytes, even when <see cref="CompletedBytes"/> is zero. <see cref="Exception.InnerException"/> holds the
/// transport, budget, or cancellation cause.
/// </para>
/// <para>
/// There is no rollback, because arbitrary sources share no transaction. Inspect or discard the affected offline
/// copy and re-plan from a known state; retrying the same patch is not a recovery protocol.
/// </para>
/// </remarks>
public sealed class MemoryPatchCommitException : Exception
{
    /// <summary>Records the confirmed progress and the index of the fragment whose write failed.</summary>
    /// <param name="completedBytes">Bytes of fragments that completed before the failure.</param>
    /// <param name="fragmentIndex">Zero-based index of the fragment whose write threw.</param>
    /// <param name="inner">The exception thrown by that write.</param>
    internal MemoryPatchCommitException(long completedBytes, int fragmentIndex, Exception inner)
        : base("Patch commit failed; the failing fragment may be partially written. No rollback is implied.", inner)
    {
        this.CompletedBytes = completedBytes;
        this.FragmentIndex = fragmentIndex;
    }

    /// <summary>Gets the total bytes of fragments confirmed written before the failing one.</summary>
    public long CompletedBytes { get; }

    /// <summary>Gets the zero-based index of the fragment whose completion is uncertain.</summary>
    public int FragmentIndex { get; }
}
