namespace CStructSharp.Compilation.Programs;

/// <summary>
///     One step of a <see cref="WriteProgram"/>: an operation and three integer operands, stored contiguously and executed
///     in order (a <see cref="WriteOpCode.SelectArm"/> step may jump forward). Operands index the program's side tables,
///     so a step holds no references.
/// </summary>
/// <remarks>What each operand means depends on <see cref="Op"/>; <see cref="WriteOpCode"/> lists it per code.</remarks>
internal readonly struct WriteStep
{
    /// <summary>Creates a step.</summary>
    /// <param name="op">The operation.</param>
    /// <param name="field">The member the step belongs to, or -1.</param>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    public WriteStep(WriteOpCode op, int field, int a, int b)
    {
        this.Op = op;
        this.Field = field;
        this.A = a;
        this.B = b;
    }

    /// <summary>Gets the operation.</summary>
    public WriteOpCode Op { get; }

    /// <summary>Gets the member the step belongs to (an index into <see cref="WriteProgram.Fields"/>), or -1.</summary>
    public int Field { get; }

    /// <summary>Gets the first operand.</summary>
    public int A { get; }

    /// <summary>Gets the second operand.</summary>
    public int B { get; }
}
