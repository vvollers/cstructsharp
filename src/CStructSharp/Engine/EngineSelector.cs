namespace CStructSharp.Engine;

using System;
using System.Runtime.CompilerServices;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Expressions;

/// <summary>
///     The single decision point between the compiled engine and the interpreter. Every public operation asks once,
///     after its direct fixed-root fast path declined and before the general path reads or writes anything, so an
///     operation is run by one implementation from start to end.
/// </summary>
/// <remarks>
///     <para>
///         The engine runs whole-root reads (<see cref="SelectRootRead"/>: <c>Parse</c>, <c>ParseAsync</c>, each record of
///         <c>ParseMany</c>, and <c>ReadValue</c> of a bare root, over every source) whose root program is eligible
///         (<see cref="LayoutCompilation.GetRootReadProgram"/>). Every other operation, and a root read it cannot
///         reproduce, is declined before anything is read, and the interpreter runs it (<see cref="Decide"/>).
///     </para>
///     <para>
///         Outside a test recording (<see cref="EngineDiagnostics.Record"/>) a decision records nothing: a declined
///         operation costs one comparison and one static read, a root read one program lookup in the layout's cache.
///     </para>
/// </remarks>
internal static class EngineSelector
{
    /// <summary>The reason the engine declines an operation it cannot run yet.</summary>
    public const string OperationNotSupported = "the compiled engine does not support this operation yet";

    /// <summary>The reason the engine declines an operation whose variables are internal expression inputs rather than integers.</summary>
    public const string ExpressionInputs = "caller variables given as expressions are not supported yet (stage 10)";

    /// <summary>
    ///     The reason the engine declines <c>ReadValue</c> of a root that is a runtime-sized array (a type-spelling root
    ///     such as <c>uint8[N]</c>), whose count the interpreter's path resolver evaluates before the read.
    /// </summary>
    public const string ResolvedRootArray = "a selected read of a runtime-sized root array resolves its count first (stage 8)";

    /// <summary>
    ///     Decides which implementation runs an operation the engine does not support and records the decision in the
    ///     recording active on the calling flow, if any. Returning means the interpreter runs the operation.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="operation">The kind of operation.</param>
    /// <exception cref="InvalidOperationException">
    ///     <paramref name="selection"/> is <see cref="EngineSelection.EngineRequired"/>; the message names the operation
    ///     and the reason.
    /// </exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Decide(EngineSelection selection, EngineOperation operation)
    {
        // The production case - automatic selection, no recording open - is one comparison and one static read.
        if (selection != EngineSelection.Automatic || EngineDiagnostics.IsRecording)
        {
            DecideAndRecord(selection, operation, OperationNotSupported);
        }
    }

    /// <summary>
    ///     Decides whether the engine reads a whole root and records the decision: the engine runs when the selection
    ///     allows it, the variables are integers, the root's program is eligible, and - for <c>ReadValue</c> - the root is
    ///     not a runtime-sized array. Anything else is declined before a byte is read.
    /// </summary>
    /// <remarks>
    ///     <c>ReadValue</c> of a root first resolves the root as a path in the interpreter; for a root that is an array
    ///     that resolution checks the count against <c>MaxArrayElements</c> before the read does. For a fixed count the
    ///     check is the read's own, with the same failure at the same position; a runtime count is evaluated by the
    ///     resolver's own rules, so that read is declined until stage 8 moves path resolution to the engine.
    /// </remarks>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The root's name, the path's only segment.</param>
    /// <param name="variables">The operation's variable input.</param>
    /// <param name="selectsValue">Whether the operation is <c>ReadValue</c>, which the interpreter resolves as a path before reading.</param>
    /// <returns>The root's program when the engine runs the operation; <see langword="null"/> when the interpreter does.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    public static ReadProgram? SelectRootRead(EngineSelection selection, LayoutCompilation compilation, string rootName, in LayoutVariableInput variables, bool selectsValue)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(EngineOperation.RootRead);
            return null;
        }

        string? reason = DeclineRootRead(compilation, rootName, variables, selectsValue, out ReadProgram? program);
        if (reason is null)
        {
            EngineDiagnostics.Current?.RecordRun(EngineOperation.RootRead);
            return program;
        }

        DecideAndRecord(selection, EngineOperation.RootRead, reason);
        return null;
    }

    /// <summary>Returns why the engine cannot read a whole root, or <see langword="null"/> with the root's program when it can.</summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The root's name.</param>
    /// <param name="variables">The operation's variable input.</param>
    /// <param name="selectsValue">Whether the operation is <c>ReadValue</c>.</param>
    /// <param name="program">The root's program when the engine can read it.</param>
    /// <returns>The decline reason, or <see langword="null"/>.</returns>
    private static string? DeclineRootRead(LayoutCompilation compilation, string rootName, in LayoutVariableInput variables, bool selectsValue, out ReadProgram? program)
    {
        program = null;

        // Expression inputs can leave a supplied expression unevaluated, which makes the operation capture every field
        // (run-time CaptureAll) and may name identifiers without a slot; stage 10 moves them to slots.
        if (!variables.UsesIntegers)
        {
            return ExpressionInputs;
        }

        ReadProgramOutcome outcome = compilation.GetRootReadProgram(rootName);
        if (outcome.Program is not { } root)
        {
            return outcome.Reason;
        }

        if (selectsValue && root.Fields is [{ Array.Kind: CompiledArrayKind.Runtime, },])
        {
            return ResolvedRootArray;
        }

        program = root;
        return null;
    }

    /// <summary>Records a decision the engine cannot take: an interpreter selection, or a decline that fails a required engine.</summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="operation">The kind of operation.</param>
    /// <param name="reason">Why the engine declines it.</param>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DecideAndRecord(EngineSelection selection, EngineOperation operation, string reason)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(operation);
            return;
        }

        EngineDiagnostics.Current?.RecordDecline(operation, reason);
        if (selection == EngineSelection.EngineRequired)
        {
            ThrowRequired(operation, reason);
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
