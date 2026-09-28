namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     Turns a compiled layout expression into C# over <c>CStructSharp.Generated.Expressions</c>, so every operator
///     keeps the runtime's checked signed 128-bit semantics: the emitted expression has the type
///     <see cref="System.Int128"/> (or <see cref="int"/>, which widens into it). Identifiers resolve through the scope
///     the emitter is given: earlier members of the composite being read, then caller variables, then defines and
///     enum members folded at compile time (a caller variable overrides a constant of the same name, as at runtime).
///     A literal is emitted with its exact value; one outside the 128-bit range fails where it is evaluated, as at
///     runtime.
/// </summary>
internal sealed class ExpressionEmitter
{
    private readonly IReadOnlyDictionary<string, Expr> staticValues;
    private readonly IReadOnlyDictionary<string, Defines> definitions;
    private readonly Func<string, string?> resolveMember;
    private int overrideCount;

    /// <summary>Creates an emitter over one layout's constants and one reader's visible members.</summary>
    /// <param name="staticValues">The defines and enum members the layout compilation folded, by name.</param>
    /// <param name="definitions">The layout's <c>#define</c> declarations, for defines that use other names.</param>
    /// <param name="resolveMember">
    ///     Maps a name to the C# expression (of type <see cref="System.Int128"/>) of a member already read, or returns
    ///     null when no member is visible under that name.
    /// </param>
    public ExpressionEmitter(IReadOnlyDictionary<string, Expr> staticValues, IReadOnlyDictionary<string, Defines> definitions, Func<string, string?> resolveMember)
    {
        this.staticValues = staticValues;
        this.definitions = definitions;
        this.resolveMember = resolveMember;
    }

    /// <summary>Whether the expression is a plain literal in the Int32 range, whose evaluation cannot fail and whose value an <c>int</c> holds.</summary>
    /// <param name="expression">The compiled layout expression to inspect.</param>
    /// <returns><see langword="true"/> only for a <see cref="Literal"/> whose exact value fits an Int32.</returns>
    public static bool IsInt32Literal(Expr expression) => expression is Literal literal && literal.TryGetInt32(out _);

    /// <summary>Emits a C# expression in the 128-bit domain that evaluates <paramref name="expression"/> at run time.</summary>
    /// <param name="expression">The compiled layout expression to translate.</param>
    /// <returns>
    ///     C# source that reads the caller's <c>variables</c> dictionary where needed and throws as the runtime
    ///     evaluator does on overflow or division by zero.
    /// </returns>
    /// <exception cref="InvalidOperationException">The expression contains an unsupported node kind.</exception>
    public string Emit(Expr expression)
    {
        const string E = "global::CStructSharp.Generated.Expressions.";
        switch (expression)
        {
        case Literal literal:
            return DomainLiteral(literal);
        case Identifier identifier:
            return this.EmitIdentifier(identifier.Name, new HashSet<string>(StringComparer.Ordinal));
        case UnaryOp unary:
            string unaryName = unary.Type switch
            {
                UnaryOperatorType.LogicalNot => "LogicalNot",
                UnaryOperatorType.Neg => "Negate",
                _ => "Complement",
            };
            return E + unaryName + "(" + this.Emit(unary.Expr) + ")";
        case BinaryOp binary:
            if (binary.Type is BinaryOperatorType.LogicalAnd or BinaryOperatorType.LogicalOr)
            {
                // Short-circuit like the evaluator: the right operand is not evaluated when the left decides.
                string left = this.Emit(binary.Left);
                string right = this.Emit(binary.Right);
                return binary.Type == BinaryOperatorType.LogicalAnd
                           ? "((" + left + ") != 0 && (" + right + ") != 0 ? 1 : 0)"
                           : "((" + left + ") != 0 || (" + right + ") != 0 ? 1 : 0)";
            }

            return E + BinaryName(binary.Type) + "(" + this.Emit(binary.Left) + ", " + this.Emit(binary.Right) + ")";
        case ConditionalExpr conditional:
            return "((" + this.Emit(conditional.Condition) + ") != 0 ? (" + this.Emit(conditional.WhenTrue) + ") : (" + this.Emit(conditional.WhenFalse) + "))";
        default:
            throw new InvalidOperationException("Unsupported expression node: " + expression.GetType().Name);
        }
    }

    private static string BinaryName(BinaryOperatorType type)
    {
        return type switch
        {
            BinaryOperatorType.Equal => "Equal",
            BinaryOperatorType.NotEqual => "NotEqual",
            BinaryOperatorType.Less => "Less",
            BinaryOperatorType.LessOrEqual => "LessOrEqual",
            BinaryOperatorType.Greater => "Greater",
            BinaryOperatorType.GreaterOrEqual => "GreaterOrEqual",
            BinaryOperatorType.Add => "Add",
            BinaryOperatorType.Minus => "Subtract",
            BinaryOperatorType.Mul => "Multiply",
            BinaryOperatorType.Div => "Divide",
            BinaryOperatorType.And => "And",
            BinaryOperatorType.Or => "Or",
            BinaryOperatorType.ShiftRight => "ShiftRight",
            BinaryOperatorType.ShiftLeft => "ShiftLeft",
            BinaryOperatorType.Mod => "Modulo",
            _ => "Xor",
        };
    }

