namespace CStructSharp.Compilation.Programs;

/// <summary>
///     What compiling one composite or root for writing produced: a <see cref="WriteProgram"/>, or the reason the compiled
///     engine cannot write it. A composite with a reason makes every struct and root that holds it ineligible, so a
///     write of such a root fails with that reason before anything is written.
/// </summary>
internal sealed class WriteProgramOutcome
{
    /// <summary>Stores either a program or a reason.</summary>
    /// <param name="program">The program, or <see langword="null"/>.</param>
    /// <param name="reason">The reason, or <see langword="null"/>.</param>
    private WriteProgramOutcome(WriteProgram? program, string? reason)
    {
        this.Program = program;
        this.Reason = reason;
    }

    /// <summary>Gets the program, or <see langword="null"/> when the composite cannot be written.</summary>
    public WriteProgram? Program { get; }

    /// <summary>
    ///     Gets why the composite cannot be written, as <c>location: what</c> - the innermost struct and member at fault,
    ///     or the failure a path or root with no writable storage reports; <see langword="null"/> when
    ///     <see cref="Program"/> is set.
    /// </summary>
    public string? Reason { get; }

    /// <summary>Gets a value indicating whether the engine can write the composite or root.</summary>
    public bool IsEligible => this.Program is not null;

    /// <summary>Wraps a compiled program.</summary>
    /// <param name="program">The program.</param>
    /// <returns>An eligible outcome.</returns>
    public static WriteProgramOutcome Eligible(WriteProgram program) => new(program, null);

    /// <summary>Records why a composite or root cannot be written.</summary>
    /// <param name="reason">The reason, as <c>location: what</c>.</param>
    /// <returns>An ineligible outcome.</returns>
    public static WriteProgramOutcome NotSupported(string reason) => new(null, reason);
}
