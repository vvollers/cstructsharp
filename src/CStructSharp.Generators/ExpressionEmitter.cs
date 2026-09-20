namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     Turns a compiled layout expression into C# over <c>CStructSharp.Generated.Expressions</c>, so every operator
///     keeps the runtime's checked signed-Int32 semantics (plan Appendix E). Identifiers resolve through the scope
///     the emitter is given: earlier members of the composite being read, then defines folded at compile time,
///     then caller variables; a member that holds a wide value is guarded with <c>RequireInt32</c> at its use.
/// </summary>
internal sealed class ExpressionEmitter
{
    private readonly IReadOnlyDictionary<string, Expr> staticValues;
    private readonly IReadOnlyDictionary<string, Defines> definitions;
    private readonly Func<string, string?> resolveMember;

    public ExpressionEmitter(IReadOnlyDictionary<string, Expr> staticValues, IReadOnlyDictionary<string, Defines> definitions, Func<string, string?> resolveMember)
    {
        this.staticValues = staticValues;
        this.definitions = definitions;
        this.resolveMember = resolveMember;
    }

    public string Emit(Expr expression)
    {
        const string E = "global::CStructSharp.Generated.Expressions.";
        switch (expression)
        {
        case Literal literal:
            return IntLiteral(literal);
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

    private static string IntLiteral(Literal literal)
    {
        // The evaluator projects a literal to Int32 and fails on overflow when the value is used; a literal past the
        // range is emitted through RequireInt32 so the same failure surfaces at the same time.
        if (literal.ExactValue >= int.MinValue && literal.ExactValue <= int.MaxValue)
        {
            int value = (int)literal.ExactValue;
            return value < 0 ? "(" + value.ToString(CultureInfo.InvariantCulture) + ")" : value.ToString(CultureInfo.InvariantCulture);
        }

        return "global::CStructSharp.Generated.Expressions.RequireInt32(" + literal.ExactValue.ToString(CultureInfo.InvariantCulture) + "L, \"literal\")";
    }

    private string EmitIdentifier(string name, HashSet<string> resolving)
    {
        string? member = this.resolveMember(name);
        if (member is not null)
        {
            return member;
        }

        if (this.staticValues.TryGetValue(name, out Expr? value))
        {
            if (value is Literal literal)
            {
                return IntLiteral(literal);
            }

            if (value is WideValueVariable wide)
            {
                return "global::CStructSharp.Generated.Expressions.RequireInt32(" + Convert.ToInt64(wide.WideValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L, " + SourceWriter.Literal(name) + ")";
            }
        }

        if (this.definitions.TryGetValue(name, out Defines? definition) && resolving.Add(name))
        {
            // A define that depends on a caller variable is evaluated where it is used, as the runtime does.
            string inner = this.EmitDefinition(definition.Value, resolving);
            resolving.Remove(name);
            return "(" + inner + ")";
        }

        return "global::CStructSharp.Generated.Expressions.Variable(variables, " + SourceWriter.Literal(name) + ")";
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
