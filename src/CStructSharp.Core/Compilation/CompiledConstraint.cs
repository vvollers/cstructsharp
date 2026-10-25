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
internal sealed record CompiledConstraint(CStructSharp.Diagnostics.ConstraintKind Kind, Expr Expression, string Text, string? Label = null, string? Description = null);
