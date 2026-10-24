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
    /// <summary>Creates a constraint failure.</summary>
    /// <param name="kind">Whether the value is invalid or the layout does not support it.</param>
    /// <param name="constraint">The condition as written in the layout.</param>
    /// <param name="message">The diagnostic naming the field and the condition.</param>
    /// <param name="label">The label the layout gave the constraint, or <see langword="null"/> when it has none.</param>
    public CStructConstraintException(ConstraintKind kind, string constraint, string message, string? label = null)
        : base(message)
    {
        this.Kind = kind;
        this.Constraint = constraint;
        this.Label = label;
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
}
#pragma warning restore RCS1194
