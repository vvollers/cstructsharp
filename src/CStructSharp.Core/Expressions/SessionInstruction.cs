namespace CStructSharp.Expressions;

using System;
using ExpressionOpcode = CStructSharp.Expressions.ExpressionEvaluator.ExpressionOpcode;

/// <summary>
///     One instruction of a program an <see cref="ExpressionSession{TKey, TProgram, TResolver}"/> runs: the compiler's
///     instruction with its jump target resolved and its identifier resolved to a key - an index into the program's
///     names for the dictionary evaluator, a slot for a slot program.
/// </summary>
/// <remarks>
///     An identifier's key and a jump's target share <see cref="Operand"/>, since no instruction has both; that keeps the
///     instruction at 48 bytes (its <see cref="Int128"/> is 16-byte aligned), the size of the compiler's own instruction.
/// </remarks>
/// <param name="Opcode">The operation.</param>
/// <param name="Value">The constant a literal pushes.</param>
/// <param name="Operand">The key an identifier reads (<see cref="Key"/>), the instruction a jump continues at (<see cref="Target"/>), otherwise 0.</param>
/// <param name="Depth">The syntax level of the node the instruction came from (the root is level 1).</param>
/// <param name="Conditional">Whether an identifier sits in a short-circuit or <c>?:</c> arm, so it is validated only when selected.</param>
/// <param name="Text">The failure message of an out-of-domain literal, otherwise <see langword="null"/>.</param>
internal readonly record struct SessionInstruction(
    ExpressionOpcode Opcode,
    Int128 Value,
    int Operand,
    int Depth,
    bool Conditional,
    string? Text)
{
    /// <summary>Gets the key an identifier reads.</summary>
    public int Key => this.Operand;

    /// <summary>Gets the instruction a jump continues at.</summary>
    public int Target => this.Operand;
}
