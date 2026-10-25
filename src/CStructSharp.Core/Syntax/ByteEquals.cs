namespace CStructSharp.Syntax;

using System;

/// <summary>
///     The comparison of a field's bytes with a byte string in a constraint: <c>oem == "NTFS    "</c> is true when the field
///     holds exactly those bytes, and <c>oem != "NTFS    "</c> when it does not. The compiler checks that the field is a
///     fixed-size <c>uint8</c> or <c>char</c> array of the literal's length, and replaces the comparison by a read of a hidden
///     one-or-zero value (<see cref="SlotName"/>) that the reader publishes right after the field.
/// </summary>
internal sealed class ByteEquals : Expr
{
    /// <summary>Creates a comparison.</summary>
    /// <param name="field">The name of the field the bytes are compared with.</param>
    /// <param name="bytes">The bytes the field must (or, when negated, must not) hold.</param>
    /// <param name="negated">Whether the comparison is <c>!=</c>.</param>
    public ByteEquals(string field, byte[] bytes, bool negated)
    {
        this.Field = field;
        this.Bytes = bytes;
        this.Negated = negated;
    }

    /// <summary>Gets the name of the field the bytes are compared with.</summary>
    public string Field { get; }

    /// <summary>Gets the bytes; the array is shared and must not be modified.</summary>
    public byte[] Bytes { get; }

    /// <summary>Gets a value indicating whether the comparison is <c>!=</c>.</summary>
    public bool Negated { get; }

    /// <summary>
    ///     Gets the name of the hidden value that is 1 when the field holds the bytes. The <c>#</c> cannot be part of a layout
    ///     identifier, so the name never collides with a field or a caller variable, and the literal's bytes in the name let
    ///     one field carry several comparisons without any numbering.
    /// </summary>
    public string SlotName => SlotNameOf(this.Field, this.Bytes);

    /// <summary>The name of the hidden value for a field and a byte string.</summary>
    /// <param name="field">The field's name.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The slot name.</returns>
    public static string SlotNameOf(string field, byte[] bytes) => field + "#" + BytesLiteral.ToHex(bytes);

    /// <summary>The ordinary expression that stands for this comparison once the hidden value is published.</summary>
    /// <returns>The hidden value's name, or its logical negation.</returns>
    public Expr Lowered()
    {
        Expr value = new Identifier(this.SlotName);
        return this.Negated ? new UnaryOp(UnaryOperatorType.LogicalNot, value) : value;
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The expression to compare with this one.</param>
    /// <returns>True when both compare the same field with the same bytes in the same way.</returns>
    public override bool Equals(Expr? other)
        => other is ByteEquals comparison && this.Negated == comparison.Negated && this.Field == comparison.Field && this.Bytes.AsSpan().SequenceEqual(comparison.Bytes);

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash of the field, the bytes and the operator.</returns>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        hash.Add(this.Field);
        hash.Add(this.Negated);
        hash.Add(BytesLiteral.ContentHash(this.Bytes));
        return hash.ToHashCode();
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The field, the operator and the bytes.</returns>
    public override string ToString() => this.Field + (this.Negated ? " != " : " == ") + BytesLiteral.ToHex(this.Bytes);
}
