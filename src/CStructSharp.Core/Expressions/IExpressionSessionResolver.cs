namespace CStructSharp.Expressions;

/// <summary>
///     What an <see cref="ExpressionSession{TKey, TProgram, TResolver}"/> needs from the names it evaluates: the parts of a
///     program, the key an instruction or prelude entry reads, and what a key holds. The dictionary evaluator resolves
///     names through its variables, a slot program resolves slots through the operation's slot array; the session's
///     order of checks and failures is the same for both.
/// </summary>
/// <typeparam name="TKey">What identifies a name: its text, or its slot.</typeparam>
/// <typeparam name="TProgram">The program type.</typeparam>
internal interface IExpressionSessionResolver<TKey, TProgram>
    where TProgram : class
{
    /// <summary>Gets the depth and work limits of the session.</summary>
    ExpressionEvaluationLimits Limits { get; }

    /// <summary>Returns a program's instructions.</summary>
    /// <param name="program">The program.</param>
    /// <returns>The instructions, in execution order.</returns>
    SessionInstruction[] Code(TProgram program);

    /// <summary>Returns a program's deepest syntax level (the root is level 1).</summary>
    /// <param name="program">The program.</param>
    /// <returns>The level.</returns>
    int MaximumDepth(TProgram program);

    /// <summary>Returns the largest number of values a program's stack holds at once.</summary>
    /// <param name="program">The program.</param>
    /// <returns>The stack size, at least 1.</returns>
    int MaximumStackSize(TProgram program);

    /// <summary>Returns a program's validation prelude: the identifiers outside short-circuit and <c>?:</c> arms, in instruction order.</summary>
    /// <param name="program">The program.</param>
    /// <returns>Each entry's reference, as an instruction's <see cref="SessionInstruction.Key"/> holds it, and its level.</returns>
    SessionReference[] Prelude(TProgram program);

    /// <summary>Returns the key a reference of a program names.</summary>
    /// <param name="program">The program.</param>
    /// <param name="reference">An instruction's <see cref="SessionInstruction.Key"/> or a prelude entry's <see cref="SessionReference.Key"/>.</param>
    /// <returns>The key.</returns>
    TKey Key(TProgram program, int reference);

    /// <summary>Returns the name a key is reported under.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The name.</returns>
    string Name(TKey key);

    /// <summary>Returns whether a key holds a value a selected conditional reference validates: neither undefined nor unusable.</summary>
    /// <param name="key">The key.</param>
    /// <returns>Whether the key's program is validated when its arm is selected.</returns>
    bool IsDefined(TKey key);

    /// <summary>
    ///     Returns the program a key's value runs, for validation: a leaf (a literal, in or out of the domain) is a
    ///     one-instruction program at level 1, returned as <see langword="null"/> when the resolver has no program object
    ///     for it.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns>The program, or <see langword="null"/> for a leaf.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">The key is undefined.</exception>
    /// <exception cref="Diagnostics.CStructException">The key holds an unusable value.</exception>
    TProgram? Dependency(TKey key);

    /// <summary>
    ///     Returns what a key's value evaluates as, failing in the session's order: undefined, unusable, then a constant
    ///     beyond the domain.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="leaf">A literal leaf's value when the method returns <see langword="null"/>.</param>
    /// <returns>The program to run, or <see langword="null"/> for a literal leaf.</returns>
    /// <exception cref="System.Collections.Generic.KeyNotFoundException">The key is undefined.</exception>
    /// <exception cref="Diagnostics.CStructException">The key holds an unusable value.</exception>
    /// <exception cref="System.InvalidOperationException">The key holds a constant beyond the domain.</exception>
    TProgram? Evaluation(TKey key, out System.Int128 leaf);
}
