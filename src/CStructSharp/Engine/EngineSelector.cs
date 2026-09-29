namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

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
///         same conditions, and the debug parse of a nested path through <see cref="SelectPathRead"/>. It also runs writes
///         (<see cref="SelectWrite"/>: <c>Serialize</c> to a new array, a span or a buffer writer, <c>Write</c> to a stream,
///         and <c>WriteAsync</c>, which serializes first) of a root whose write program is eligible, or of a nested path
///         whose selected member's program is, under plain or update options, and updates (<see cref="SelectUpdate"/>:
///         <c>Update</c> of a stream or a span, <c>UpdateAsync</c>, and the Memory API's patches) whose root is readable and
///         whose selected storage is writable. It declines, before anything is read or written, only what it cannot
///         reproduce - a root or member the programs cannot compile, and a path whose shape selects no writable storage or
///         names no root, which the interpreter reports - and the interpreter runs those.
///     </para>
///     <para>
///         Outside a test recording (<see cref="EngineDiagnostics.Record"/>) a decision records nothing: a root read costs
///         one program lookup in the layout's cache.
///     </para>
/// </remarks>
internal static class EngineSelector
{
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
    ///     Decides whether the engine reads a whole root and records the decision: the engine runs when the selection
    ///     allows it and the root's program is eligible. Anything else is declined before a byte is read.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The root's name, the path's only segment.</param>
    /// <returns>The root's program when the engine runs the operation; <see langword="null"/> when the interpreter does.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    public static ReadProgram? SelectRootRead(EngineSelection selection, LayoutCompilation compilation, string rootName)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(EngineOperation.RootRead);
            return null;
        }

        ReadProgramOutcome outcome = compilation.GetRootReadProgram(rootName);
        string? reason = outcome.Reason;
        ReadProgram? program = outcome.Program;
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
    ///     the decision: the engine runs when the selection allows it and the program of the path's root is eligible, which makes every struct, union and pointer target the path can reach readable. Anything
    ///     else is declined before a byte is read.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The name of the path's root, its first segment.</param>
    /// <param name="operation">The kind of operation, which the decision is recorded under.</param>
    /// <param name="debug">Whether the operation is a debug parse, which runs the root's debug program.</param>
    /// <returns>The root's program (its debug program for a debug parse) when the engine runs the operation; <see langword="null"/> when the interpreter does.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    public static ReadProgram? SelectPathRead(EngineSelection selection, LayoutCompilation compilation, string rootName, EngineOperation operation, bool debug = false)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(operation);
            return null;
        }

        ReadProgramOutcome outcome = debug ? compilation.GetRootDebugReadProgram(rootName) : compilation.GetRootReadProgram(rootName);
        if (outcome.Program is { } program)
        {
            EngineDiagnostics.Current?.RecordRun(operation);
            return program;
        }

        DecideAndRecord(selection, operation, outcome.Reason!);
        return null;
    }

    /// <summary>
    ///     Decides whether the engine runs the debug parse of a whole root (<c>ParseWithDebug</c>, <c>ReadValueWithDebug</c>
    ///     and their asynchronous forms, which the interpreter all runs as a root parse) and records the decision: the engine
    ///     runs when the selection allows it and the root's debug program is eligible.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The root's name, the path's only segment.</param>
    /// <returns>The root's debug program when the engine runs the parse; <see langword="null"/> when the interpreter does.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    public static ReadProgram? SelectDebugRead(EngineSelection selection, LayoutCompilation compilation, string rootName)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(EngineOperation.DebugRead);
            return null;
        }

        ReadProgramOutcome outcome = compilation.GetRootDebugReadProgram(rootName);
        if (outcome.Program is { } program)
        {
            EngineDiagnostics.Current?.RecordRun(EngineOperation.DebugRead);
            return program;
        }

        DecideAndRecord(selection, EngineOperation.DebugRead, outcome.Reason!);
        return null;
    }

    /// <summary>
    ///     Decides whether the engine runs a write and records the decision, for every destination (a new array, a span, a
    ///     stream, a buffer writer) and every write option (update options switch on the same update semantics in both
    ///     implementations): the engine runs when the program is eligible - the root's for a
    ///     whole root (any indexes on its one segment are ignored, as the interpreter ignores them), or the program of the
    ///     member a nested path selects, written on its own. A nested path that selects no writable member is declined, so
    ///     the interpreter reports it. Anything declined is declined before a byte is written.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="compilation">The layout.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="childSegments">The segments after the root for a nested path, or <see langword="null"/>.</param>
    /// <param name="rootElement">The root's declaration.</param>
    /// <returns>The program the engine runs; <see langword="null"/> when the interpreter writes.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    public static WriteProgram? SelectWrite(EngineSelection selection, LayoutCompilation compilation, IReadOnlyList<PathSegment> segments, IReadOnlyList<PathSegment>? childSegments, CStructElement rootElement)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(EngineOperation.Write);
            return null;
        }

        WriteProgramOutcome outcome = childSegments is null
                                          ? compilation.GetRootWriteProgram(segments[0].Name)
                                          : compilation.GetPathWriteProgram(rootElement, childSegments);
        string? reason = outcome.Reason;
        WriteProgram? program = outcome.Program;
        if (reason is null)
        {
            EngineDiagnostics.Current?.RecordRun(EngineOperation.Write);
            return program;
        }

        DecideAndRecord(selection, EngineOperation.Write, reason);
        return null;
    }

    /// <summary>
    ///     Decides whether the engine runs an <c>Update</c> (of a stream, a span, and <c>UpdateAsync</c> and the Memory API's
    ///     patches through it) and records the decision: the engine runs when <see cref="LayoutCompilation.DeclineUpdate"/>
    ///     finds the root readable and what the path selects writable.
    ///     Anything else is declined before anything is read or written.
    /// </summary>
    /// <param name="selection">The operation's snapshotted engine selection.</param>
    /// <param name="compilation">The layout.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="rootElement">The root's declaration.</param>
    /// <returns>Whether the engine runs the update.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the operation.</exception>
    public static bool SelectUpdate(EngineSelection selection, LayoutCompilation compilation, IReadOnlyList<PathSegment> segments, CStructElement rootElement)
    {
        if (selection == EngineSelection.InterpreterOnly)
        {
            EngineDiagnostics.Current?.RecordInterpreterSelection(EngineOperation.Update);
            return false;
        }

        string? reason = compilation.DeclineUpdate(rootElement, segments);
        if (reason is null)
        {
            EngineDiagnostics.Current?.RecordRun(EngineOperation.Update);
            return true;
        }

        DecideAndRecord(selection, EngineOperation.Update, reason);
        return false;
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
