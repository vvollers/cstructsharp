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

/// <summary>
///     Recognizes the layout language with one cursor over the source text and direct character tests, building the
///     same small model classes (<see cref="CStructElement"/>, <see cref="Expr"/>) the compiler consumes.
/// </summary>
/// <remarks>
///     Token conventions: every token skips the whitespace and comments <em>after</em> it, literals additionally skip
///     them before, keywords match as text prefixes, and an enum member skips whitespace only after its <c>=</c>; the
///     parser corpus and the fuzz-replay digests pin the accepted language and the produced tree. A block comment may
///     contain a lone <c>*</c>. Every failure surfaces as a <see cref="CStructLayoutException"/> whose message starts
///     with <see cref="SyntaxErrorPrefix"/>. The grammar sections live in partial files: declarations, the
///     preprocessor, struct and union members, and expressions; this file holds the entry points and the lexing.
/// </remarks>
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:ElementsMustAppearInTheCorrectOrder", Justification = "grouped by grammar section for clarity")]
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1203:ConstantsMustAppearBeforeFields", Justification = "grouped by grammar section for clarity")]
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:StaticElementsMustAppearBeforeInstanceElements", Justification = "grouped by grammar section for clarity")]
internal sealed partial class LayoutParser
{
    internal const string SyntaxErrorPrefix = "Layout definition contains invalid syntax: ";

    private static readonly string[] TypeQualifiers = ["const", "volatile", "restrict",];

    private readonly string source;

    /// <summary>Names made visible to <c>#ifdef</c>/<c>#ifndef</c>: every <c>#define</c> seen so far plus the caller's set.</summary>
    private readonly HashSet<string> definedNames;

    /// <summary>The <c>#ifdef</c> stack: whether each open conditional's current branch is being parsed, and whether it has seen <c>#else</c>.</summary>
    private readonly List<(bool Active, bool SawElse)> conditionals = [];

    /// <summary>The <c>#pragma pack</c> stack; the top entry (when any) is the alignment clamp applied to the next composites.</summary>
    private readonly List<Expr?> packStack = [];

    /// <summary>
    ///     The storage an enum declared without a backing type gets (<see cref="CStructCompilationOptions.DefaultEnumStorage"/>);
    ///     <see langword="null"/> applies the compiler rule (32 bits, unsigned unless a member is negative).
    /// </summary>
    private readonly Identifier? defaultEnumStorage;

    /// <summary>Tags declared inside a body (<c>struct tag { } member;</c>) - C and dissect both make them global types.</summary>
    private readonly List<CStructElement> hoisted = [];

    private int position;

    /// <summary>Set when an expression named a qualified member (<c>Enum.Member</c>), so the compiler publishes those names.</summary>
    private bool usesQualifiedIdentifiers;

    private LayoutParser(string source, IReadOnlySet<string>? definedNames = null, string? defaultEnumStorage = null)
    {
        this.source = source;
        this.definedNames = definedNames is null
                                ? new HashSet<string>(StringComparer.Ordinal)
                                : new HashSet<string>(definedNames, StringComparer.Ordinal);
        this.defaultEnumStorage = defaultEnumStorage is null ? null : new Identifier(defaultEnumStorage);
    }

    private bool AtEnd => this.position >= this.source.Length;

    /// <summary>
    ///     Parses a complete layout: zero or more top-level declarations followed by the end of the text.
    ///     <paramref name="definedNames"/> seeds the names <c>#ifdef</c> tests, in addition to the layout's own defines.
    /// </summary>
    public static IReadOnlyList<CStructElement> ParseLayout(string source, IReadOnlySet<string>? definedNames = null)
    {
        return ParseLayout(source, definedNames, null, out _);
    }

    /// <summary>
    ///     Parses a complete layout with the storage that an enum declared without a backing type gets, and reports
    ///     whether any expression named a qualified <c>Enum.Member</c>.
    /// </summary>
    public static IReadOnlyList<CStructElement> ParseLayout(string source, IReadOnlySet<string>? definedNames, string? defaultEnumStorage, out bool usesQualifiedIdentifiers)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var parser = new LayoutParser(source, definedNames, defaultEnumStorage);
        parser.SkipTrivia();
        var elements = new List<CStructElement>();
        while (parser.TryParseTopLevel(elements))
        {
        }

