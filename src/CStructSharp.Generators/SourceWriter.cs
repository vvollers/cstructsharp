namespace CStructSharp.Generators;

using System;
using System.Globalization;
using System.Text;

/// <summary>An indented text builder for generated C#: four-space indentation, one statement per line.</summary>
internal sealed class SourceWriter
{
    private readonly StringBuilder text = new();
    private int indentation;
    private bool atLineStart = true;

    public int Length => this.text.Length;

    /// <summary>A C# string literal for <paramref name="value"/>, escaped for a regular (non-verbatim) literal.</summary>
    public static string Literal(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
            case '"':
                builder.Append("\\\"");
                break;
            case '\\':
                builder.Append("\\\\");
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
            case '\0':
                builder.Append("\\0");
                break;
            default:
                if (character < ' ' || character == '\u2028' || character == '\u2029')
                {
                    builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(character);
                }

                break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    public SourceWriter Line(string line = "")
    {
        if (line.Length == 0)
        {
            this.text.Append('\n');
            this.atLineStart = true;
            return this;
        }

        this.Write(line);
        this.text.Append('\n');
        this.atLineStart = true;
        return this;
    }

    public SourceWriter Write(string fragment)
    {
        if (this.atLineStart && fragment.Length > 0)
        {
            this.text.Append(' ', this.indentation * 4);
            this.atLineStart = false;
        }

        this.text.Append(fragment);
        return this;
    }

    /// <summary>Opens a brace block: writes <paramref name="header"/>, then <c>{</c>, and indents.</summary>
    public SourceWriter Open(string header)
    {
        this.Line(header);
        this.Line("{");
        this.indentation++;
        return this;
    }

    /// <summary>Closes a brace block, with an optional trailer such as <c>;</c> or <c>);</c>.</summary>
    public SourceWriter Close(string trailer = "")
    {
        this.indentation = Math.Max(0, this.indentation - 1);
        this.Line("}" + trailer);
        return this;
    }

    public SourceWriter Indent()
    {
        this.indentation++;
        return this;
    }

    public SourceWriter Outdent()
    {
        this.indentation = Math.Max(0, this.indentation - 1);
        return this;
    }

    public override string ToString() => this.text.ToString();
}
