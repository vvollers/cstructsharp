namespace CStructSharp.Tests;

/// <summary>
///     Builds options that choose the implementation of one operation's general path - the interpreter or the compiled
///     engine - to compare the two implementations.
/// </summary>
internal static class EngineSelections
{
    /// <summary>Gets the selection of the reference implementation: the interpreter while a run compares with it, otherwise automatic.</summary>
    private static EngineSelection ReferenceSelection => EngineGolden.ComparesInterpreter ? EngineSelection.InterpreterOnly : EngineSelection.Automatic;

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

    /// <summary>
    ///     Returns <paramref name="options"/> (or the defaults) set to the implementation that produces a differential
    ///     test's inputs and reference values: the interpreter while a run compares with it
    ///     (<see cref="EngineGolden.ComparesInterpreter"/>), otherwise automatic selection, whose results the golden
    ///     outcomes check.
    /// </summary>
    /// <param name="options">The caller's read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with the reference selection.</returns>
    public static ReadOptions Reference(ReadOptions? options = null) => With(ReferenceSelection, options);

    /// <summary>Returns <paramref name="options"/> (or the defaults) set to the reference implementation (see <see cref="Reference(ReadOptions?)"/>).</summary>
    /// <param name="options">The caller's write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A copy with the reference selection.</returns>
    public static WriteOptions Reference(WriteOptions? options) => With(ReferenceSelection, options);

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
