namespace CStructSharp.Parsing;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Introspection;
using CStructSharp.Syntax;
using CStructSharpEnum = CStructSharp.Syntax.Enum;

/// <summary>The preprocessor lines: <c>#define</c>, <c>#ifdef</c>/<c>#ifndef</c>/<c>#else</c>/<c>#endif</c>, <c>#include</c>, <c>#pragma</c>, and the line-level lexing they share.</summary>
internal sealed partial class LayoutParser
{
    /// <summary>
    ///     One line starting with <c>#</c>: <c>#define</c>, <c>#undef</c>, <c>#include</c>, <c>#pragma</c>, or a
    ///     conditional. Inside a false conditional branch every line is skipped, so only the conditional keywords are
    ///     interpreted there.
    /// </summary>
    private void ParsePreprocessorLine(List<CStructElement> destination)
    {
        if (this.StartsWith("#ifdef") || this.StartsWith("#ifndef"))
        {
            bool negate = this.source[this.position + 3] == 'n';
            Identifier name = this.ExpectDirectiveName(negate ? "#ifndef" : "#ifdef");
            this.SkipTrivia();
            bool parentActive = this.conditionals.Count == 0 || this.conditionals[^1].Active;
            bool active = parentActive && (this.definedNames.Contains(name.Name) != negate);
            this.conditionals.Add((active, false));
            this.SkipInactiveBranch();
            return;
        }

        if (this.StartsWith("#else"))
        {
            if (this.conditionals.Count == 0 || this.conditionals[^1].SawElse)
            {
                throw this.Fail("a matching #ifdef or #ifndef before #else");
            }

            this.SkipKeyword("#else");
            bool parentActive = this.conditionals.Count == 1 || this.conditionals[^2].Active;
            this.conditionals[^1] = (parentActive && !this.conditionals[^1].Active, true);
            this.SkipInactiveBranch();
            return;
        }

        if (this.StartsWith("#endif"))
        {
            if (this.conditionals.Count == 0)
            {
                throw this.Fail("a matching #ifdef or #ifndef before #endif");
            }

            this.SkipKeyword("#endif");
            this.conditionals.RemoveAt(this.conditionals.Count - 1);
            this.SkipInactiveBranch();
            return;
        }

        if (this.StartsWith("#define"))
        {
            this.ParseDefine(destination);
            return;
        }

        if (this.StartsWith("#undef"))
        {
            Identifier name = this.ExpectDirectiveName("#undef");
            this.SkipTrivia();
            this.definedNames.Remove(name.Name);
            destination.RemoveAll(element => element is Defines or ConstantDefinition && element.Name.Name == name.Name);
            return;
        }

        if (this.StartsWith("#include"))
        {
            this.SkipKeyword("#include");
            char open = this.AtEnd ? '\0' : this.source[this.position];
            char close = open == '<' ? '>' : open == '"' ? '"' : '\0';
            if (close == '\0')
            {
                throw this.Fail("a quoted or angle-bracketed include path");
            }

            int end = this.source.IndexOf(close, this.position + 1);
            if (end < 0)
            {
                throw this.Fail($"'{close}'");
            }

            destination.Add(new IncludeDirective(this.source[(this.position + 1)..end], open == '<'));
            this.position = end + 1;
            this.SkipTrivia();
            return;
        }

        if (this.StartsWith("#pragma"))
        {
            this.SkipKeyword("#pragma");
            this.ParsePragma();
            return;
        }

        throw this.Fail("a preprocessor directive (#define, #undef, #include, #pragma, #ifdef, #ifndef, #else, #endif)");
    }

    /// <summary>
    ///     While the innermost conditional is false, skips whole lines until the <c>#else</c>/<c>#endif</c> that
    ///     closes it, counting nested conditionals so an inner <c>#endif</c> does not end the outer branch.
    /// </summary>
    private void SkipInactiveBranch()
    {
        if (this.conditionals.Count == 0 || this.conditionals[^1].Active)
        {
            return;
        }

        int depth = 0;
        while (!this.AtEnd)
        {
            if (this.source[this.position] == '#')
            {
                if (depth == 0 && (this.StartsWith("#else") || this.StartsWith("#endif")))
                {
                    return;
                }

                if (this.StartsWith("#ifdef") || this.StartsWith("#ifndef"))
                {
                    depth++;
                }
                else if (this.StartsWith("#endif"))
                {
                    depth--;
                }
            }

            this.SkipRestOfLine();
            this.SkipTrivia();
        }

        throw this.Fail("#endif");
    }

