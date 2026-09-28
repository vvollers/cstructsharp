namespace CStructSharp.Syntax;

/// <summary>Lists the supported unary operator type values.</summary>
internal enum UnaryOperatorType
{
    /// <summary>Logical not (<c>!x</c>): 1 when the operand is zero, otherwise 0.</summary>
    LogicalNot,

    /// <summary>Arithmetic negation (<c>-x</c>).</summary>
    Neg,

    /// <summary>Bitwise complement (<c>~x</c>): every bit of the operand inverted.</summary>
    Complement,
}
