namespace CStructSharp.Tests;

/// <summary>
///     Builds options that restrict one operation to the compiled engine's member-by-member work (no direct access,
///     static plans or block paths), to compare it with the fast paths.
/// </summary>
internal static class ExecutionPaths
{
    /// <summary>Returns <paramref name="options"/> (or the defaults) restricted to member-by-member reads.</summary>
    /// <param name="options">The caller's read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <see cref="ExecutionPath.NoFastPaths"/>.</returns>
    public static ReadOptions NoFastPaths(ReadOptions? options = null) => (options ?? new ReadOptions()) with { ExecutionPath = ExecutionPath.NoFastPaths };

    /// <summary>Returns <paramref name="options"/> (or the defaults) restricted to member-by-member writes.</summary>
    /// <param name="options">The caller's write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <see cref="ExecutionPath.NoFastPaths"/>.</returns>
    public static WriteOptions NoFastPaths(WriteOptions? options) => (options ?? new WriteOptions()) with { ExecutionPath = ExecutionPath.NoFastPaths };

    /// <summary>Returns default write options restricted to member-by-member writes.</summary>
    /// <returns>Default options with <see cref="ExecutionPath.NoFastPaths"/>.</returns>
    public static WriteOptions NoFastPathsWrite() => NoFastPaths((WriteOptions?)null);
}
