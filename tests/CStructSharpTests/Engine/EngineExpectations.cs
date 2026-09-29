namespace CStructSharp.Tests;

using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     Decides, for the differential harness, whether automatic selection must run the compiled engine for an
///     operation: the engine reads a whole root (<c>Parse</c>, <c>ParseAsync</c>, <c>ParseMany</c>, <c>ReadValue</c> of a
///     bare root) exactly when the root's read program is eligible - the property the eligibility report
///     (<c>ReadProgramEligibility.txt</c>) pins for every corpus root - and every other operation is the interpreter's.
/// </summary>
internal static class EngineExpectations
{
    /// <summary>
    ///     Whether the engine must read the root <paramref name="path"/> names: the path is one segment, the root's program
    ///     is eligible, and - for <c>ReadValue</c> - the root is not a runtime-sized array, whose count the interpreter's
    ///     path resolver evaluates first.
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
               !(selectsValue && program.Fields is [{ Array.Kind: CompiledArrayKind.Runtime, },]);
    }
}
