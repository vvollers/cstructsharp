namespace CStructSharp.Tests;

/// <summary>Builds options that restrict one operation to the general reader or writer, to compare it with the fast paths.</summary>
internal static class ExecutionPaths
{
    /// <summary>Returns <paramref name="options"/> (or the defaults) restricted to the general per-member reader.</summary>
    /// <param name="options">The caller's read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <see cref="ExecutionPath.GeneralOnly"/>.</returns>
    public static ReadOptions GeneralOnly(ReadOptions? options = null) => (options ?? new ReadOptions()) with { ExecutionPath = ExecutionPath.GeneralOnly };

    /// <summary>Returns <paramref name="options"/> (or the defaults) restricted to the general per-member writer.</summary>
    /// <param name="options">The caller's write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <see cref="ExecutionPath.GeneralOnly"/>.</returns>
    public static WriteOptions GeneralOnly(WriteOptions? options) => (options ?? new WriteOptions()) with { ExecutionPath = ExecutionPath.GeneralOnly };

    /// <summary>Returns default write options restricted to the general per-member writer.</summary>
    /// <returns>Default options with <see cref="ExecutionPath.GeneralOnly"/>.</returns>
    public static WriteOptions GeneralWrite() => GeneralOnly((WriteOptions?)null);
}
