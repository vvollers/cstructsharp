namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     Turns a compiled layout expression into C# over <c>CStructSharp.Generated.Expressions</c>, so every operator
///     keeps the runtime's checked signed-Int32 semantics (plan Appendix E). Identifiers resolve through the scope
///     the emitter is given: earlier members of the composite being read, then caller variables, then defines and
///     enum members folded at compile time (a caller variable overrides a constant of the same name, as at runtime);
///     a member that holds a wide value is guarded with <c>RequireInt32</c> at its use.
/// </summary>
internal sealed class ExpressionEmitter
{
    private readonly IReadOnlyDictionary<string, Expr> staticValues;
    private readonly IReadOnlyDictionary<string, Defines> definitions;
    private readonly Func<string, string?> resolveMember;
    private int overrideCount;

    public ExpressionEmitter(IReadOnlyDictionary<string, Expr> staticValues, IReadOnlyDictionary<string, Defines> definitions, Func<string, string?> resolveMember)
    {
        this.staticValues = staticValues;
        this.definitions = definitions;
        this.resolveMember = resolveMember;
    }

    /// <summary>Whether the expression is a plain literal in the Int32 range, whose evaluation cannot fail.</summary>
    public static bool IsInt32Literal(Expr expression)
        => expression is Literal { ExactValue: var value } && value >= int.MinValue && value <= int.MaxValue;

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
        // The evaluator keeps a constant exact and overflows when an expression selects one past the Int32 range; a
        // literal past the range is emitted as that overflow so the same failure surfaces at the same time.
        if (IsInt32Literal(literal))
        {
            int value = (int)literal.ExactValue;
            return value < 0 ? "(" + value.ToString(CultureInfo.InvariantCulture) + ")" : value.ToString(CultureInfo.InvariantCulture);
        }

        return "global::CStructSharp.Generated.Expressions.Overflow()";
    }

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
            if (value is Literal literal && IsInt32Literal(literal))
            {
                return "global::CStructSharp.Generated.Expressions.Variable(variables, " + literalName + ", " + IntLiteral(literal) + ")";
            }

            if (value is WideValueVariable wide)
            {
                return this.WithOverride(name, "global::CStructSharp.Generated.Expressions.RequireInt32(" + Convert.ToInt64(wide.WideValue, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture) + "L, " + literalName + ")");
            }

            if (value is Literal)
            {
                return this.WithOverride(name, IntLiteral((Literal)value));
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