    /// <summary>
    ///     <c>#define NAME expression</c> (a layout constant), <c>#define NAME "text"</c> / <c>b"bytes"</c> /
    ///     <c>'c'</c> (a text or byte constant), <c>#define NAME(args) ...</c> (an opaque macro), or a bare
    ///     <c>#define NAME</c>. Every form makes the name visible to <c>#ifdef</c>.
    /// </summary>
    private void ParseDefine(List<CStructElement> destination)
    {
        Identifier name = this.ExpectDirectiveName("#define");
        this.definedNames.Add(name.Name);

        // A function-like macro has its parameter list glued to the name; nothing of it takes part in a layout.
        if (!this.AtEnd && this.source[this.position] == '(')
        {
            destination.Add(new ConstantDefinition(name, LayoutConstantKind.Macro, this.ReadRestOfLine()));
            this.SkipTrivia();
            return;
        }

        this.SkipInlineTrivia();
        if (this.AtEnd || this.source[this.position] is '\n' or '\r')
        {
            destination.Add(new ConstantDefinition(name, LayoutConstantKind.Empty, null));
            this.SkipTrivia();
            return;
        }

        char current = this.source[this.position];
        bool bytesPrefix = current == 'b' && this.position + 1 < this.source.Length && this.source[this.position + 1] is '"' or '\'';
        if (current is '"' or '\'' || bytesPrefix)
        {
            if (bytesPrefix)
            {
                this.position++;
            }

            string text = this.ParseQuotedLiteral();
            destination.Add(
                bytesPrefix
                    ? new ConstantDefinition(name, LayoutConstantKind.Bytes, BoundedTextCodec.Latin1.GetBytes(text))
                    : new ConstantDefinition(name, LayoutConstantKind.Text, text));
            this.SkipTrivia();
            return;
        }

        // The value is an integer expression; the next declaration may follow it on the same line. A line that is
        // not an expression (`#define MAGIC XYZ\0`, a token-pasting body, ...) is kept as text, as dissect does:
        // using it where a number is needed is the error, not defining it.
        int valueStart = this.position;
        Expr? value = null;
        try
        {
            this.SkipInlineTrivia();
            value = this.ParseExpr();
        }
        catch (CStructLayoutException)
        {
            value = null;
        }

        if (value is not null && (this.AtEnd || this.AtDeclarationKeyword() || this.LineBreakBetween(valueStart, this.position)))
        {
            destination.Add(new Defines(name, value));
            return;
        }

        this.position = valueStart;
        destination.Add(new ConstantDefinition(name, LayoutConstantKind.Text, this.ReadRestOfLine().Trim()));
        this.SkipTrivia();
    }

