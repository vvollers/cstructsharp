namespace CStructSharp.Expressions;

using System;
using CStructSharp.Syntax;

/// <summary>
///     The layout variable of a field whose value is not an integer (text, an array, a struct, a floating-point
///     value, ...). Layout construction rejects an expression that can only name such fields; this state covers a
///     name that a numeric field shares, so an expression that meets the non-numeric field fails instead of reading
///     an older value.
/// </summary>
internal sealed class NotANumberVariable : UnusableVariable
{
    /// <summary>Creates the variable for a field of the described kind.</summary>
    /// <param name="reason">What the field holds, as the phrase after "is" (for example <c>text</c> or <c>an array</c>).</param>
    public NotANumberVariable(string reason)
    {
        this.Reason = reason;
    }

    /// <summary>Gets what the field holds, as the phrase after "is".</summary>
    public string Reason { get; }

    /// <summary>The text for a field an expression names although its value is not an integer.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="reason">What the field holds, as the phrase after "is".</param>
    /// <returns>The diagnostic text.</returns>
    public static string Describe(string name, string reason)
        => $"'{name}' is {reason}, but layout expressions can only use integer fields (integers, characters, bool, enums and pointers).";

    /// <inheritdoc/>
    public override InvalidOperationException CreateFailure(string name) => new(Describe(name, this.Reason));

    /// <inheritdoc/>
    public override bool Equals(Expr? other) => other is NotANumberVariable variable && variable.Reason == this.Reason;

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Reason);

    /// <inheritdoc/>
    public override string ToString() => $"NotANumber: {this.Reason}";
}
