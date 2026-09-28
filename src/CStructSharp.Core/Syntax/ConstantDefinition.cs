namespace CStructSharp.Syntax;

using System;
using CStructSharp.Introspection;

/// <summary>
///     A <c>#define</c> whose value is not an integer expression: a quoted text or byte-string literal, a bare name
///     with no value, or a function-like macro kept as opaque text. None of these take part in layout expressions;
///     they are published through <see cref="CStruct.Constants"/> so a format's magic strings travel with its layout.
/// </summary>
internal sealed class ConstantDefinition : CStructElement
{
    /// <summary>Creates a non-integer <c>#define</c>.</summary>
    /// <param name="name">The defined name.</param>
    /// <param name="kind">Whether the value is text, bytes, a bare name, or an opaque macro.</param>
    /// <param name="value">The text or byte value as described by <see cref="Value"/>, or null for a bare name.</param>
    public ConstantDefinition(Identifier name, LayoutConstantKind kind, object? value)
    {
        this.Name = name;
        this.Kind = kind;
        this.Value = value;
    }

    /// <summary>Gets the defined name.</summary>
    public override Identifier Name { get; }

    /// <summary>Gets the kind of value: text, bytes, a bare name, or an opaque macro.</summary>
    public LayoutConstantKind Kind { get; }

    /// <summary>A <see cref="string"/> for text and macro constants, a <see cref="T:byte[]"/> for byte constants, otherwise <see langword="null"/>.</summary>
    public object? Value { get; }

    /// <summary>Checks whether another element is the same constant; byte values compare by content.</summary>
    /// <param name="other">The element to compare with this constant.</param>
    /// <returns>True when both are constants with equal names, kinds, and values.</returns>
    public override bool Equals(CStructElement? other)
    {
        return other is ConstantDefinition constant &&
               this.Name.Equals(constant.Name) &&
               this.Kind == constant.Kind &&
               (this.Value is byte[] bytes
                    ? constant.Value is byte[] otherBytes && bytes.AsSpan().SequenceEqual(otherBytes)
                    : Equals(this.Value, constant.Value));
    }

    /// <summary>Returns a hash code over the name and kind, consistent with equality.</summary>
    /// <returns>A hash combining the name and kind.</returns>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name, this.Kind);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The name, kind, and value as text.</returns>
    public override string ToString()
    {
        return $"Constant: {this.Name} ({this.Kind}) = {this.Value}";
    }
}
