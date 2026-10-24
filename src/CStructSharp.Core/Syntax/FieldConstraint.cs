namespace CStructSharp.Syntax;

/// <summary>
///     A condition a field's value must satisfy, written after its declarator as <c>@expect(expression)</c> or
///     <c>@supports(expression)</c>. The expression may name the field itself, the fields before it, <c>#define</c>
///     constants, enum members and caller variables, exactly as an array count may; a read fails when it evaluates to zero.
/// </summary>
/// <param name="Kind">Whether a failure means the data is malformed or unsupported.</param>
/// <param name="Expression">The condition; zero means it is not satisfied.</param>
/// <param name="Text">The condition as written in the layout (trimmed), which every failure message quotes.</param>
/// <param name="Label">The optional label written after the condition (<c>@expect(magic == 0xEF53, "ext.magic")</c>): a stable name or wording a caller can match, carried by every failure; <see langword="null"/> when none was written.</param>
internal sealed record FieldConstraint(CStructSharp.Diagnostics.ConstraintKind Kind, Expr Expression, string Text, string? Label = null);
