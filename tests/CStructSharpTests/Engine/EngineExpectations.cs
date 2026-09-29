namespace CStructSharp.Tests;

using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     Decides, for the differential harness, whether automatic selection must run the compiled engine for an
///     operation: the engine reads a whole root or a path (<c>Parse</c>, <c>ParseAsync</c>, <c>ParseMany</c>,
///     <c>ReadValue</c>, <c>ResolveAddress</c>, <c>GetArrayLength</c>) exactly when the read program of the path's root is
///     eligible - the property the eligibility report (<c>ReadProgramEligibility.txt</c>) pins for every corpus root - and
///     serializes a whole root to a new array or a span (<c>Serialize</c>, and <c>WriteAsync</c> through it) exactly when
///     the root's write program is eligible (<c>WriteProgramEligibility.txt</c>); it runs a debug parse (<c>ParseWithDebug</c>,
///     <c>ReadValueWithDebug</c>) of a root or a path exactly when the root's debug program is, which is exactly when its
///     read program is; every other operation is the interpreter's.
/// </summary>
internal static class EngineExpectations
{
    /// <summary>
    ///     Whether the engine must read the root or path <paramref name="path"/> names, or resolve it or measure it: the path
    ///     parses and the read program of its root is eligible.
    /// </summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="path">The operation's root name or path, or <see langword="null"/> for the first struct or union.</param>
    /// <returns>Whether the engine must run; <see langword="false"/> for an unparsable path, an unknown root, or an ineligible one.</returns>
    public static bool Read(CStruct layout, string? path)
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

        return layout.Compilation.GetRootReadProgram(segments[0].Name).IsEligible;
    }

    /// <summary>
    ///     Whether the engine must run the debug parse of the root or path <paramref name="path"/> names
    ///     (<c>ParseWithDebug</c> and <c>ReadValueWithDebug</c>, which both parse the root or the struct the path selects): the
    ///     path parses and the debug program of its root is eligible.
    /// </summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="path">The operation's root name or path, or <see langword="null"/> for the first struct or union.</param>
    /// <returns>Whether the engine must run; <see langword="false"/> for an unparsable path, an unknown root, or an ineligible one.</returns>
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

        return layout.Compilation.GetRootDebugReadProgram(segments[0].Name).IsEligible;
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
