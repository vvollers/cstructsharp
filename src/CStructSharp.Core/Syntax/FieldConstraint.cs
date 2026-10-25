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
/// <param name="Description">The optional message written after the label (<c>@expect(magic == 0xEF53, "ext.magic", "Not an ext superblock")</c>): the sentence a failure reports instead of the generated one; <see langword="null"/> when none was written. Only a constraint with a label can have one.</param>
/// <param name="IsMagic">Whether it was written <c>@magic(...)</c>: a discriminator that is checked before the other conditions of its struct and fails as <see cref="CStructSharp.Diagnostics.ConstraintKind.Unsupported"/>.</param>
internal sealed record FieldConstraint(CStructSharp.Diagnostics.ConstraintKind Kind, Expr Expression, string Text, string? Label = null, string? Description = null, bool IsMagic = false)
{
    /// <summary>Gets the suffix keyword the constraint was written with, without the <c>@</c>: <c>expect</c>, <c>supports</c> or <c>magic</c>.</summary>
    public string Keyword => ConstraintLabels.Keyword(this.Kind, this.IsMagic);
}
