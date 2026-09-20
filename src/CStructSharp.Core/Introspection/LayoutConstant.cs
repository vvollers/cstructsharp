namespace CStructSharp.Introspection;

using System.Numerics;

/// <summary>The kind of value a <c>#define</c> carries.</summary>
public enum LayoutConstantKind
{
    /// <summary>An integer expression that was resolved at construction time; <see cref="LayoutConstant.Value"/> is a <see cref="BigInteger"/>.</summary>
    Integer,

    /// <summary>An integer expression that depends on a caller-supplied variable; <see cref="LayoutConstant.Value"/> is <see langword="null"/>.</summary>
    Expression,

    /// <summary>A quoted text literal; <see cref="LayoutConstant.Value"/> is a <see cref="string"/>.</summary>
    Text,

    /// <summary>A <c>b"..."</c> byte-string literal; <see cref="LayoutConstant.Value"/> is a <c>byte[]</c> copy.</summary>
    Bytes,

    /// <summary>A name defined without a value; <see cref="LayoutConstant.Value"/> is <see langword="null"/>.</summary>
    Empty,

    /// <summary>A function-like macro kept as its unexpanded text; <see cref="LayoutConstant.Value"/> is a <see cref="string"/>.</summary>
    Macro,
}

/// <summary>One <c>#define</c> of a compiled layout, as published by <see cref="CStruct.Constants"/>.</summary>
public sealed class LayoutConstant
{
    private readonly object? value;

    internal LayoutConstant(string name, LayoutConstantKind kind, object? value)
    {
        this.Name = name;
        this.Kind = kind;
        this.value = value;
    }

    /// <summary>Gets the defined name.</summary>
    public string Name { get; }

    /// <summary>Gets what kind of value the definition carries.</summary>
    public LayoutConstantKind Kind { get; }

    /// <summary>Gets the value: a <see cref="BigInteger"/>, <see cref="string"/>, a fresh <c>byte[]</c> copy, or <see langword="null"/> (see <see cref="LayoutConstantKind"/>).</summary>
    public object? Value => this.value is byte[] bytes ? bytes.Clone() : this.value;

    /// <summary>Renders the definition as the <c>#define</c> line that produces it.</summary>
    /// <returns>A <c>#define</c> line, with text and byte values quoted and escaped.</returns>
    public override string ToString()
    {
        return this.Kind switch
        {
            LayoutConstantKind.Text => $"#define {this.Name} \"{Escape((string)this.value!)}\"",
            LayoutConstantKind.Bytes => $"#define {this.Name} b\"{Escape(CStructSharp.Codecs.BoundedTextCodec.Latin1.GetString((byte[])this.value!))}\"",
            LayoutConstantKind.Empty => $"#define {this.Name}",
            LayoutConstantKind.Macro => $"#define {this.Name}{this.Value}",
            LayoutConstantKind.Expression => $"#define {this.Name} (expression)",
            _ => $"#define {this.Name} {this.Value}",
        };
    }

    private static string Escape(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length + 8);
        foreach (char character in text)
        {
            switch (character)
            {
            case '\\':
                builder.Append("\\\\");
                break;
            case '"':
                builder.Append("\\\"");
                break;
            case '\n':
                builder.Append("\\n");
                break;
            case '\r':
                builder.Append("\\r");
                break;
            case '\t':
                builder.Append("\\t");
                break;
            default:
                if (character < ' ' || character == '\x7f')
                {
                    builder.Append("\\x").Append(((int)character).ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(character);
                }

                break;
            }
        }

        return builder.ToString();
    }
}
