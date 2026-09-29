namespace CStructSharp.Tests;

using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     Decides, for the differential harness, whether automatic selection must run the compiled engine for an
///     operation: the engine reads a whole root or a path (<c>Parse</c>, <c>ParseAsync</c>, <c>ParseMany</c>,
///     <c>ReadValue</c>, <c>ResolveAddress</c>, <c>GetArrayLength</c>) exactly when the read program of the path's root is
///     eligible - the property the eligibility report (<c>ReadProgramEligibility.txt</c>) pins for every corpus root - and
///     writes a whole root to any destination (<c>Serialize</c> to an array, a span or a buffer writer, <c>Write</c> to a
///     stream, and <c>WriteAsync</c>) exactly when the root's write program is eligible (<c>WriteProgramEligibility.txt</c>),
///     and a nested path exactly when the path selects a writable member whose own program is eligible, under plain and
///     update options alike; it runs a debug parse (<c>ParseWithDebug</c>, <c>ReadValueWithDebug</c>) of a root or a path
///     exactly when the root's debug program is, which is exactly when its read program is; every other operation is the
///     interpreter's.
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
    ///     Whether the engine must write the root or nested path <paramref name="path"/> names, to any destination and under
    ///     any write options: for a root, its write program is eligible; for a nested path, the path selects a writable
    ///     member (its shape, before any index is checked against a count) and that member's own program is eligible.
    /// </summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="path">The operation's root name or path.</param>
    /// <returns>Whether the engine must run; <see langword="false"/> for an unparsable path, an unknown root, a path of no writable member, or an ineligible program.</returns>
    public static bool Write(CStruct layout, string path)
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

        LayoutCompilation compilation = layout.Compilation;
        if (!compilation.ModelQueries.TryGetCompiledDeclaration(segments[0].Name, out Syntax.CStructElement? root))
        {
            return false;
        }

        return segments.Count == 1
                   ? compilation.GetRootWriteProgram(segments[0].Name).IsEligible
                   : compilation.GetPathWriteProgram(root, segments.Skip(1).ToArray()).IsEligible;
    }
}
