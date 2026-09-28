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
internal sealed class WideValueVariable : UnusableVariable
{
    /// <summary>Creates the variable for an integer outside the Int32 range.</summary>
    /// <param name="value">The boxed integer as it was decoded or supplied.</param>
    public WideValueVariable(object value)
    {
        this.WideValue = value;
    }

    /// <summary>Gets the boxed integer as it was decoded or supplied.</summary>
    public object WideValue { get; }

    /// <summary>The text for a member whose value an expression selected but which is outside the 32-bit range.</summary>
    /// <param name="name">The variable or member name the expression referenced.</param>
    /// <param name="value">The out-of-range integer, formatted with the invariant culture.</param>
    /// <returns>A message naming the member and its actual value.</returns>
    public static string DescribeOutOfRange(string name, object value)
    {
        string text = value is IFormattable formattable
                          ? formattable.ToString(null, CultureInfo.InvariantCulture)
                          : value.ToString() ?? string.Empty;
        return $"'{name}' is {text}, which is outside the 32-bit range that layout expressions support.";
    }

    /// <inheritdoc/>
    public override InvalidOperationException CreateFailure(string name)
    {
        return new InvalidOperationException(DescribeOutOfRange(name, this.WideValue));
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