        if (parser.conditionals.Count > 0)
        {
            throw parser.Fail("#endif");
        }

        parser.ExpectEnd();
        usesQualifiedIdentifiers = parser.usesQualifiedIdentifiers;
        return elements;
    }

    /// <summary>Parses exactly one top-level declaration (struct, union, typedef, enum, or define).</summary>
    public static CStructElement ParseElement(string source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var parser = new LayoutParser(source);
        parser.SkipTrivia();
        var elements = new List<CStructElement>(1);
        if (!parser.TryParseTopLevel(elements) || elements.Count != 1)
        {
            throw parser.Fail("a struct, union, typedef, enum, or #define declaration");
        }

        parser.ExpectEnd();
        return elements[0];
    }

    /// <summary>Parses one complete expression; leading and trailing whitespace and comments are permitted.</summary>
    public static Expr ParseExpression(string source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var parser = new LayoutParser(source);
        parser.SkipTrivia();
        Expr expression = parser.ParseExpr();
        parser.ExpectEnd();
        return expression;
    }

    /// <summary>
    ///     Parses one integer literal at the start of the text and ignores whatever follows it. <paramref name="radix"/>
    ///     0 accepts every spelling (like an expression term does); 2, 8, and 16 require the matching <c>0b</c>,
    ///     <c>0o</c>, or <c>0x</c> prefix; 10 accepts a plain decimal literal only.
    /// </summary>
    public static Expr ParseLiteral(string source, int radix = 0)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var parser = new LayoutParser(source);
        if (radix == 0)
        {
            parser.SkipTrivia();
        }

        int sign = parser.ParseSign();
        Expr? literal = radix switch
        {
            0 => parser.TryParseRadixLiteral(sign, 16) ??
                 parser.TryParseRadixLiteral(sign, 2) ??
                 parser.TryParseRadixLiteral(sign, 8) ??
                 parser.TryParseDecimalLiteral(sign),
            10 => parser.TryParseDecimalLiteral(sign),
            2 or 8 or 16 => parser.TryParseRadixLiteral(sign, radix),
            _ => throw new ArgumentOutOfRangeException(nameof(radix)),
        };

        return literal ?? throw parser.Fail("an integer literal");
    }

    /// <summary>
    ///     Reads the digit run at the start of the text in the requested radix, dropping <c>_</c> separators, and
    ///     ignores whatever follows it. The run must contain at least one real digit.
    /// </summary>
    public static string ParseDigits(string source, int radix)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var parser = new LayoutParser(source);
        int digitCount = parser.SkipDigitRun(radix);
        if (digitCount == 0)
        {
            throw parser.Fail("a digit");
        }

        return NormalizeDigits(source.AsSpan(0, parser.position), digitCount);
    }

    /// <summary>Returns whether a character is a digit of the requested radix or the <c>_</c> separator.</summary>
    public static bool IsDigitOrSeparator(char character, int radix)
    {
        return character == '_' || DigitValue(character, radix) >= 0;
    }

    /// <summary>Parses one enum member (<c>Name</c> or <c>Name = expression</c>) with optional surrounding whitespace.</summary>
    public static EnumValue ParseEnumValue(string source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new LayoutParser(source).ParseEnumMember();
    }

    /// <summary>Parses a comma-separated enum member list without the surrounding braces.</summary>
    public static IReadOnlyList<EnumValue> ParseEnumValues(string source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var parser = new LayoutParser(source);
        List<EnumValue> values = parser.ParseEnumMembers();
        parser.ExpectEnd();
        return values;
    }

    /// <summary>Parses a braced enum member list, e.g. <c>{ Red = 1, Green }</c>.</summary>
    public static IReadOnlyList<EnumValue> ParseEnumValuesInBrackets(string source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var parser = new LayoutParser(source);
        parser.ExpectToken('{');
        List<EnumValue> values = parser.ParseEnumMembers();
        parser.ExpectToken('}');
        parser.ExpectEnd();
        return values;
    }

    /// <summary>Parses one field declaration with one or more declarators, e.g. <c>uint8 *a, b[4];</c>.</summary>
    public static IReadOnlyList<Field> ParseFieldGroup(string source)
    {
        if (source is null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var parser = new LayoutParser(source);
        List<Field> fields = parser.ParseFieldDeclaration();
        parser.ExpectEnd();
        return fields;
    }

    /// <summary>
    ///     Parses one top-level item into <paramref name="destination"/>; returns false at the end of the text (or of
    ///     the current conditional branch's visible text). A preprocessor line may add nothing; a typedef may add several.
    /// </summary>
    private bool TryParseTopLevel(List<CStructElement> destination)
    {
        while (true)
        {
            if (this.AtEnd)
            {
                return false;
            }

            if (this.source[this.position] == '#')
            {
                // Conditionals, #undef, and #pragma steer the parse without producing an element of their own.
                int before = destination.Count;
                this.ParsePreprocessorLine(destination);
                if (destination.Count > before)
                {
                    return true;
                }

                continue;
            }

            if (this.StartsWithKeyword("struct"))
            {
                int before = destination.Count;
                this.ParseTopLevelComposite("struct", isUnion: false, destination);
                this.FlushHoisted(destination, before);
                return true;
            }

            if (this.StartsWithKeyword("union"))
            {
                int before = destination.Count;
                this.ParseTopLevelComposite("union", isUnion: true, destination);
                this.FlushHoisted(destination, before);
                return true;
            }

            if (this.StartsWithKeyword("typedef"))
            {
                int before = destination.Count;
                this.ParseTypedef(destination);
                this.FlushHoisted(destination, before);
                return true;
            }

            if (this.StartsWithKeyword("enum"))
            {
                this.ParseEnum(isFlag: false, destination);
                return true;
            }

            if (this.StartsWithKeyword("flag"))
            {
                this.ParseEnum(isFlag: true, destination);
                return true;
            }

            return false;
        }
    }

    /// <summary>Skips whitespace, <c>//</c> line comments, and <c>/* */</c> block comments; an unclosed block comment is an error.</summary>
    private void SkipTrivia()
    {
        while (true)
        {
            this.SkipWhitespace();
            if (this.position + 1 >= this.source.Length || this.source[this.position] != '/')
            {
                return;
            }

            char next = this.source[this.position + 1];
            if (next == '/')
            {
                int newline = this.source.IndexOf('\n', this.position + 2);
                this.position = newline == -1 ? this.source.Length : newline + 1;
            }
            else if (next == '*')
            {
                int close = this.source.IndexOf("*/", this.position + 2, StringComparison.Ordinal);
                if (close == -1)
                {
                    this.position = this.source.Length;
                    throw this.Fail("the end of the block comment");
                }

                this.position = close + 2;
            }
            else
            {
                return;
            }
        }
    }

    private void SkipWhitespace()
    {
        while (!this.AtEnd)
        {
            char current = this.source[this.position];
            if (current != ' ' && !char.IsWhiteSpace(current))
            {
                // A backslash-newline joins physical lines anywhere, as in C; it costs one extra test only when
                // the current character is a backslash, which ordinary layout text never contains.
                if (current == '\\' && this.IsLineContinuation(this.position))
                {
                    this.position += this.source[this.position + 1] == '\r' ? 3 : 2;
                    continue;
                }

                return;
            }

            this.position++;
        }
    }

    private bool StartsWith(string text)
    {
        return this.source.AsSpan(this.position).StartsWith(text, StringComparison.Ordinal);
    }

    /// <summary>Places the tags hoisted out of the declaration just parsed before it, so they read in source order.</summary>
    private void FlushHoisted(List<CStructElement> destination, int index)
    {
        if (this.hoisted.Count > 0)
        {
            destination.InsertRange(index, this.hoisted);
            this.hoisted.Clear();
        }
    }

    /// <summary>True at the start of a top-level declaration (a declaration keyword or a preprocessor line).</summary>
    private bool AtDeclarationKeyword()
    {
        return !this.AtEnd &&
               (this.source[this.position] == '#' || this.StartsWithKeyword("struct") || this.StartsWithKeyword("union") ||
                this.StartsWithKeyword("typedef") || this.StartsWithKeyword("enum") || this.StartsWithKeyword("flag"));
    }

    /// <summary>A keyword is only a keyword when no identifier character follows it (<c>structX</c> is a type name).</summary>
    private bool StartsWithKeyword(string keyword)
    {
        return this.StartsWith(keyword) && !this.IsIdentifierPart(this.position + keyword.Length);
    }

    /// <summary>Keyword followed (after trivia) by a specific character, e.g. <c>if (</c>; the cursor is never moved.</summary>
    private bool StartsWithKeywordThen(string keyword, char character)
    {
        if (!this.StartsWith(keyword))
        {
            return false;
        }

        int start = this.position;
        this.position += keyword.Length;
        this.SkipTrivia();
        bool matched = !this.AtEnd && this.source[this.position] == character;
        this.position = start;
        return matched;
    }

    /// <summary>Consumes the keyword text when present (a prefix match, like the combinator grammar) plus trailing trivia.</summary>
    private bool TryKeyword(string keyword)
    {
        if (!this.StartsWith(keyword))
        {
            return false;
        }

        this.position += keyword.Length;
        this.SkipTrivia();
        return true;
    }

    /// <summary>Consumes a keyword the caller has already seen at the cursor, plus trailing trivia.</summary>
    private void SkipKeyword(string keyword)
    {
        this.position += keyword.Length;
        this.SkipTrivia();
    }

    private bool TryChar(char character)
    {
        if (this.AtEnd || this.source[this.position] != character)
        {
            return false;
        }

        this.position++;
        return true;
    }

    private bool TryToken(char character)
    {
        if (!this.TryChar(character))
        {
            return false;
        }

        this.SkipTrivia();
        return true;
    }

    private void ExpectToken(char character)
    {
        if (!this.TryToken(character))
        {
            throw this.Fail($"'{character}'");
        }
    }

    private void ExpectEnd()
    {
        if (!this.AtEnd)
        {
            throw this.Fail("the end of the layout");
        }
    }

    private bool IsIdentifierStart(int index)
    {
        if (index >= this.source.Length)
        {
            return false;
        }

        char character = this.source[index];
        return character == '_' || char.IsLetter(character);
    }

    private bool IsIdentifierPart(int index)
    {
        if (index >= this.source.Length)
        {
            return false;
        }

        char character = this.source[index];
        return character == '_' || char.IsLetterOrDigit(character);
    }

    private bool IsExtendedIdentifierStart(int index)
    {
        if (index >= this.source.Length)
        {
            return false;
        }

        char character = this.source[index];
        return character is '_' or '*' or '<' or '>' || char.IsLetter(character);
    }

    private bool IsExtendedIdentifierPart(int index)
    {
        if (index >= this.source.Length)
        {
            return false;
        }

        char character = this.source[index];
        return character is '_' or '*' or '<' or '>' || char.IsLetterOrDigit(character);
    }

    /// <summary>A plain identifier token (letters, digits, <c>_</c>) plus trailing trivia, or null without moving.</summary>
    private Identifier? TryParseIdentifier()
    {
        if (!this.IsIdentifierStart(this.position))
        {
            return null;
        }

        int start = this.position;
        do
        {
            this.position++;
        }
        while (this.IsIdentifierPart(this.position));

        var identifier = new Identifier(this.source.Substring(start, this.position - start)) { SourceOffset = start, };
        this.SkipTrivia();
        return identifier;
    }

    private Identifier ExpectIdentifier()
    {
        return this.TryParseIdentifier() ?? throw this.Fail("an identifier");
    }

    /// <summary>Converts a zero-based offset into the one-based line and column the diagnostics report.</summary>
    internal static (int Line, int Column) LocatePosition(string source, int offset)
    {
        int line = 1;
        int column = 1;
        int end = Math.Min(offset, source.Length);
        for (int index = 0; index < end; index++)
        {
            if (source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        return (line, column);
    }

    /// <summary>A syntax error at the current position, naming what was found and what the grammar expected.</summary>
    /// <param name="expected">What the grammar expected here, as a phrase (<c>an identifier</c>).</param>
    /// <returns>The exception to throw, with its line and column.</returns>
    private CStructLayoutException Fail(string expected)
    {
        (int line, int column) = LocatePosition(this.source, this.position);
        string found = this.AtEnd
                           ? "end of input"
                           : $"'{DescribeCharacter(this.source[this.position])}'";
        return CStructLayoutException.SyntaxError(
            FormattableString.Invariant($"{SyntaxErrorPrefix}unexpected {found} at line {line}, column {column}; expected {expected}."),
            line,
            column);
    }

    private static string DescribeCharacter(char character)
    {
        return character switch
        {
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            _ when char.IsControl(character) => $"\\u{(int)character:X4}",
            _ => character.ToString(),
        };
    }
}