    /// <summary>Whether the text between two cursor positions holds a line break (a continuation does not count).</summary>
    /// <param name="start">The first source character to inspect, inclusive.</param>
    /// <param name="end">The parsed cursor position, exclusive.</param>
    /// <returns>Whether an unescaped physical line ending occurs in the inspected source range.</returns>
    private bool LineBreakBetween(int start, int end)
    {
        for (int index = start; index < end && index < this.source.Length; index++)
        {
            if (this.source[index] == '\\' && this.IsLineContinuation(index))
            {
                // Skip both characters of CRLF; its LF must not look like a second, unescaped line ending.
                index += this.source[index + 1] == '\r' ? 2 : 1;
                continue;
            }

            if (this.source[index] is '\n' or '\r')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     <c>#pragma pack(N)</c>, <c>pack(push[, N])</c>, <c>pack(pop)</c>, <c>pack()</c>: maintains the pack stack
    ///     whose top clamps the alignment of every following composite exactly like a composite <c>@align(N)</c>.
    ///     Any other pragma is ignored, as a C compiler ignores pragmas it does not know.
    /// </summary>
    private void ParsePragma()
    {
        if (!this.TryKeyword("pack"))
        {
            this.SkipRestOfLine();
            this.SkipTrivia();
            return;
        }

        this.ExpectToken('(');
        if (this.TryToken(')'))
        {
            this.packStack.Clear();
            return;
        }

        if (this.TryKeyword("push"))
        {
            Expr? value = this.CurrentPack;
            if (this.TryToken(','))
            {
                value = this.ParseExpr();
            }

            this.packStack.Add(value);
        }
        else if (this.TryKeyword("pop"))
        {
            if (this.packStack.Count > 0)
            {
                this.packStack.RemoveAt(this.packStack.Count - 1);
            }
        }
        else
        {
            // `pack(N)` replaces the current clamp without pushing.
            Expr value = this.ParseExpr();
            if (this.packStack.Count == 0)
            {
                this.packStack.Add(value);
            }
            else
            {
                this.packStack[^1] = value;
            }
        }

        this.ExpectToken(')');
    }

    /// <summary>A double- or single-quoted literal with C escapes; the cursor starts on the opening quote.</summary>
    private string ParseQuotedLiteral()
    {
        char quote = this.source[this.position++];
        var builder = new StringBuilder();
        while (true)
        {
            // A literal may span lines: dissect sees the evaluated Python string, so `'\n'` reaches it as a real
            // line break inside the quotes.
            if (this.AtEnd)
            {
                throw this.Fail($"the closing {quote}");
            }

            char current = this.source[this.position++];
            if (current == quote)
            {
                return builder.ToString();
            }

            if (current != '\\')
            {
                builder.Append(current);
                continue;
            }

            if (this.AtEnd)
            {
                throw this.Fail("an escape sequence");
            }

            if (this.IsLineContinuation(this.position - 1))
            {
                // A backslash-newline inside the literal joins the lines, as in C.
                this.position += this.source[this.position] == '\r' ? 2 : 1;
                continue;
            }

            char escaped = this.source[this.position++];
            switch (escaped)
            {
            case 'n':
                builder.Append('\n');
                break;
            case 'r':
                builder.Append('\r');
                break;
            case 't':
                builder.Append('\t');
                break;
            case '0':
                builder.Append('\0');
                break;
            case 'x':
                int digits = 0;
                int value = 0;
                while (digits < 2 && !this.AtEnd && DigitValue(this.source[this.position], 16) >= 0)
                {
                    value = (value * 16) + DigitValue(this.source[this.position], 16);
                    this.position++;
                    digits++;
                }

                if (digits == 0)
                {
                    throw this.Fail("a hexadecimal digit");
                }

                builder.Append((char)value);
                break;
            default:
                builder.Append(escaped);
                break;
            }
        }
    }

    /// <summary>Returns the rest of the physical line (continuations joined, trailing comment kept) and leaves the cursor at its end.</summary>
    private string ReadRestOfLine()
    {
        var builder = new StringBuilder();
        while (!this.AtEnd)
        {
            char current = this.source[this.position];
            if (current is '\n' or '\r')
            {
                break;
            }

            if (current == '\\' && this.IsLineContinuation(this.position))
            {
                // The joined line keeps one space where the break was, like a C preprocessor's macro body.
                this.position += this.source[this.position + 1] == '\r' ? 3 : 2;
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                while (!this.AtEnd && this.source[this.position] is ' ' or '\t')
                {
                    this.position++;
                }

                continue;
            }

            builder.Append(current);
            this.position++;
        }

        return builder.ToString().TrimEnd();
    }

    /// <summary>Skips to the end of the line, following backslash line continuations.</summary>
    private void SkipRestOfLine()
    {
        while (!this.AtEnd && this.source[this.position] is not ('\n' or '\r'))
        {
            if (this.source[this.position] == '\\' && this.IsLineContinuation(this.position))
            {
                this.position += this.source[this.position + 1] == '\r' ? 3 : 2;
                continue;
            }

            this.position++;
        }
    }

    /// <summary>Reads the identifier a directive names; it must sit on the directive's own line.</summary>
    private Identifier ExpectDirectiveName(string directive)
    {
        this.position += directive.Length;
        this.SkipInlineTrivia();
        int nameStart = this.position;
        if (!this.IsIdentifierStart(this.position))
        {
            throw this.Fail("an identifier on the " + directive + " line");
        }

        while (this.IsIdentifierPart(this.position))
        {
            this.position++;
        }

        return new Identifier(this.source[nameStart..this.position]) { SourceOffset = nameStart, };
    }

    /// <summary>Skips non-line-ending whitespace, block comments, and continuations where a line end is significant.</summary>
    private void SkipInlineTrivia()
    {
        while (!this.AtEnd)
        {
            char current = this.source[this.position];
            if (current is not ('\r' or '\n') && char.IsWhiteSpace(current))
            {
                this.position++;
            }
            else if (current == '\\' && this.IsLineContinuation(this.position))
            {
                this.position += this.source[this.position + 1] == '\r' ? 3 : 2;
            }
            else if (current == '/' && this.position + 1 < this.source.Length && this.source[this.position + 1] == '*')
            {
                int close = this.source.IndexOf("*/", this.position + 2, StringComparison.Ordinal);
                if (close == -1)
                {
                    this.position = this.source.Length;
                    throw this.Fail("the end of the block comment");
                }

                this.position = close + 2;
            }
            else if (current == '/' && this.position + 1 < this.source.Length && this.source[this.position + 1] == '/')
            {
                // A line comment ends the value; leave the cursor on the line end so it is still visible.
                while (!this.AtEnd && this.source[this.position] is not ('\n' or '\r'))
                {
                    this.position++;
                }
            }
            else
            {
                return;
            }
        }
    }

    /// <summary>Whether the backslash at <paramref name="index"/> is immediately followed by a line end.</summary>
    private bool IsLineContinuation(int index)
    {
        int next = index + 1;
        return next < this.source.Length &&
               (this.source[next] == '\n' || (this.source[next] == '\r' && next + 1 < this.source.Length && this.source[next + 1] == '\n'));
    }
}
