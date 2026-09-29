namespace CStructSharp.Tests;

/// <summary>
///     Builds options that choose the implementation of one operation's general path - the interpreter or the compiled
///     engine - to compare the two implementations.
/// </summary>
internal static class EngineSelections
{
    /// <summary>Returns <paramref name="options"/> (or the defaults) with the given engine selection.</summary>
    /// <param name="selection">The engine selection.</param>
    /// <param name="options">The caller's read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <paramref name="selection"/>.</returns>
    public static ReadOptions With(EngineSelection selection, ReadOptions? options = null)
        => (options ?? new ReadOptions()) with { EngineSelection = selection, };

    /// <summary>Returns <paramref name="options"/> (or the defaults) with the given engine selection.</summary>
    /// <param name="selection">The engine selection.</param>
    /// <param name="options">The caller's write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <paramref name="selection"/>.</returns>
    public static WriteOptions With(EngineSelection selection, WriteOptions? options)
        => (options ?? new WriteOptions()) with { EngineSelection = selection, };

    /// <summary>Returns <paramref name="options"/> (or the defaults) with the given engine selection.</summary>
    /// <param name="selection">The engine selection.</param>
    /// <param name="options">The caller's update options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <paramref name="selection"/>.</returns>
    public static UpdateOptions With(EngineSelection selection, UpdateOptions? options)
        => (options ?? new UpdateOptions()) with { EngineSelection = selection, };

    /// <summary>Returns <paramref name="options"/> (or the defaults) restricted to the interpreter.</summary>
    /// <param name="options">The caller's read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <see cref="EngineSelection.InterpreterOnly"/>.</returns>
    public static ReadOptions InterpreterOnly(ReadOptions? options = null) => With(EngineSelection.InterpreterOnly, options);

    /// <summary>Returns <paramref name="options"/> (or the defaults) restricted to the interpreter.</summary>
    /// <param name="options">The caller's write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <see cref="EngineSelection.InterpreterOnly"/>.</returns>
    public static WriteOptions InterpreterOnly(WriteOptions? options) => With(EngineSelection.InterpreterOnly, options);

    /// <summary>Returns <paramref name="options"/> (or the defaults) requiring the engine, so a declined operation fails.</summary>
    /// <param name="options">The caller's read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <see cref="EngineSelection.EngineRequired"/>.</returns>
    public static ReadOptions EngineRequired(ReadOptions? options = null) => With(EngineSelection.EngineRequired, options);

    /// <summary>Returns <paramref name="options"/> (or the defaults) requiring the engine, so a declined operation fails.</summary>
    /// <param name="options">The caller's write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <see cref="EngineSelection.EngineRequired"/>.</returns>
    public static WriteOptions EngineRequired(WriteOptions? options) => With(EngineSelection.EngineRequired, options);

    /// <summary>Returns <paramref name="options"/> (or the defaults) requiring the engine, so a declined update fails.</summary>
    /// <param name="options">The caller's update options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with <see cref="EngineSelection.EngineRequired"/>.</returns>
    public static UpdateOptions EngineRequired(UpdateOptions? options) => With(EngineSelection.EngineRequired, options);
}
