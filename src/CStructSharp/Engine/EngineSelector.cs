namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CStructSharp.Addressing;
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
///         (<see cref="LayoutCompilation.GetRootReadProgram"/>). It runs the path operations (<see cref="SelectPathRead"/>:
///         <c>ReadValue</c> and <c>Parse</c> of a nested path, <c>ResolveAddress</c> and <c>GetArrayLength</c>) when the
///         path's root program is eligible, because every struct, union and pointer target a path can reach is then
///         compiled. It runs the debug parse of a whole root (<see cref="SelectDebugRead"/>: <c>ParseWithDebug</c> and
///         <c>ReadValueWithDebug</c> of a bare root, synchronous and asynchronous) through the root's debug program under the
///         same conditions, and the debug parse of a nested path through <see cref="SelectPathRead"/>. It also runs
///         whole-root writes to memory (<see cref="SelectRootWrite"/>: <c>Serialize</c> to a new array or a span, and
///         <c>WriteAsync</c>, which serializes first) whose root write program is eligible. Every other operation, and one it
///         cannot reproduce, is declined before anything is read or written, and the interpreter runs it
///         (<see cref="Decide"/>).
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

    /// <summary>The reason the engine declines a write to a caller's stream or buffer writer (<c>Write</c>, <c>Serialize(IBufferWriter)</c>).</summary>
    public const string StreamDestinations = "writing to a stream or a buffer writer is not supported yet (stage 9)";

    /// <summary>The reason the engine declines a write of a nested path rather than a whole root.</summary>
    public const string PathWrites = "a write of a nested path is not supported yet (stage 9)";

    /// <summary>The reason the engine declines a write whose options are <see cref="UpdateOptions"/>, which switch on update semantics.</summary>
    public const string UpdateSemantics = "a write with update semantics (UpdateOptions) is not supported yet (stage 10)";

    /// <summary>
    ///     Whether <c>ReadValue</c> of a root field has the path resolver take the root array's count first, by rules of its
    ///     own, before the root is read: a runtime-sized array, a data-sized one (<c>uint16[EOF]</c>; a terminated one is
    ///     scanned, and charged, once more) and a multidimensional one (whose outermost count is checked against the element
    ///     limit, where the read checks the total). A fixed one-dimensional count is checked exactly as the read checks it.
    /// </summary>
    /// <param name="root">The root program's only field.</param>
    /// <returns>Whether the root's count is taken first.</returns>
    public static bool ResolvesCountFirst(CompiledField root)
        => root.Array.Kind is CompiledArrayKind.Runtime or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated || root.Array.Dimensions.Length > 1;

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
    ///     allows it, the variables are integers and the root's program is eligible. Anything else is declined before a
    ///     byte is read.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The root's name, the path's only segment.</param>
    /// <param name="variables">The operation's variable input.</param>
    /// <returns>The root's program when the engine runs the operation; <see langword="null"/> when the interpreter does.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    public static ReadProgram? SelectRootRead(EngineSelection selection, LayoutCompilation compilation, string rootName, in LayoutVariableInput variables)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(EngineOperation.RootRead);
            return null;
        }

        string? reason = DeclineRootRead(compilation, rootName, variables, out ReadProgram? program);
        if (reason is null)
        {
            EngineDiagnostics.Current?.RecordRun(EngineOperation.RootRead);
            return program;
        }

        DecideAndRecord(selection, EngineOperation.RootRead, reason);
        return null;
    }

    /// <summary>
    ///     Decides whether the engine runs an operation on a path - <c>ReadValue</c> or <c>Parse</c> of a nested path (a
    ///     debug parse through the debug programs), <c>ResolveAddress</c> or <c>GetArrayLength</c> of any path - and records
    ///     the decision: the engine runs when the selection allows it, the variables are integers and the program of the
    ///     path's root is eligible, which makes every struct, union and pointer target the path can reach readable. Anything
    ///     else is declined before a byte is read.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The name of the path's root, its first segment.</param>
    /// <param name="variables">The operation's variable input.</param>
    /// <param name="operation">The kind of operation, which the decision is recorded under.</param>
    /// <param name="debug">Whether the operation is a debug parse, which runs the root's debug program.</param>
    /// <returns>The root's program (its debug program for a debug parse) when the engine runs the operation; <see langword="null"/> when the interpreter does.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    public static ReadProgram? SelectPathRead(EngineSelection selection, LayoutCompilation compilation, string rootName, in LayoutVariableInput variables, EngineOperation operation, bool debug = false)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(operation);
            return null;
        }

        // Expression inputs can make the operation capture every field (run-time CaptureAll); stage 10 moves them to slots.
        string reason = ExpressionInputs;
        if (variables.UsesIntegers)
        {
            ReadProgramOutcome outcome = debug ? compilation.GetRootDebugReadProgram(rootName) : compilation.GetRootReadProgram(rootName);
            if (outcome.Program is { } program)
            {
                EngineDiagnostics.Current?.RecordRun(operation);
                return program;
            }

            reason = outcome.Reason!;
        }

        DecideAndRecord(selection, operation, reason);
        return null;
    }

    /// <summary>
    ///     Decides whether the engine runs the debug parse of a whole root (<c>ParseWithDebug</c>, <c>ReadValueWithDebug</c>
    ///     and their asynchronous forms, which the interpreter all runs as a root parse) and records the decision: the engine
    ///     runs when the selection allows it, the variables are integers and the root's debug program is eligible.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The root's name, the path's only segment.</param>
    /// <param name="variables">The operation's variable input.</param>
    /// <returns>The root's debug program when the engine runs the parse; <see langword="null"/> when the interpreter does.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    public static ReadProgram? SelectDebugRead(EngineSelection selection, LayoutCompilation compilation, string rootName, in LayoutVariableInput variables)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(EngineOperation.DebugRead);
            return null;
        }

        // Expression inputs can make the operation capture every field (run-time CaptureAll); stage 10 moves them to slots.
        string reason = ExpressionInputs;
        if (variables.UsesIntegers)
        {
            ReadProgramOutcome outcome = compilation.GetRootDebugReadProgram(rootName);
            if (outcome.Program is { } program)
            {
                EngineDiagnostics.Current?.RecordRun(EngineOperation.DebugRead);
                return program;
            }

            reason = outcome.Reason!;
        }

        DecideAndRecord(selection, EngineOperation.DebugRead, reason);
        return null;
    }

    /// <summary>
    ///     Decides whether the engine writes a whole root and records the decision: the engine runs a <c>Serialize</c> to a
    ///     new array or a caller's span (which <c>WriteAsync</c> serializes through) of a bare root, with integer variables,
    ///     plain write options and an eligible root program. Anything else is declined before a byte is written.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="compilation">The layout.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="variables">The operation's variable input.</param>
    /// <param name="options">The operation's snapshotted options.</param>
    /// <param name="serializes">Whether the destination is a new array or a caller's span rather than a stream or buffer writer.</param>
    /// <returns>The root's write program when the engine runs the operation; <see langword="null"/> when the interpreter does.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    public static WriteProgram? SelectRootWrite(EngineSelection selection, LayoutCompilation compilation, IReadOnlyList<PathSegment> segments, in LayoutVariableInput variables, WriteOptions options, bool serializes)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(EngineOperation.Write);
            return null;
        }

        string? reason = DeclineRootWrite(compilation, segments, variables, options, serializes, out WriteProgram? program);
        if (reason is null)
        {
            EngineDiagnostics.Current?.RecordRun(EngineOperation.Write);
            return program;
        }

        DecideAndRecord(selection, EngineOperation.Write, reason);
        return null;
    }

    /// <summary>Returns why the engine cannot write a whole root, or <see langword="null"/> with the root's program when it can.</summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="variables">The operation's variable input.</param>
    /// <param name="options">The operation's options.</param>
    /// <param name="serializes">Whether the destination is a new array or a caller's span.</param>
    /// <param name="program">The root's program when the engine can write it.</param>
    /// <returns>The decline reason, or <see langword="null"/>.</returns>
    private static string? DeclineRootWrite(LayoutCompilation compilation, IReadOnlyList<PathSegment> segments, in LayoutVariableInput variables, WriteOptions options, bool serializes, out WriteProgram? program)
    {
        program = null;
        if (!serializes)
        {
            return StreamDestinations;
        }

        if (segments.Count != 1 || segments[0].Indexes.Count > 0)
        {
            return PathWrites;
        }

        if (!variables.UsesIntegers)
        {
            return ExpressionInputs;
        }

        if (options is UpdateOptions)
        {
            return UpdateSemantics;
        }

        WriteProgramOutcome outcome = compilation.GetRootWriteProgram(segments[0].Name);
        program = outcome.Program;
        return outcome.Reason;
    }

    /// <summary>Returns why the engine cannot read a whole root, or <see langword="null"/> with the root's program when it can.</summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The root's name.</param>
    /// <param name="variables">The operation's variable input.</param>
    /// <param name="program">The root's program when the engine can read it.</param>
    /// <returns>The decline reason, or <see langword="null"/>.</returns>
    private static string? DeclineRootRead(LayoutCompilation compilation, string rootName, in LayoutVariableInput variables, out ReadProgram? program)
    {
        program = null;

        // Expression inputs can leave a supplied expression unevaluated, which makes the operation capture every field
        // (run-time CaptureAll) and may name identifiers without a slot; stage 10 moves them to slots.
        if (!variables.UsesIntegers)
        {
            return ExpressionInputs;
        }

        ReadProgramOutcome outcome = compilation.GetRootReadProgram(rootName);
        program = outcome.Program;
        return outcome.Reason;
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
