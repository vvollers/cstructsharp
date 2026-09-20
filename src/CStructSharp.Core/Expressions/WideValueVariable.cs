namespace CStructSharp.Expressions;

using System;
using System.Collections.Generic;
using System.Globalization;
using CStructSharp.Syntax;

/// <summary>
///     A layout variable whose decoded or supplied integer lies outside the Int32 expression domain. It keeps the
///     original boxed value (no arithmetic representation is needed) so that an expression selecting it can fail with
///     the actual number, and costs one small object per capture instead of a <see cref="System.Numerics.BigInteger"/>.
/// </summary>
internal sealed class WideValueVariable : Expr
{
    public WideValueVariable(object value)
    {
        this.WideValue = value;
    }

    /// <summary>Gets the boxed integer as it was decoded or supplied.</summary>
    public object WideValue { get; }

    /// <inheritdoc/>
    public override int Value => throw this.CreateFailure("a value");

    /// <inheritdoc/>
    public override int Calc(Dictionary<string, Expr> variables)
    {
        throw this.CreateFailure("a value");
    }

    /// <summary>Creates the diagnostic raised when an expression selects this variable through <paramref name="name"/>.</summary>
    public InvalidOperationException CreateFailure(string name)
    {
        string text = this.WideValue is IFormattable formattable
                          ? formattable.ToString(null, CultureInfo.InvariantCulture)
                          : this.WideValue.ToString() ?? string.Empty;
        return new InvalidOperationException(
            $"'{name}' is {text}, which is outside the 32-bit range that layout expressions support.");
    }

    /// <inheritdoc/>
    public override bool Equals(Expr? other)
    {
        return other is WideValueVariable wide && Equals(wide.WideValue, this.WideValue);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return this.WideValue.GetHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"Wide: {this.WideValue}";
    }
}
