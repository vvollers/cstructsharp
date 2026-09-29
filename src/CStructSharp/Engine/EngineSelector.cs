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
///         (<see cref="LayoutCompilation.GetRootReadProgram"/>). Every other operation, and a root read it cannot
///         reproduce, is declined before anything is read, and the interpreter runs it (<see cref="Decide"/>). It also
///         runs whole-root writes to memory (<see cref="SelectRootWrite"/>: <c>Serialize</c> to a new array or a span, and
///         <c>WriteAsync</c>, which serializes first) whose root write program is eligible.
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
    ///     The reason the engine declines <c>ReadValue</c> of a root array whose count the interpreter's path resolver takes
    ///     by its own rules before the read: a runtime-sized one (a type-spelling root such as <c>uint8[N]</c>), a data-sized
    ///     one (<c>uint16[EOF]</c>, whose terminated form the resolver scans, and charges, once more) and a multidimensional
    ///     one (whose outermost count the resolver checks against the element limit, where the read checks the total).
    /// </summary>
    public const string ResolvedRootArray = "a selected read of a root array whose count the path resolver takes first is not supported yet (stage 8)";

    /// <summary>
    ///     Whether <c>ReadValue</c> of a root field would have the interpreter's path resolver take the root array's count
    ///     first, by rules of its own: a runtime-sized, data-sized or multidimensional array. A fixed one-dimensional count is
    ///     checked exactly as the read checks it, so that read is the engine's.
    /// </summary>
    /// <param name="root">The root program's only field.</param>
    /// <returns>Whether the selected read is declined.</returns>
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
    ///     allows it, the variables are integers, the root's program is eligible, and - for <c>ReadValue</c> - the root is
    ///     not an array whose count the path resolver takes first (<see cref="ResolvesCountFirst"/>). Anything else is
    ///     declined before a byte is read.
    /// </summary>
    /// <remarks>
    ///     <c>ReadValue</c> of a root first resolves the root as a path in the interpreter; for a root that is an array
    ///     that resolution checks the count against <c>MaxArrayElements</c> before the read does. For a fixed
    ///     one-dimensional count the check is the read's own, with the same failure at the same position; a runtime,
    ///     data-sized or multidimensional count is taken by the resolver's own rules, so that read is declined until stage 8
    ///     moves path resolution to the engine.
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

        if (selectsValue && root.Fields is [{ } only,] && ResolvesCountFirst(only))
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
