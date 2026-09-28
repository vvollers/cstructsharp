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

    /// <summary>A C# string literal for <paramref name="value"/>, escaped for a regular (non-verbatim) literal.</summary>
    /// <param name="value">The text to quote.</param>
    /// <returns>The quoted literal, with control characters and line separators written as escapes.</returns>
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

    /// <summary>Writes one line, indented unless it is empty, and ends it with <c>\n</c>.</summary>
    /// <param name="line">The line text without a line break; empty writes a blank line.</param>
    /// <returns>This writer, for chaining.</returns>
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

    /// <summary>Appends text to the current line, indenting first when the line is still empty.</summary>
    /// <param name="fragment">The text to append; it should contain no line break.</param>
    /// <returns>This writer, for chaining.</returns>
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
    /// <param name="header">The line before the brace, such as a type or method signature; empty writes none.</param>
    /// <returns>This writer, for chaining.</returns>
    public SourceWriter Open(string header)
    {
        if (header.Length > 0)
        {
            this.Line(header);
        }

        this.Line("{");
        this.indentation++;
        return this;
    }

    /// <summary>Closes a brace block, with an optional trailer such as <c>;</c> or <c>);</c>.</summary>
    /// <param name="trailer">The text written directly after the closing brace.</param>
    /// <returns>This writer, for chaining.</returns>
    public SourceWriter Close(string trailer = "")
    {
        this.indentation = Math.Max(0, this.indentation - 1);
        this.Line("}" + trailer);
        return this;
    }

    /// <summary>Increases the indentation of later lines by one level of four spaces.</summary>
    /// <returns>This writer, for chaining.</returns>
    public SourceWriter Indent()
    {
        this.indentation++;
        return this;
    }

    /// <summary>Decreases the indentation of later lines by one level, never below zero.</summary>
    /// <returns>This writer, for chaining.</returns>
    public SourceWriter Outdent()
    {
        this.indentation = Math.Max(0, this.indentation - 1);
        return this;
    }

    /// <summary>The text written so far.</summary>
    /// <returns>The generated source, with <c>\n</c> line breaks.</returns>
    public override string ToString() => this.text.ToString();
}
