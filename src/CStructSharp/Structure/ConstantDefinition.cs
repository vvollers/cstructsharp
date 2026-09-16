namespace CStructSharp.Structure;

using System;

/// <summary>
///     A <c>#define</c> whose value is not an integer expression: a quoted text or byte-string literal, a bare name
///     with no value, or a function-like macro kept as opaque text. None of these take part in layout expressions;
///     they are published through <see cref="CStruct.Constants"/> so a format's magic strings travel with its layout.
/// </summary>
internal sealed class ConstantDefinition : CStructElement
{
    public ConstantDefinition(Identifier name, LayoutConstantKind kind, object? value)
    {
        this.Name = name;
        this.Kind = kind;
        this.Value = value;
    }

    public override Identifier Name { get; }

    public LayoutConstantKind Kind { get; }

    /// <summary>A <see cref="string"/> for text and macro constants, a <see cref="T:byte[]"/> for byte constants, otherwise <see langword="null"/>.</summary>
    public object? Value { get; }

    public override bool Equals(CStructElement? other)
    {
        return other is ConstantDefinition constant &&
               this.Name.Equals(constant.Name) &&
               this.Kind == constant.Kind &&
               (this.Value is byte[] bytes
                    ? constant.Value is byte[] otherBytes && bytes.AsSpan().SequenceEqual(otherBytes)
                    : Equals(this.Value, constant.Value));
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name, this.Kind);
    }

    public override string ToString()
    {
        return $"Constant: {this.Name} ({this.Kind}) = {this.Value}";
    }
}
