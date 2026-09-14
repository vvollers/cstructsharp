// ReSharper disable MemberCanBePrivate.Global

namespace CStructSharp.Structure;

using System;
using System.Collections.Generic;

/// <summary>Base class for a number or named calculation used in array lengths, enum values, and defines.</summary>
internal abstract class Expr : IEquatable<Expr>
{
    private static readonly Dictionary<string, Expr> EmptyVariables = new(StringComparer.Ordinal);

    public abstract int Value { get; }

    /// <summary>Calculates this expression without caller-supplied names.</summary>
    public abstract int Calc(Dictionary<string, Expr> variables);

    /// <summary>Calculates the value represented by this expression.</summary>
    public int Calc()
    {
        return this.Calc(EmptyVariables);
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public abstract bool Equals(Expr? other);

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(object? obj)
    {
        return this.Equals(obj as Expr);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public abstract override int GetHashCode();
}
