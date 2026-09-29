namespace CStructSharp.Engine;

using System.Collections.Generic;
using System.Threading;

/// <summary>
///     Counts the engine selector's decisions made inside one test recording: how many operations the engine ran, how
///     many it declined (with the most recent reasons), and how many were sent to the interpreter by
///     <see cref="EngineSelection.InterpreterOnly"/>. Only tests create recordings and read the counts.
/// </summary>
/// <remarks>
///     <para>
///         A test opens a recording with <c>using EngineRecording recording = EngineDiagnostics.Record();</c>, runs
///         operations, and reads <see cref="EngineRecording.Diagnostics"/>. The recording is an
///         <see cref="AsyncLocal{T}"/> value: it flows into the awaits and tasks the test starts, and it is invisible to
///         every other test, so tests that MSTest runs in parallel never see each other's decisions. Disposing the
///         recording restores the one it replaced, so recordings nest.
///     </para>
///     <para>
///         Production code pays nothing for this: the selector reads the <see cref="AsyncLocal{T}"/> only while a
///         static count of open recordings is non-zero, so outside tests every decision costs one plain static read,
///         and no option or operation state carries a recorder. The plan's rule against <see cref="AsyncLocal{T}"/>
///         (decision D-14) concerns the engine selection, which stays an option snapshotted per operation; this
///         recorder is test-only and gated by that count.
///     </para>
///     <para>
///         The counters are safe for concurrent use, so the parallel operations of one recording (the records of a
///         sequence, the tasks of one test) are all counted.
///     </para>
/// </remarks>
internal sealed class EngineDiagnostics
{
    /// <summary>How many of the most recent declines <see cref="RecentDeclines"/> keeps.</summary>
    public const int KeptDeclines = 16;

    /// <summary>The recorder of the calling flow's innermost open recording, or <see langword="null"/> outside one.</summary>
    private static readonly AsyncLocal<EngineDiagnostics?> Recorder = new();

    /// <summary>The number of recordings open anywhere in the process; while it is zero nobody reads <see cref="Recorder"/>.</summary>
    private static int openRecordings;

    /// <summary>Guards <see cref="recentDeclines"/>; the counters are updated with interlocked operations.</summary>
    private readonly object gate = new();

    /// <summary>The most recent declines, oldest first, at most <see cref="KeptDeclines"/>.</summary>
    private readonly Queue<EngineDecline> recentDeclines = new();

    /// <summary>The number of operations the engine ran.</summary>
    private int engineRuns;

    /// <summary>The number of operations the engine was asked for and declined.</summary>
    private int declines;

    /// <summary>The number of operations sent to the interpreter without asking the engine.</summary>
    private int interpreterSelections;

    /// <summary>The <see cref="EngineOperation"/> of the latest decision as an integer, or -1 before the first one.</summary>
    private int lastOperation = -1;

    /// <summary>
    ///     Gets the recorder of the calling flow's recording, or <see langword="null"/>. While no recording is open
    ///     anywhere this is one plain static read and never touches the <see cref="AsyncLocal{T}"/>.
    /// </summary>
    public static EngineDiagnostics? Current => openRecordings == 0 ? null : Recorder.Value;

    /// <summary>Gets whether any recording is open anywhere in the process; one plain static read.</summary>
    public static bool IsRecording => openRecordings != 0;

    /// <summary>Gets the number of operations the engine ran.</summary>
    public int EngineRuns => Volatile.Read(ref this.engineRuns);

    /// <summary>Gets the number of operations the engine was asked for and declined; the interpreter ran them unless the engine was required.</summary>
    public int Declines => Volatile.Read(ref this.declines);

    /// <summary>Gets the number of operations <see cref="EngineSelection.InterpreterOnly"/> sent to the interpreter without asking the engine.</summary>
    public int InterpreterSelections => Volatile.Read(ref this.interpreterSelections);

    /// <summary>Gets the total number of decisions recorded: engine runs, declines, and interpreter selections.</summary>
    public int Decisions => this.EngineRuns + this.Declines + this.InterpreterSelections;

    /// <summary>Gets the kind of operation the latest decision was for, or <see langword="null"/> before the first decision.</summary>
    public EngineOperation? LastOperation => Volatile.Read(ref this.lastOperation) is int operation and >= 0 ? (EngineOperation)operation : null;

    /// <summary>Gets the most recent decline, or <see langword="null"/> when the engine has declined nothing.</summary>
    public EngineDecline? LastDecline
    {
        get
        {
            lock (this.gate)
            {
                EngineDecline? last = null;
                foreach (EngineDecline decline in this.recentDeclines)
                {
                    last = decline;
                }

                return last;
            }
        }
    }

    /// <summary>Gets a copy of the most recent declines, oldest first, at most <see cref="KeptDeclines"/>.</summary>
    public IReadOnlyList<EngineDecline> RecentDeclines
    {
        get
        {
            lock (this.gate)
            {
                return this.recentDeclines.ToArray();
            }
        }
    }

    /// <summary>
    ///     Opens a recording on the calling flow: every decision made on this flow and the tasks it starts is counted
    ///     in the recording's recorder until the recording is disposed.
    /// </summary>
    /// <param name="diagnostics">The recorder to count into, or <see langword="null"/> for a new one.</param>
    /// <returns>The open recording; dispose it on the flow that opened it.</returns>
    public static EngineRecording Record(EngineDiagnostics? diagnostics = null)
    {
        var recording = new EngineRecording(diagnostics ?? new EngineDiagnostics(), Recorder.Value);

        // Count first, then publish: a decision that sees the recorder always passes the counter check.
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
        Interlocked.Increment(ref this.engineRuns);
    }

    /// <summary>Records that <see cref="EngineSelection.InterpreterOnly"/> sent one operation to the interpreter.</summary>
    /// <param name="operation">The kind of operation.</param>
    public void RecordInterpreterSelection(EngineOperation operation)
    {
        Volatile.Write(ref this.lastOperation, (int)operation);
        Interlocked.Increment(ref this.interpreterSelections);
    }

    /// <summary>Records that the engine declined one operation, keeping the reason among the most recent ones.</summary>
    /// <param name="operation">The kind of operation.</param>
    /// <param name="reason">Why the engine declined it.</param>
    public void RecordDecline(EngineOperation operation, string reason)
    {
        lock (this.gate)
        {
            // The count and the kept reasons change together, so a reader never sees a count without its reason.
            Volatile.Write(ref this.lastOperation, (int)operation);
            Interlocked.Increment(ref this.declines);
            this.recentDeclines.Enqueue(new EngineDecline(operation, reason));
            if (this.recentDeclines.Count > KeptDeclines)
            {
                this.recentDeclines.Dequeue();
            }
        }
    }
}
