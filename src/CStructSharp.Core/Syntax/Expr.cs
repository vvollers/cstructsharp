// ReSharper disable MemberCanBePrivate.Global

namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;

/// <summary>Base class for a number or named calculation used in array lengths, enum values, and defines.</summary>
internal abstract class Expr : IEquatable<Expr>
{
    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The expression to compare with this one.</param>
    /// <returns>True when both expressions have the same structure and values.</returns>
    public abstract bool Equals(Expr? other);

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="obj">The object to compare; anything that is not an <see cref="Expr"/> is unequal.</param>
    /// <returns>True when <paramref name="obj"/> is an equal expression.</returns>
    public override bool Equals(object? obj)
    {
        return this.Equals(obj as Expr);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash consistent with <see cref="Equals(Expr)"/>.</returns>
    public abstract override int GetHashCode();
}
