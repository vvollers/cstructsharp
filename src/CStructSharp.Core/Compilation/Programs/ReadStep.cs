namespace CStructSharp.Compilation.Programs;

/// <summary>
///     One step of a <see cref="ReadProgram"/>: an operation and three integer operands. Steps are stored contiguously
///     and executed in order (a <see cref="ReadOpCode.SelectArm"/> step may jump forward); operands index the program's
///     side tables, so a step holds no references and the step array is one block of memory.
/// </summary>
/// <remarks>What each operand means depends on <see cref="Op"/>; <see cref="ReadOpCode"/> lists it per code.</remarks>
internal readonly struct ReadStep
{
    /// <summary>Creates a step.</summary>
    /// <param name="op">The operation.</param>
    /// <param name="field">The member the step belongs to, or -1.</param>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    public ReadStep(ReadOpCode op, int field, int a, int b)
    {
        this.Op = op;
        this.Field = field;
        this.A = a;
        this.B = b;
    }

    /// <summary>Gets the operation.</summary>
    public ReadOpCode Op { get; }

    /// <summary>Gets the member the step belongs to (an index into <see cref="ReadProgram.Fields"/>), or -1 for a step outside any member.</summary>
    public int Field { get; }

    /// <summary>Gets the first operand.</summary>
    public int A { get; }

    /// <summary>Gets the second operand.</summary>
    public int B { get; }
}