    /// <summary>
    ///     A literal's exact value as C#: an <c>int</c> literal when it fits one, a <c>long</c> or <c>ulong</c> literal
    ///     cast to <see cref="System.Int128"/> up to 64 bits, and the two-halves constructor beyond. A literal outside the
    ///     128-bit range fails where it is evaluated, as the runtime's compiled program does.
    /// </summary>
    /// <param name="literal">The literal.</param>
    /// <returns>The C# expression.</returns>
    public static string DomainLiteral(Literal literal)
    {
        if (!literal.IsInDomain)
        {
            return "global::CStructSharp.Generated.Expressions.OutOfRangeLiteral(" + SourceWriter.Literal(Digits(literal)) + ")";
        }

        return DomainConstant(literal.ExactValue);
    }

    /// <summary>A value inside the 128-bit domain as a C# constant expression.</summary>
    /// <param name="value">The value, which must lie in the signed 128-bit range.</param>
    /// <returns>The C# expression.</returns>
    public static string DomainConstant(BigInteger value)
    {
        string digits = value.ToString(CultureInfo.InvariantCulture);
        if (value >= int.MinValue && value <= int.MaxValue)
        {
            return value.Sign < 0 ? "(" + digits + ")" : digits;
        }

        if (value >= long.MinValue && value <= ulong.MaxValue)
        {
            string suffix = value > long.MaxValue ? "UL" : "L";
            return "((global::System.Int128)(" + digits + suffix + "))";
        }

        // Beyond 64 bits C# has no literal: the constructor takes the upper and lower halves of the two's complement.
        BigInteger bits = value.Sign < 0 ? value + (BigInteger.One << 128) : value;
        ulong upper = (ulong)(bits >> 64);
        ulong lower = (ulong)(bits & ulong.MaxValue);
        return "new global::System.Int128(0x" + upper.ToString("X", CultureInfo.InvariantCulture) + "UL, 0x" + lower.ToString("X", CultureInfo.InvariantCulture) + "UL)";
    }

    /// <summary>A literal's exact value in invariant decimal digits, for a failure message.</summary>
    private static string Digits(Literal literal) => literal.ExactValue.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    ///     Emits a name as the runtime resolves it: a member already read, then (for any other name) the caller's
    ///     variable, falling back to the layout's constant or definition, or failing as undefined.
    /// </summary>
    /// <param name="name">The identifier.</param>
    /// <param name="resolving">The definitions being expanded, which stops a cyclic definition from recursing.</param>
    /// <returns>The C# expression in the 128-bit domain.</returns>
    private string EmitIdentifier(string name, HashSet<string> resolving)
    {
        string? member = this.resolveMember(name);
        if (member is not null)
        {
            return member;
        }

        // A caller variable overrides the layout's constant of the same name (the runtime copies supplied variables
        // over its definitions), so every non-member name checks the caller's dictionary first.
        string literalName = SourceWriter.Literal(name);
        bool dependentDefine = this.definitions.TryGetValue(name, out Defines? definition) && definition.Value is not Literal;
        if (!dependentDefine && this.staticValues.TryGetValue(name, out Expr? value))
        {
            if (value is Literal { IsInDomain: true, } literal)
            {
                return "global::CStructSharp.Generated.Expressions.Variable(variables, " + literalName + ", " + DomainLiteral(literal) + ")";
            }

            if (value is Literal wideLiteral)
            {
                // The runtime keeps the exact value and fails, naming the constant, when an expression selects it.
                return this.WithOverride(name, "global::CStructSharp.Generated.Expressions.OutOfRangeConstant(" + literalName + ", " + SourceWriter.Literal(Digits(wideLiteral)) + ")");
            }

            if (value is WideValueVariable wide)
            {
                string digits = wide.WideValue is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : wide.WideValue.ToString() ?? string.Empty;
                return this.WithOverride(name, "global::CStructSharp.Generated.Expressions.OutOfRangeConstant(" + literalName + ", " + SourceWriter.Literal(digits) + ")");
            }

            if (value is Identifier or UnaryOp or BinaryOp or ConditionalExpr)
            {
                return this.WithOverride(name, this.Emit(value));
            }
        }

        if (definition is not null && resolving.Add(name))
        {
            // A define with dependencies is evaluated where it is used: a caller variable may override a name it
            // depends on, which at runtime invalidates the folded value and re-evaluates the definition.
            string inner = this.EmitDefinition(definition.Value, resolving);
            resolving.Remove(name);
            return this.WithOverride(name, inner);
        }

        return "global::CStructSharp.Generated.Expressions.Variable(variables, " + literalName + ")";
    }

    /// <summary>The caller's value of <paramref name="name"/> when supplied; otherwise <paramref name="fallback"/>, evaluated only then.</summary>
    private string WithOverride(string name, string fallback)
    {
        string local = "supplied" + (this.overrideCount++).ToString(CultureInfo.InvariantCulture);
        return "(global::CStructSharp.Generated.Expressions.TryVariable(variables, " + SourceWriter.Literal(name) + ", out int " + local + ") ? " + local + " : (" + fallback + "))";
    }

    private string EmitDefinition(Expr expression, HashSet<string> resolving)
    {
        return expression switch
        {
            Identifier identifier => this.EmitIdentifier(identifier.Name, resolving),
            _ => this.Emit(expression),
        };
    }
}
