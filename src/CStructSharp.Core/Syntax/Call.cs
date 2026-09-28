namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

/// <summary>
///     Represents a parsed function-style call. Only <c>sizeof(type)</c> and <c>offsetof(type, field)</c> are
///     accepted; they fold to literals when the layout is constructed, and any other call is rejected.
/// </summary>
internal class Call : Expr
{
    /// <summary>Creates a call expression with the function expression and its parsed arguments.</summary>
    /// <param name="expr">The expression before the parentheses, normally the function name.</param>
    /// <param name="arguments">The argument expressions, in source order.</param>
    public Call(Expr expr, ImmutableArray<Expr> arguments)
    {
        this.Expr = expr;
        this.Arguments = arguments;
    }

    /// <summary>Gets the argument expressions, in source order.</summary>
    public ImmutableArray<Expr> Arguments { get; }

    /// <summary>Gets the expression that names the called function.</summary>
    public Expr Expr { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The expression to compare with.</param>
    /// <returns>True when <paramref name="other"/> is a call with an equal function and equal arguments.</returns>
    public override bool Equals(Expr? other)
    {
        return other is Call c && this.Expr.Equals(c.Expr) && this.Arguments.SequenceEqual(c.Arguments);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash combining the function expression and every argument.</returns>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(this.Expr);
        foreach (Expr argument in this.Arguments)
        {
            hash.Add(argument);
        }

        return hash.ToHashCode();
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The text <c>Call: f(a, b)</c> built from the function and its arguments.</returns>
    public override string ToString()
    {
        return $"Call: {this.Expr}({string.Join(", ", this.Arguments)})";
    }
}
