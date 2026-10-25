namespace CStructSharp.Compilation;

using CStructSharp.Syntax;

/// <summary>
///     One <c>@expect</c> or <c>@supports</c> condition of a compiled field, with its calls folded to literals: the read
///     evaluates <see cref="Expression"/> once the field's value (and the values before it) are known and fails when the
///     result is zero.
/// </summary>
/// <param name="Kind">Whether a failure means malformed or unsupported data.</param>
/// <param name="Expression">The condition.</param>
/// <param name="Text">The condition as written, quoted by the failure message.</param>
/// <param name="Label">The label written after the condition, carried by the failure; <see langword="null"/> when none.</param>
/// <param name="Description">The message written after the label, which a failure reports instead of the generated sentence; <see langword="null"/> when none.</param>
/// <param name="ByteMatches">The comparisons of the field's bytes with byte strings that <see cref="Expression"/> reads as hidden values; empty when it has none.</param>
/// <param name="IsMagic">Whether it was written <c>@magic</c>: checked before the other conditions of its struct, failing as <see cref="CStructSharp.Diagnostics.ConstraintKind.Unsupported"/>, and the only kind <c>Matches</c> evaluates for a struct that has one.</param>
internal sealed record CompiledConstraint(CStructSharp.Diagnostics.ConstraintKind Kind, Expr Expression, string Text, string? Label = null, string? Description = null, bool IsMagic = false, System.Collections.Immutable.ImmutableArray<ByteMatch> ByteMatches = default)
{
    /// <summary>Gets the suffix keyword the constraint was written with, without the <c>@</c>: <c>expect</c>, <c>supports</c> or <c>magic</c>.</summary>
    public string Keyword => ConstraintLabels.Keyword(this.Kind, this.IsMagic);
}
