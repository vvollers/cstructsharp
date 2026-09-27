namespace CStructSharp.Expressions;

using System;
using CStructSharp.Syntax;

/// <summary>
///     A layout variable that holds no usable Int32 value: a field was read or written under this name, so an older
///     caller or definition value must not stand in for it, but an expression that selects it fails with a precise
///     message instead of evaluating.
/// </summary>
internal abstract class UnusableVariable : Expr
{
    /// <summary>Creates the diagnostic raised when an expression selects this variable through <paramref name="name"/>.</summary>
    /// <param name="name">The name the expression used.</param>
    /// <returns>The exception to throw.</returns>
    public abstract InvalidOperationException CreateFailure(string name);
}
