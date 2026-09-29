namespace CStructSharp.Compilation.Programs;

/// <summary>
///     What compiling one composite or root produced: a <see cref="ReadProgram"/>, or the reason the compiled engine
///     cannot read it yet. A composite with a reason makes every struct and root that reaches it ineligible, so an
///     operation over such a root is left to the interpreter before anything is read.
/// </summary>
internal sealed class ReadProgramOutcome
{
    /// <summary>Stores either a program or a reason.</summary>
    /// <param name="program">The program, or <see langword="null"/>.</param>
    /// <param name="reason">The reason, or <see langword="null"/>.</param>
    private ReadProgramOutcome(ReadProgram? program, string? reason)
    {
        this.Program = program;
        this.Reason = reason;
    }

    /// <summary>Gets the program, or <see langword="null"/> when the composite is not supported yet.</summary>
    public ReadProgram? Program { get; }

    /// <summary>
    ///     Gets why the composite is not supported yet, as <c>location: what</c> - the innermost struct and member that
    ///     needs a feature a later engine stage adds, such as <c>header.flags: bitfields are not supported yet (stage 4)</c>;
    ///     <see langword="null"/> when <see cref="Program"/> is set.
    /// </summary>
    public string? Reason { get; }

    /// <summary>Gets a value indicating whether the engine can read the composite or root.</summary>
    public bool IsEligible => this.Program is not null;

    /// <summary>Wraps a compiled program.</summary>
    /// <param name="program">The program.</param>
    /// <returns>An eligible outcome.</returns>
    public static ReadProgramOutcome Eligible(ReadProgram program) => new(program, null);

    /// <summary>Records why a composite or root is not supported yet.</summary>
    /// <param name="reason">The reason, as <c>location: what</c>.</param>
    /// <returns>An ineligible outcome.</returns>
    public static ReadProgramOutcome NotSupported(string reason) => new(null, reason);
}
