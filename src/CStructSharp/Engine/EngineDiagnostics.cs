namespace CStructSharp.Engine;

using System.Threading;

/// <summary>
///     Counts, inside one test recording, the operations that entered the compiled engine (<see cref="EnginePrograms"/>),
///     and the kind of the latest one. A direct fixed-root read, a fast path taken before the engine, or a call that fails
///     before it reaches the engine is not counted, so a test can tell which calls took the general path. Only tests create
///     recordings and read the counts.
/// </summary>
/// <remarks>
///     <para>
///         A test opens a recording with <c>using EngineRecording recording = EngineDiagnostics.Record();</c>, runs
///         operations, and reads <see cref="EngineRecording.Diagnostics"/>. The recording is an
///         <see cref="AsyncLocal{T}"/> value: it flows into the awaits and tasks the test starts, and it is invisible to
///         every other test, so tests that MSTest runs in parallel never see each other's operations. Disposing the
///         recording restores the one it replaced, so recordings nest.
///     </para>
///     <para>
///         Production code pays nothing for this: an operation reads the <see cref="AsyncLocal{T}"/> only while a static
///         count of open recordings is non-zero, so outside tests every operation costs one plain static read, and no
///         option or operation state carries a recorder.
///     </para>
///     <para>
///         The counter is safe for concurrent use, so the parallel operations of one recording (the records of a
///         sequence, the tasks of one test) are all counted.
///     </para>
/// </remarks>
internal sealed class EngineDiagnostics
{
    /// <summary>The recorder of the calling flow's innermost open recording, or <see langword="null"/> outside one.</summary>
    private static readonly AsyncLocal<EngineDiagnostics?> Recorder = new();

    /// <summary>The number of recordings open anywhere in the process; while it is zero nobody reads <see cref="Recorder"/>.</summary>
    private static int openRecordings;

    /// <summary>The number of operations the engine ran.</summary>
    private int runs;

    /// <summary>The <see cref="EngineOperation"/> of the latest operation as an integer, or -1 before the first one.</summary>
    private int lastOperation = -1;

    /// <summary>
    ///     Gets the recorder of the calling flow's recording, or <see langword="null"/>. While no recording is open
    ///     anywhere this is one plain static read and never touches the <see cref="AsyncLocal{T}"/>.
    /// </summary>
    public static EngineDiagnostics? Current => openRecordings == 0 ? null : Recorder.Value;

    /// <summary>Gets the number of operations the engine ran.</summary>
    public int Runs => Volatile.Read(ref this.runs);

    /// <summary>Gets the kind of the latest operation the engine ran, or <see langword="null"/> before the first one.</summary>
    public EngineOperation? LastOperation => Volatile.Read(ref this.lastOperation) is int operation and >= 0 ? (EngineOperation)operation : null;

    /// <summary>
    ///     Opens a recording on the calling flow: every operation run on this flow and the tasks it starts is counted in
    ///     the recording's recorder until the recording is disposed.
    /// </summary>
    /// <param name="diagnostics">The recorder to count into, or <see langword="null"/> for a new one.</param>
    /// <returns>The open recording; dispose it on the flow that opened it.</returns>
    public static EngineRecording Record(EngineDiagnostics? diagnostics = null)
    {
        var recording = new EngineRecording(diagnostics ?? new EngineDiagnostics(), Recorder.Value);

        // Count first, then publish: an operation that sees the recorder always passes the counter check.
        Interlocked.Increment(ref openRecordings);
        Recorder.Value = recording.Diagnostics;
        return recording;
    }

    /// <summary>Closes a recording: restores the recorder it replaced on the calling flow and uncounts it.</summary>
    /// <param name="previous">The recorder that was current when the recording opened.</param>
    internal static void Close(EngineDiagnostics? previous)
    {
        Recorder.Value = previous;
        Interlocked.Decrement(ref openRecordings);
    }

    /// <summary>Records that the engine ran one operation.</summary>
    /// <param name="operation">The kind of operation.</param>
    public void RecordRun(EngineOperation operation)
    {
        Volatile.Write(ref this.lastOperation, (int)operation);
        Interlocked.Increment(ref this.runs);
    }
}
