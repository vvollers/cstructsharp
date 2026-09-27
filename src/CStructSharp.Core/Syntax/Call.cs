namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

/// <summary>Represents a parsed function-style expression call, which the layout language currently rejects at evaluation time.</summary>
internal class Call : Expr
{
    /// <summary>Creates a call expression with the function expression and its parsed arguments.</summary>
    public Call(Expr expr, ImmutableArray<Expr> arguments)
    {
        this.Expr = expr;
        this.Arguments = arguments;
    }

    public ImmutableArray<Expr> Arguments { get; }

    public Expr Expr { get; }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(Expr? other)
    {
        return other is Call c && this.Expr.Equals(c.Expr) && this.Arguments.SequenceEqual(c.Arguments);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
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
    public override string ToString()
    {
        return $"Call: {this.Expr}({string.Join(", ", this.Arguments)})";
    }
}
