namespace CStructSharp.Tests;

using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     Decides, for the differential harness, whether automatic selection must run the compiled engine for an
///     operation: the engine reads a whole root (<c>Parse</c>, <c>ParseAsync</c>, <c>ParseMany</c>, <c>ReadValue</c> of a
///     bare root) exactly when the root's read program is eligible - the property the eligibility report
///     (<c>ReadProgramEligibility.txt</c>) pins for every corpus root - and serializes a whole root to a new array or a
///     span (<c>Serialize</c>, and <c>WriteAsync</c> through it) exactly when the root's write program is eligible
///     (<c>WriteProgramEligibility.txt</c>); it runs the debug parse of a whole root (<c>ParseWithDebug</c>,
///     <c>ReadValueWithDebug</c>) exactly when the root's debug program is, which is exactly when its read program is;
///     every other operation is the interpreter's.
/// </summary>
internal static class EngineExpectations
{
    /// <summary>
    ///     Whether the engine must read the root <paramref name="path"/> names: the path is one segment, the root's program
    ///     is eligible, and - for <c>ReadValue</c> - the root is not an array whose count the interpreter's path resolver
    ///     takes first by its own rules: a runtime-sized, data-sized (<c>[EOF]</c>, terminated) or multidimensional one.
    /// </summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="path">The operation's root name or path, or <see langword="null"/> for the first struct or union.</param>
    /// <param name="selectsValue">Whether the operation is <c>ReadValue</c> rather than a parse.</param>
    /// <returns>Whether the engine must run; <see langword="false"/> for a nested path, an unknown root, or an ineligible one.</returns>
    public static bool RootRead(CStruct layout, string? path, bool selectsValue)
    {
        IReadOnlyList<PathSegment> segments;
        try
        {
            segments = layout.ParsePath(path ?? layout.Compilation.ModelQueries.GetFirstCompiledStructName());
        }
        catch (CStructException)
        {
            return false;
        }

        if (segments.Count != 1)
        {
            return false;
        }

        return layout.Compilation.GetRootReadProgram(segments[0].Name).Program is { } program &&
               !(selectsValue && program.Fields is [{ } root,] &&
                 (root.Array.Kind is CompiledArrayKind.Runtime or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated || root.Array.Dimensions.Length > 1));
    }

    /// <summary>
    ///     Whether the engine must run the debug parse of the root <paramref name="path"/> names (<c>ParseWithDebug</c> and
    ///     <c>ReadValueWithDebug</c>, which the interpreter both runs as a root parse): the path is one segment and the root's
    ///     debug program is eligible.
    /// </summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="path">The operation's root name or path, or <see langword="null"/> for the first struct or union.</param>
    /// <returns>Whether the engine must run; <see langword="false"/> for a nested path, an unknown root, or an ineligible one.</returns>
    public static bool DebugRead(CStruct layout, string? path)
    {
        IReadOnlyList<PathSegment> segments;
        try
        {
            segments = layout.ParsePath(path ?? layout.Compilation.ModelQueries.GetFirstCompiledStructName());
        }
        catch (CStructException)
        {
            return false;
        }

        return segments.Count == 1 && layout.Compilation.GetRootDebugReadProgram(segments[0].Name).IsEligible;
    }

    /// <summary>
    ///     Whether the engine must serialize the root <paramref name="path"/> names: the path is one segment without an
    ///     index, the options are not update options, and the root's write program is eligible.
    /// </summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="path">The operation's root name or path.</param>
    /// <param name="options">The case's write options, or <see langword="null"/>.</param>
    /// <returns>Whether the engine must run; <see langword="false"/> for a nested path, an unknown root, or an ineligible one.</returns>
    public static bool RootWrite(CStruct layout, string path, WriteOptions? options)
    {
        IReadOnlyList<PathSegment> segments;
        try
        {
            segments = layout.ParsePath(path);
        }
        catch (CStructException)
        {
            return false;
        }

        return segments is [{ Indexes.Count: 0, } root,] && options is not UpdateOptions && layout.Compilation.GetRootWriteProgram(root.Name).IsEligible;
    }
}
