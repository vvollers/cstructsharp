namespace CStructSharp.Diagnostics;

using System;

#pragma warning disable RCS1194 // Binary serialization constructors are intentionally unsupported.
/// <summary>
///     Represents a read that stopped because a field's value did not satisfy an <c>@expect</c> or <c>@supports</c>
///     constraint of the layout. It is a <see cref="CStructReadException"/> with the code
///     <see cref="CStructErrorCode.ReadFailed"/>; <see cref="Kind"/> says whether the data is malformed or merely unsupported.
/// </summary>
public sealed class CStructConstraintException : CStructReadException
{
    // The diagnostic, composed on first use when the failure was created from its facts; the field is kept for that.
    private string? field;
    private string? text;

    /// <summary>Creates a constraint failure.</summary>
    /// <param name="kind">Whether the value is invalid or the layout does not support it.</param>
    /// <param name="constraint">The condition as written in the layout.</param>
    /// <param name="message">The diagnostic naming the field and the condition.</param>
    /// <param name="label">The label the layout gave the constraint, or <see langword="null"/> when it has none.</param>
    /// <param name="description">The message the layout gave the constraint, or <see langword="null"/> when it has none; see <see cref="Description"/>.</param>
    public CStructConstraintException(ConstraintKind kind, string constraint, string message, string? label = null, string? description = null)
        : base(message)
    {
        this.Kind = kind;
        this.Constraint = constraint;
        this.Label = label;
        this.Description = description;
        this.text = message;
    }

    /// <summary>Gets whether the data is malformed (<c>@expect</c>) or unsupported (<c>@supports</c>).</summary>
    public ConstraintKind Kind { get; }

    /// <summary>Gets the condition that was not satisfied, as written in the layout.</summary>
    public string Constraint { get; }

    /// <summary>
    ///     Gets the label the layout gave the constraint (<c>@expect(magic == 0xEF53, "ext.magic")</c>), a stable name or
    ///     wording a caller can match instead of the message; <see langword="null"/> when the constraint has none.
    /// </summary>
    public string? Label { get; }

    /// <summary>
    ///     Gets the message the layout gave the constraint (<c>@expect(magic == 0xEF53, "ext.magic", "Not an ext superblock")</c>);
    ///     <see langword="null"/> when the constraint has none. When present it replaces the generated sentence in <see cref="Message"/>,
    ///     which then still ends with the position facts and the <see cref="Label"/> in brackets.
    /// </summary>
    public string? Description { get; }

    /// <summary>Gets the diagnostic naming the field and the condition (or the layout's own message), with the position facts a failure carries.</summary>
    public override string Message => this.Compose(this.text ??= ReadFailures.ConstraintNotSatisfied(this.Kind, this.field!, this.Constraint, this.Label, this.Description));

    /// <summary>
    ///     Creates the failure of a field that breaks a condition without composing its message: the reader that finds the failure
    ///     often only hands it back (a non-throwing <c>TryParse</c>, a collected violation), and the message is built from these facts
    ///     when it is first read.
    /// </summary>
    /// <param name="kind">Whether the value is invalid or the layout does not support it.</param>
    /// <param name="constraint">The condition as written in the layout.</param>
    /// <param name="field">The name of the field whose value breaks the condition.</param>
    /// <param name="label">The label the layout gave the constraint, or <see langword="null"/> when it has none.</param>
    /// <param name="description">The message the layout gave the constraint, or <see langword="null"/> when it has none.</param>
    /// <returns>The failure.</returns>
    internal static CStructConstraintException FromFacts(ConstraintKind kind, string constraint, string field, string? label, string? description = null)
        => new(kind, constraint, string.Empty, label, description) { field = field, text = null, };
}
#pragma warning restore RCS1194
