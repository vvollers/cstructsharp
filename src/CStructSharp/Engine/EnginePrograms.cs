namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Syntax;

/// <summary>
///     Looks up, once per public operation, the compiled program the operation runs, and counts the operation in an open
///     test recording (<see cref="EngineDiagnostics"/>). Every public operation that is not served by a fast path in
///     front of the engine (the direct fixed-root reads and writes) asks here exactly once, after its arguments are
///     checked and before anything is read or written.
/// </summary>
/// <remarks>
///     <para>
///         A program is compiled on first request and cached with the layout (<see cref="LayoutCompilation.SlotTable"/>),
///         so a repeated operation costs one cache lookup. A layout the parser accepts always compiles: a declaration that
///         stores no value reads nothing, and a member whose type has no codec compiles to a step that fails the operation
///         when it is reached (<see cref="ReadOpCode.FailNoReader"/>, <see cref="WriteOpCode.FailNoWriter"/>).
///     </para>
///     <para>
///         Two lookups can find no program, and the operation reports each at the point its contract names: a root the
///         layout does not declare (a read reports it after the caller's variables are resolved, a path operation while
///         it resolves the path), and a write of a path that selects no writable member or of a declaration with no
///         binary storage (the write reports it after the value is normalized, <see cref="WriteEngine"/>).
///     </para>
/// </remarks>
internal static class EnginePrograms
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
    ///     Returns the program of a whole-root read - <c>Parse</c>, <c>ParseAsync</c>, each record of <c>ParseMany</c>, and
    ///     <c>ReadValue</c> of a bare root, from every source - and counts the operation.
    /// </summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The root's name, the path's only segment.</param>
    /// <returns>The root's program, or <see langword="null"/> when the layout declares no such root.</returns>
    public static ReadProgram? RootRead(LayoutCompilation compilation, string rootName)
    {
        EngineDiagnostics.Current?.RecordRun(EngineOperation.RootRead);
        return Require(compilation, rootName, compilation.GetRootReadProgram(rootName));
    }

    /// <summary>
    ///     Returns the debug program of a whole-root debug parse - <c>ParseWithDebug</c> and <c>ReadValueWithDebug</c> of a
    ///     bare root, synchronous and asynchronous - which records every value's byte range, and counts the operation.
    /// </summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The root's name, the path's only segment.</param>
    /// <returns>The root's debug program, or <see langword="null"/> when the layout declares no such root.</returns>
    public static ReadProgram? DebugRead(LayoutCompilation compilation, string rootName)
    {
        EngineDiagnostics.Current?.RecordRun(EngineOperation.DebugRead);
        return Require(compilation, rootName, compilation.GetRootDebugReadProgram(rootName));
    }

    /// <summary>
    ///     Counts an operation on a path - <c>ReadValue</c>, <c>Parse</c> or a debug parse of a nested path,
    ///     <c>ResolveAddress</c> or <c>GetArrayLength</c> of any path - whose programs the path resolver looks up member by
    ///     member as it walks (<see cref="TargetResolver"/>).
    /// </summary>
    /// <param name="operation">The kind of operation.</param>
    public static void PathOperation(EngineOperation operation) => EngineDiagnostics.Current?.RecordRun(operation);

    /// <summary>
    ///     Returns the program of a write - <c>Serialize</c> to a new array, a span or a buffer writer, <c>Write</c> to a
    ///     stream, and <c>WriteAsync</c>, under plain or update options - and counts the operation: the root's program for a
    ///     whole root (any indexes on its one segment are ignored), or the program of the member a nested path selects,
    ///     written on its own.
    /// </summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="childSegments">The segments after the root for a nested path, or <see langword="null"/>.</param>
    /// <param name="rootElement">The root's declaration.</param>
    /// <returns>
    ///     The program, or - for a path that selects no writable member, or a root with no binary storage - no program and
    ///     the failure the write reports (<see cref="WriteProgramOutcome.Reason"/>).
    /// </returns>
    public static WriteProgramOutcome Write(LayoutCompilation compilation, IReadOnlyList<PathSegment> segments, IReadOnlyList<PathSegment>? childSegments, CStructElement rootElement)
    {
        EngineDiagnostics.Current?.RecordRun(EngineOperation.Write);
        return childSegments is null
                   ? compilation.GetRootWriteProgram(segments[0].Name)
                   : compilation.GetPathWriteProgram(rootElement, childSegments);
    }

    /// <summary>
    ///     Counts an <c>Update</c> - of a stream or a span, <c>UpdateAsync</c>, and the Memory API's patches through it -
    ///     whose programs the update looks up as it resolves its path (<c>CStruct.UpdateWithEngine</c>).
    /// </summary>
    public static void Update() => EngineDiagnostics.Current?.RecordRun(EngineOperation.Update);

    /// <summary>
    ///     Returns a root's read program, or <see langword="null"/> for a root the layout does not declare, which the
    ///     operation reports where its contract names.
    /// </summary>
    /// <param name="compilation">The layout.</param>
    /// <param name="rootName">The root's name.</param>
    /// <param name="outcome">The program lookup.</param>
    /// <returns>The program, or <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">The layout declares the root, but the engine cannot read it.</exception>
    private static ReadProgram? Require(LayoutCompilation compilation, string rootName, ReadProgramOutcome outcome)
    {
        if (outcome.Program is { } program)
        {
            return program;
        }

        return compilation.ModelQueries.TryGetCompiledDeclaration(rootName, out _)
                   ? throw new InvalidOperationException("The compiled engine cannot read " + outcome.Reason + ".")
                   : null;
    }
}
