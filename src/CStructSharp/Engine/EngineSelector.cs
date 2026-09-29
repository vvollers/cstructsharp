namespace CStructSharp.Engine;

using System;
using System.Runtime.CompilerServices;

/// <summary>
///     The single decision point between the compiled engine and the interpreter. Every public operation calls
///     <see cref="Decide"/> once, after its direct fixed-root fast path declined and before the general path reads or
///     writes anything, so an operation is run by one implementation from start to end.
/// </summary>
/// <remarks>
///     The engine supports no operation yet, so every decision declines with <see cref="OperationNotSupported"/> and
///     the interpreter runs; the static read and write plans the interpreter uses for fixed composites belong to it.
///     A decision under <see cref="EngineSelection.Automatic"/> allocates nothing and formats no text; outside a test
///     recording (<see cref="EngineDiagnostics.Record"/>) it reads one static counter and nothing else.
/// </remarks>
internal static class EngineSelector
{
    /// <summary>The reason the engine declines an operation it cannot run yet.</summary>
    public const string OperationNotSupported = "the compiled engine does not support this operation yet";

    /// <summary>
    ///     Decides which implementation runs one operation and records the decision in the recording active on the
    ///     calling flow, if any. Returning means the interpreter runs the operation.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="operation">The kind of operation.</param>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="selection"/> is <see cref="EngineSelection.EngineRequired"/> and the engine declined the
    ///     operation; the message names the operation and the reason.
    /// </exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Decide(EngineSelection selection, EngineOperation operation)
    {
        // The production case - automatic selection, no recording open - is one comparison and one static read.
        if (selection != EngineSelection.Automatic || EngineDiagnostics.IsRecording)
        {
            DecideAndRecord(selection, operation);
        }
    }

    /// <summary>The rest of <see cref="Decide"/>: a forced or required selection, or a decision made while a recording is open.</summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="operation">The kind of operation.</param>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DecideAndRecord(EngineSelection selection, EngineOperation operation)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(operation);
            return;
        }

        // No operation is eligible yet: Automatic and EngineRequired both decline with the same reason.
        EngineDiagnostics.Current?.RecordDecline(operation, OperationNotSupported);
        if (selection == EngineSelection.EngineRequired)
        {
            ThrowRequired(operation, OperationNotSupported);
        }
    }

    /// <summary>Fails an operation whose selection required the engine after the engine declined it.</summary>
    /// <param name="operation">The declined operation.</param>
    /// <param name="reason">Why the engine declined it.</param>
    /// <exception cref="InvalidOperationException">Always.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowRequired(EngineOperation operation, string reason)
        => throw new InvalidOperationException($"The compiled engine is required but declined the {operation} operation: {reason}.");
}
