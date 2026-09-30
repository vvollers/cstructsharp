namespace CStructSharp.Compilation.Programs;

using CStructSharp.Expressions;

/// <summary>
///     One member of a <see cref="TargetProgram"/> (or a root field): the compiled field with the slot programs and slots
///     the resolver uses while it walks past it or into it.
/// </summary>
/// <remarks>Immutable after construction and shared by every thread, apart from the nested program cached on first use.</remarks>
internal sealed class TargetMember
{
    /// <summary>Creates a member.</summary>
    /// <param name="field">The compiled field.</param>
    /// <param name="branches">The indexes of the conditional branches it sits in, outermost first.</param>
    /// <param name="count">The slot program of a runtime-sized array's count, or <see langword="null"/>.</param>
    /// <param name="captureSlot">The slot its captured value is stored in, or -1 when no expression can read it.</param>
    /// <param name="qualifiedTargets">The slots a capture is also published to under each qualified prefix.</param>
    /// <param name="promoted">Whether it is an anonymous struct or union whose members are addressed as the composite's own.</param>
    internal TargetMember(CompiledField field, int[] branches, ProgramExpression? count, int captureSlot, QualifiedTarget[] qualifiedTargets, bool promoted)
    {
        this.Field = field;
        this.Branches = branches;
        this.Count = count;
        this.CountContext = "array length for " + field.Name;
        this.CaptureSlot = captureSlot;
        this.NotANumber = captureSlot >= 0 && field.NotANumberReason is { } reason ? new NotANumberVariable(reason) : null;
        this.QualifiedTargets = qualifiedTargets;
        this.Promoted = promoted;
    }

    /// <summary>Gets the compiled field.</summary>
    public CompiledField Field { get; }

    /// <summary>Gets the indexes (into <see cref="TargetProgram.Branches"/>) of the arms the member sits in, outermost first; empty for an unconditional member.</summary>
    public int[] Branches { get; }

    /// <summary>Gets the slot program of a runtime-sized array's count (<see cref="CompiledArrayKind.Runtime"/>), or <see langword="null"/>.</summary>
    public ProgramExpression? Count { get; }

    /// <summary>Gets what a count failure names (<c>array length for n</c>).</summary>
    public string CountContext { get; }

    /// <summary>Gets the slot a capture of the member stores its value in, or -1 when no expression can read the member's name.</summary>
    public int CaptureSlot { get; }

    /// <summary>
    ///     Gets the unusable value a capture stores for a member that is not an integer (text, an array, a struct, a
    ///     floating-point value...), or <see langword="null"/> for an integer member or one that captures nothing.
    /// </summary>
    public UnusableVariable? NotANumber { get; }

    /// <summary>Gets the slots a capture is also published to while a qualified prefix is active; empty when no expression spells the name with a prefix.</summary>
    public QualifiedTarget[] QualifiedTargets { get; }

    /// <summary>Gets a value indicating whether the member is an anonymous struct or union whose own members belong to the composite's namespace.</summary>
    public bool Promoted { get; }

    /// <summary>Gets or sets the walk of the member's own struct or union, cached on first use (every thread sets the same program).</summary>
    public TargetProgram? Nested { get; set; }
}
