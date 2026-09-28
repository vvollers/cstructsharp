namespace CStructSharp.Syntax;

/// <summary>Lists the supported binary operator type values.</summary>
internal enum BinaryOperatorType
{
    /// <summary><c>&amp;&amp;</c>: 1 when both operands are nonzero, else 0; a left 0 skips the right.</summary>
    LogicalAnd,

    /// <summary><c>||</c>: 1 when either operand is nonzero, else 0; a nonzero left operand skips the right.</summary>
    LogicalOr,

    /// <summary><c>==</c>: 1 when the operands are equal, else 0.</summary>
    Equal,

    /// <summary><c>!=</c>: 1 when the operands differ, else 0.</summary>
    NotEqual,

    /// <summary><c>&lt;</c>: 1 when the left operand is smaller, else 0.</summary>
    Less,

    /// <summary><c>&lt;=</c>: 1 when the left operand is smaller or equal, else 0.</summary>
    LessOrEqual,

    /// <summary><c>&gt;</c>: 1 when the left operand is larger, else 0.</summary>
    Greater,

    /// <summary><c>&gt;=</c>: 1 when the left operand is larger or equal, else 0.</summary>
    GreaterOrEqual,

    /// <summary><c>+</c>: the sum of the operands.</summary>
    Add,

    /// <summary><c>-</c>: the left operand minus the right operand.</summary>
    Minus,

    /// <summary><c>*</c>: the product of the operands.</summary>
    Mul,

    /// <summary><c>/</c>: the integer quotient, truncated toward zero; a zero divisor is an error.</summary>
    Div,

    /// <summary><c>&amp;</c>: the bitwise AND of the operands.</summary>
    And,

    /// <summary><c>|</c>: the bitwise OR of the operands.</summary>
    Or,

    /// <summary><c>&gt;&gt;</c>: the left operand shifted right by the right operand's bit count.</summary>
    ShiftRight,

    /// <summary><c>&lt;&lt;</c>: the left operand shifted left by the right operand's bit count.</summary>
    ShiftLeft,

    /// <summary><c>%</c>: the remainder of truncating division, with the sign of the left operand.</summary>
    Mod,

    /// <summary><c>^</c>: the bitwise exclusive OR of the operands.</summary>
    Xor,
}
