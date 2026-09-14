namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text;
using CStructSharp.Structure;
using CStructSharpEnum = CStructSharp.Structure.Enum;

/// <summary>
///     Recognizes the layout language with one cursor over the source text and direct character tests, building the
///     same small model classes (<see cref="CStructElement"/>, <see cref="Expr"/>) the compiler consumes.
/// </summary>
/// <remarks>
///     The productions mirror the parser-combinator grammar this class replaced (E1.2) one for one, including its
///     token conventions: every token skips the whitespace and comments <em>after</em> it, literals additionally skip
///     them before, keywords match as text prefixes, and an enum member skips whitespace only after its <c>=</c>.
///     Keeping those conventions is what makes the accepted language, the produced tree, and the frozen fuzz-replay
///     digests identical to the combinator grammar; the only intentional difference is that a block comment may
///     contain a lone <c>*</c>, which the documented grammar always allowed. Every failure surfaces as a
///     <see cref="CStructLayoutException"/> whose message starts with <see cref="SyntaxErrorPrefix"/>.
/// </remarks>
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:ElementsMustAppearInTheCorrectOrder", Justification = "grouped by grammar section for clarity")]
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1203:ConstantsMustAppearBeforeFields", Justification = "grouped by grammar section for clarity")]
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:StaticElementsMustAppearBeforeInstanceElements", Justification = "grouped by grammar section for clarity")]
internal sealed class LayoutParser
{
    internal const string SyntaxErrorPrefix = "Layout definition contains invalid syntax: ";

    private static readonly string[] TypeQualifiers = ["const", "volatile", "restrict",];

    private readonly string source;
    private int position;

    private LayoutParser(string source)
    {
        this.source = source;
    }

    private bool AtEnd => this.position >= this.source.Length;

    /// <summary>Parses a complete layout: zero or more top-level declarations followed by the end of the text.</summary>
    public static IReadOnlyList<CStructElement> ParseLayout(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parser = new LayoutParser(source);
        parser.SkipTrivia();
        var elements = new List<CStructElement>();
        while (parser.TryParseTopLevel(out CStructElement? element))
        {
            elements.Add(element);
        }

        parser.ExpectEnd();
        return elements;
    }

    /// <summary>Parses exactly one top-level declaration (struct, union, typedef, enum, or define).</summary>
    public static CStructElement ParseElement(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parser = new LayoutParser(source);
        parser.SkipTrivia();
        if (!parser.TryParseTopLevel(out CStructElement? element))
        {
            throw parser.Fail("a struct, union, typedef, enum, or #define declaration");
        }

        parser.ExpectEnd();
        return element;
    }

    /// <summary>Parses one complete expression; leading and trailing whitespace and comments are permitted.</summary>
    public static Expr ParseExpression(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
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
        ArgumentNullException.ThrowIfNull(source);
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
        ArgumentNullException.ThrowIfNull(source);
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
        ArgumentNullException.ThrowIfNull(source);
        return new LayoutParser(source).ParseEnumMember();
    }

    /// <summary>Parses a comma-separated enum member list without the surrounding braces.</summary>
    public static IReadOnlyList<EnumValue> ParseEnumValues(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var parser = new LayoutParser(source);
        List<EnumValue> values = parser.ParseEnumMembers();
        parser.ExpectEnd();
        return values;
    }

    /// <summary>Parses a braced enum member list, e.g. <c>{ Red = 1, Green }</c>.</summary>
    public static IReadOnlyList<EnumValue> ParseEnumValuesInBrackets(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
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
        ArgumentNullException.ThrowIfNull(source);
        var parser = new LayoutParser(source);
        List<Field> fields = parser.ParseFieldDeclaration();
        parser.ExpectEnd();
        return fields;
    }

    private bool TryParseTopLevel(out CStructElement element)
    {
        if (this.StartsWith("struct"))
        {
            element = this.ParseNamedComposite("struct", isUnion: false);
            return true;
        }

        if (this.StartsWith("union"))
        {
            element = this.ParseNamedComposite("union", isUnion: true);
            return true;
        }

        if (this.StartsWith("typedef"))
        {
            element = this.ParseTypedef();
            return true;
        }

        if (this.StartsWith("enum"))
        {
            element = this.ParseEnum();
            return true;
        }

        if (this.StartsWith("#define"))
        {
            element = this.ParseDefine();
            return true;
        }

        element = null!;
        return false;
    }

    /// <summary>
    ///     <c>struct Name [@align(N)] { members } [;]</c> or <c>union Name [@align(N)] { fields } [;]</c>. The trailing
    ///     semicolon is optional for both; a union body accepts plain field declarations only.
    /// </summary>
    private Struct ParseNamedComposite(string keyword, bool isUnion)
    {
        this.SkipKeyword(keyword);
        Identifier name = this.ExpectIdentifier();
        Expr? alignment = this.TryParseAlignmentOverride();
        this.ExpectToken('{');
        List<Field> fields = isUnion ? this.ParseUnionMembers() : this.ParseStructMembers();
        this.ExpectToken('}');
        this.TryToken(';');
        return new Struct(name, [.. fields,], isUnion, alignment);
    }

    /// <summary>
    ///     Dispatches the four typedef spellings in the order the combinator grammar tried them: anonymous union,
    ///     anonymous struct, tagged union, tagged struct, then the plain alias <c>typedef type [&lt;|&gt;] [*...] name;</c>.
    /// </summary>
    private CStructElement ParseTypedef()
    {
        this.SkipKeyword("typedef");
        int afterKeyword = this.position;
        if (this.StartsWith("union") || this.StartsWith("struct"))
        {
            bool isUnion = this.source[this.position] == 'u';
            this.SkipKeyword(isUnion ? "union" : "struct");
            int afterCompositeKeyword = this.position;

            // Anonymous form: the alignment override (if any) sits directly after the keyword and `{` follows.
            Expr? alignment = this.TryParseAlignmentOverride();
            if (this.TryToken('{'))
            {
                List<Field> fields = isUnion ? this.ParseUnionMembers() : this.ParseStructMembers();
                this.ExpectToken('}');
                Identifier alias = this.ExpectIdentifier();
                this.ExpectToken(';');
                return new Typedef(alias, new Struct(alias, [.. fields,], isUnion, alignment));
            }

            // Tagged form: `typedef struct Tag [@align(N)] { ... } [;] Alias;` — committed once the brace is seen,
            // because the plain alias form can never accept a `{` after the tag.
            this.position = afterCompositeKeyword;
            Identifier? tag = this.TryParseIdentifier();
            if (tag is not null)
            {
                Expr? tagAlignment = this.TryParseAlignmentOverride();
                if (this.TryToken('{'))
                {
                    List<Field> fields = isUnion ? this.ParseUnionMembers() : this.ParseStructMembers();
                    this.ExpectToken('}');
                    this.TryToken(';');
                    Identifier alias = this.ExpectIdentifier();
                    this.ExpectToken(';');
                    return new Typedef(alias, new Struct(tag, [.. fields,], isUnion, tagAlignment));
                }
            }

            this.position = afterKeyword;
        }

        Identifier underlying = this.ExpectIdentifier();
        string typeName = underlying.Name;
        if (this.TryToken('<'))
        {
            typeName += '<';
        }
        else if (this.TryToken('>'))
        {
            typeName += '>';
        }

        int stars = 0;
        while (this.TryToken('*'))
        {
            stars++;
        }

        Identifier alias2 = this.ExpectIdentifier();
        this.ExpectToken(';');
        return new Typedef(alias2, new Identifier(typeName + new string('*', stars)));
    }

    /// <summary><c>enum Name [: type] { members };</c> — the semicolon is required.</summary>
    private CStructElement ParseEnum()
    {
        this.SkipKeyword("enum");
        Identifier name = this.ExpectIdentifier();
        Identifier type = Identifier.BYTE;
        if (this.TryToken(':'))
        {
            type = this.ExpectIdentifier();
        }

        this.ExpectToken('{');
        List<EnumValue> values = this.ParseEnumMembers();
        this.ExpectToken('}');
        this.ExpectToken(';');
        return CStructSharpEnum.CreateUnevaluated(name, [.. values,], type);
    }

    private List<EnumValue> ParseEnumMembers()
    {
        var values = new List<EnumValue>();
        this.SkipWhitespace();
        if (!this.IsIdentifierStart(this.position))
        {
            return values;
        }

        values.Add(this.ParseEnumMember());
        while (this.TryToken(','))
        {
            values.Add(this.ParseEnumMember());
        }

        return values;
    }

    /// <summary>One member: an identifier optionally followed by <c>=</c>, whitespace, and an expression.</summary>
    private EnumValue ParseEnumMember()
    {
        this.SkipWhitespace();
        Identifier name = this.ExpectIdentifier();
        if (this.TryChar('='))
        {
            this.SkipWhitespace();
            return new EnumValue(name, this.ParseExpr());
        }

        return new EnumValue(name);
    }

    /// <summary><c>#define NAME expression</c>.</summary>
    private CStructElement ParseDefine()
    {
        this.SkipKeyword("#define");
        Identifier name = this.ExpectIdentifier();
        Expr value = this.ParseExpr();
        return new Defines(name, value);
    }

    private List<Field> ParseStructMembers()
    {
        var fields = new List<Field>();
        while (true)
        {
            if (this.StartsWithKeywordThen("if", '('))
            {
                this.ParseConditional(fields);
            }
            else if (this.StartsWithKeywordThen("switch", '('))
            {
                this.ParseSwitch(fields);
            }
            else if (this.TryStartInlineStruct(out Expr? alignment))
            {
                fields.Add(this.ParseInlineStruct(alignment));
            }
            else if (this.CanStartFieldDeclaration())
            {
                fields.AddRange(this.ParseFieldDeclaration());
            }
            else
            {
                return fields;
            }
        }
    }

    private List<Field> ParseUnionMembers()
    {
        var fields = new List<Field>();
        while (this.CanStartFieldDeclaration())
        {
            fields.AddRange(this.ParseFieldDeclaration());
        }

        return fields;
    }

    /// <summary>
    ///     Reports whether <c>struct</c> begins an inline struct (the keyword, an optional alignment override, then
    ///     <c>{</c>). When it does, the cursor is left after the brace; otherwise it is restored so the same text
    ///     parses as a field with a <c>struct</c> keyword hint.
    /// </summary>
    private bool TryStartInlineStruct(out Expr? alignment)
    {
        alignment = null;
        if (!this.StartsWith("struct"))
        {
            return false;
        }

        int start = this.position;
        this.SkipKeyword("struct");
        alignment = this.TryParseAlignmentOverride();
        if (this.TryToken('{'))
        {
            return true;
        }

        this.position = start;
        alignment = null;
        return false;
    }

    /// <summary><c>struct [@align(N)] { members } [name];</c> — cursor starts after the opening brace.</summary>
    private Struct ParseInlineStruct(Expr? alignment)
    {
        List<Field> fields = this.ParseStructMembers();
        this.ExpectToken('}');
        Identifier name = this.TryParseIdentifier() ?? new Identifier(string.Empty);
        this.ExpectToken(';');
        return new Struct(name, [.. fields,], false, alignment);
    }

    /// <summary><c>if (condition) { members } [else { members }]</c>, flattened with per-member predicates.</summary>
    private void ParseConditional(List<Field> destination)
    {
        this.SkipKeyword("if");
        this.ExpectToken('(');
        Expr condition = this.ParseExpr();
        this.ExpectToken(')');
        this.ExpectToken('{');
        List<Field> yes = this.ParseStructMembers();
        this.ExpectToken('}');
        List<Field> no = [];
        if (this.TryKeyword("else"))
        {
            this.ExpectToken('{');
            no = this.ParseStructMembers();
            this.ExpectToken('}');
        }

        var group = new ConditionalGroup(condition);
        ApplyCondition(condition, yes, group, 1, destination);
        ApplyCondition(new UnaryOp(UnaryOperatorType.LogicalNot, condition), no, group, 0, destination);
    }

    /// <summary><c>switch (selector) { case tag: { members } ... [default: { members }] }</c>, no fall-through.</summary>
    private void ParseSwitch(List<Field> destination)
    {
        this.SkipKeyword("switch");
        this.ExpectToken('(');
        Expr selector = this.ParseExpr();
        this.ExpectToken(')');
        this.ExpectToken('{');
        var arms = new List<(Expr Tag, List<Field> Fields)>();
        while (this.TryKeyword("case"))
        {
            Expr tag = this.ParseExpr();
            this.ExpectToken(':');
            this.ExpectToken('{');
            List<Field> armFields = this.ParseStructMembers();
            this.ExpectToken('}');
            arms.Add((tag, armFields));
        }

        List<Field> fallback = [];
        if (this.TryKeyword("default"))
        {
            this.ExpectToken(':');
            this.ExpectToken('{');
            fallback = this.ParseStructMembers();
            this.ExpectToken('}');
        }

        this.ExpectToken('}');

        var labels = new Expr[arms.Count];
        for (int index = 0; index < arms.Count; index++)
        {
            labels[index] = arms[index].Tag;
        }

        var group = new ConditionalGroup(selector, labels);
        Expr any = new Literal(0);
        var tags = new HashSet<Expr>();
        int insertAt = destination.Count;
        for (int armIndex = 0; armIndex < arms.Count; armIndex++)
        {
            (Expr tag, List<Field> armFields) = arms[armIndex];
            if (!tags.Add(tag))
            {
                throw new CStructLayoutException("Duplicate switch case.");
            }

            var condition = new BinaryOp(BinaryOperatorType.Equal, selector, tag);
            ApplyCondition(condition, armFields, group, armIndex, destination);
            any = new BinaryOp(BinaryOperatorType.LogicalOr, any, condition);
        }

        ApplyCondition(new UnaryOp(UnaryOperatorType.LogicalNot, any), fallback, group, -1, destination);

        // This marker is removed during normalization. Keeping it even for empty arms makes duplicate and
        // non-constant labels a compilation error regardless of runtime selection.
        destination.Insert(insertAt, new SwitchCaseValidation(labels));
    }

    private static void ApplyCondition(Expr condition, List<Field> fields, ConditionalGroup group, int arm, List<Field> destination)
    {
        var branch = new ConditionalBranch(group, arm);
        foreach (Field field in fields)
        {
            field.Condition = field.Condition is null
                                  ? condition
                                  : new BinaryOp(BinaryOperatorType.LogicalAnd, condition, field.Condition);
            IReadOnlyList<ConditionalBranch> inner = field.BranchConditions;
            var branches = new ConditionalBranch[inner.Count + 1];
            branches[0] = branch;
            for (int index = 0; index < inner.Count; index++)
            {
                branches[index + 1] = inner[index];
            }

            field.BranchConditions = branches;
            destination.Add(field);
        }
    }

    /// <summary>A field declaration begins with a word: a tag keyword, a qualifier, or the type itself all start with a letter.</summary>
    private bool CanStartFieldDeclaration()
    {
        return this.IsExtendedIdentifierStart(this.position);
    }

    /// <summary>
    ///     <c>[struct|union|enum] words... [dims] [: bits] [@align(N) | @offset] (, declarator)* ;</c>. The last
    ///     word is the first declarator's name and the words before it form the type, except that a single word
    ///     with a bit width is an anonymous bitfield whose one word is the type (LANG-17).
    /// </summary>
    private List<Field> ParseFieldDeclaration()
    {
        string? typeKeywordHint = null;
        if (this.TryKeyword("struct"))
        {
            typeKeywordHint = "struct";
        }
        else if (this.TryKeyword("union"))
        {
            typeKeywordHint = "union";
        }
        else if (this.TryKeyword("enum"))
        {
            typeKeywordHint = "enum";
        }

        List<Identifier> words = this.ParseQualifiedWords();
        if (words.Count == 0)
        {
            throw this.Fail("a field type");
        }

        List<Expr?> dimensions = this.ParseArrayDimensions();
        Expr? bitSize = this.TryParseBitSize();
        (Expr? alignmentOverride, Expr? offsetAssertion) = this.ParsePlacementSuffix();

        bool firstDeclaratorIsAnonymous = words.Count == 1 && bitSize is not null;
        Identifier typeIdentifier = firstDeclaratorIsAnonymous ? new Identifier(words[0].Name) : BuildTypeIdentifier(words);
        int firstPointerDepth = 0;
        if (!firstDeclaratorIsAnonymous)
        {
            foreach (Identifier word in words)
            {
                firstPointerDepth += word.PointerDepth;
            }
        }

        var result = new List<Field>
        {
            MakeField(
                typeIdentifier,
                typeKeywordHint,
                firstDeclaratorIsAnonymous ? null : words[^1],
                firstPointerDepth,
                dimensions,
                bitSize,
                alignmentOverride,
                offsetAssertion),
        };

        while (this.TryToken(','))
        {
            List<Identifier> declaratorWords = this.ParseQualifiedWords();
            List<Expr?> declaratorDimensions = this.ParseArrayDimensions();
            Expr? declaratorBitSize = this.TryParseBitSize();
            (Expr? declaratorAlignment, Expr? declaratorOffset) = this.ParsePlacementSuffix();
            if (declaratorWords.Count == 0 && declaratorBitSize is null)
            {
                throw new CStructLayoutException(
                    SyntaxErrorPrefix + "A declarator must have a name, a bit width, or both.");
            }

            int pointerDepth = 0;
            foreach (Identifier word in declaratorWords)
            {
                pointerDepth += word.PointerDepth;
            }

            result.Add(
                MakeField(
                    typeIdentifier,
                    typeKeywordHint,
                    declaratorWords.Count > 0 ? declaratorWords[^1] : null,
                    pointerDepth,
                    declaratorDimensions,
                    declaratorBitSize,
                    declaratorAlignment,
                    declaratorOffset));
        }

        this.ExpectToken(';');
        return result;
    }

    private static Field MakeField(
        Identifier type,
        string? typeKeywordHint,
        Identifier? name,
        int pointerDepth,
        List<Expr?> dimensions,
        Expr? bitSize,
        Expr? alignmentOverride,
        Expr? offsetAssertion)
    {
        return new Field(
            type,
            name ?? new Identifier(string.Empty),
            BuildArrayCount(dimensions),
            bitSize ?? NoneExpr.Instance,
            pointerDepth,
            typeKeywordHint,
            alignmentOverride,
            offsetAssertion);
    }

    /// <summary>Joins every word but the last with single spaces, skipping words that were only pointer stars.</summary>
    private static Identifier BuildTypeIdentifier(List<Identifier> words)
    {
        var builder = new StringBuilder();
        for (int index = 0; index < words.Count - 1; index++)
        {
            string name = words[index].Name;
            if (name.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(name);
        }

        return new Identifier(builder.ToString());
    }

    /// <summary>
    ///     Builds one declarator's <see cref="Field.ArrayCount"/> from zero or more parsed bracket pairs (LANG-05),
    ///     outermost dimension first. An empty bracket pair (<c>char name[];</c>) is accepted only as the sole
    ///     dimension of a one-dimensional array.
    /// </summary>
    private static IReadOnlyList<Expr> BuildArrayCount(List<Expr?> dimensions)
    {
        if (dimensions.Count == 0)
        {
            return Field.NoArray;
        }

        if (dimensions.Count == 1)
        {
            return [dimensions[0] ?? Field.UnknownArraysize,];
        }

        var result = new Expr[dimensions.Count];
        for (int index = 0; index < dimensions.Count; index++)
        {
            result[index] = dimensions[index] ??
                            throw new CStructLayoutException(
                                SyntaxErrorPrefix +
                                "An unsized array dimension ([]) is allowed only as the sole dimension of a one-dimensional array.");
        }

        return result;
    }

    /// <summary>
    ///     Zero or more words, each optionally preceded by layout-neutral qualifiers (<c>const</c>, <c>volatile</c>,
    ///     <c>restrict</c>, LANG-13) that are consumed and discarded. A word is an extended identifier: letters,
    ///     digits, <c>_</c>, pointer stars, and the endian markers <c>&lt;</c>/<c>&gt;</c>.
    /// </summary>
    private List<Identifier> ParseQualifiedWords()
    {
        var words = new List<Identifier>(3);
        while (true)
        {
            bool sawQualifier = false;
            while (this.TryQualifier())
            {
                sawQualifier = true;
            }

            if (!this.IsExtendedIdentifierStart(this.position))
            {
                if (sawQualifier)
                {
                    throw this.Fail("an identifier");
                }

                return words;
            }

            int start = this.position;
            do
            {
                this.position++;
            }
            while (this.IsExtendedIdentifierPart(this.position));

            words.Add(new Identifier(this.source.Substring(start, this.position - start)));
            this.SkipTrivia();
        }
    }

    private bool TryQualifier()
    {
        foreach (string qualifier in TypeQualifiers)
        {
            if (this.TryKeyword(qualifier))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Zero or more <c>[expression]</c> or <c>[]</c> pairs; an empty pair yields null.</summary>
    private List<Expr?> ParseArrayDimensions()
    {
        List<Expr?>? dimensions = null;
        while (this.TryToken('['))
        {
            dimensions ??= [];
            if (this.TryToken(']'))
            {
                dimensions.Add(null);
                continue;
            }

            dimensions.Add(this.ParseExpr());
            this.ExpectToken(']');
        }

        return dimensions ?? EmptyDimensions;
    }

    private static readonly List<Expr?> EmptyDimensions = [];

    private Expr? TryParseBitSize()
    {
        return this.TryToken(':') ? this.ParseExpr() : null;
    }

    /// <summary>
    ///     A declarator carries at most one trailing placement suffix: <c>@align(N)</c> (LANG-15 alignment override)
    ///     or <c>@N</c> (offset assertion), tried in that order since <c>@align(</c> is the more specific prefix.
    /// </summary>
    private (Expr? AlignmentOverride, Expr? OffsetAssertion) ParsePlacementSuffix()
    {
        if (this.AtEnd || this.source[this.position] != '@')
        {
            return (null, null);
        }

        Expr? alignment = this.TryParseAlignmentOverride();
        if (alignment is not null)
        {
            return (alignment, null);
        }

        this.position++;
        this.SkipTrivia();
        return (null, this.ParseExpr());
    }

    /// <summary>
    ///     <c>@align(expression)</c> when present; otherwise the cursor is unchanged. <c>@align</c> not followed by
    ///     <c>(</c> is left for the caller (an offset assertion may name an identifier starting with "align").
    /// </summary>
    private Expr? TryParseAlignmentOverride()
    {
        if (!this.StartsWith("@align"))
        {
            return null;
        }

        int start = this.position;
        this.position += "@align".Length;
        this.SkipTrivia();
        if (!this.TryToken('('))
        {
            this.position = start;
            return null;
        }

        Expr expression = this.ParseExpr();
        this.ExpectToken(')');
        return expression;
    }

    private const int LogicalOrLevel = 0;
    private const int LogicalAndLevel = 1;
    private const int BitwiseOrLevel = 2;
    private const int BitwiseAndLevel = 3;
    private const int EqualityLevel = 4;
    private const int RelationalLevel = 5;
    private const int ShiftLevel = 6;
    private const int AdditiveLevel = 7;
    private const int MultiplicativeLevel = 8;
    private const int UnaryLevel = 9;

    private Expr ParseExpr()
    {
        return this.ParseBinary(LogicalOrLevel);
    }

    private Expr ParseBinary(int level)
    {
        if (level == UnaryLevel)
        {
            return this.ParseUnary();
        }

        Expr left = this.ParseBinary(level + 1);
        while (this.TryBinaryOperator(level, out BinaryOperatorType type))
        {
            Expr right = this.ParseBinary(level + 1);
            left = new BinaryOp(type, left, right);
        }

        return left;
    }

    private bool TryBinaryOperator(int level, out BinaryOperatorType type)
    {
        type = default;
        if (this.AtEnd)
        {
            return false;
        }

        char current = this.source[this.position];
        char next = this.position + 1 < this.source.Length ? this.source[this.position + 1] : '\0';
        if (!TryRecognizeBinaryOperator(current, next, level, out type, out int length))
        {
            return false;
        }

        this.position += length;
        this.SkipTrivia();
        return true;
    }

    /// <summary>
    ///     Recognizes the binary operator spelled at the cursor when it belongs to the precedence row being parsed;
    ///     two-character spellings are recognized before their one-character prefixes, whichever row is asked, so a
    ///     lone <c>|</c> is never taken for the first half of <c>||</c> and vice versa.
    /// </summary>
    private static bool TryRecognizeBinaryOperator(char current, char next, int level, out BinaryOperatorType type, out int length)
    {
        (type, int operatorLevel, length) = (current, next) switch
        {
            ('|', '|') => (BinaryOperatorType.LogicalOr, LogicalOrLevel, 2),
            ('&', '&') => (BinaryOperatorType.LogicalAnd, LogicalAndLevel, 2),
            ('=', '=') => (BinaryOperatorType.Equal, EqualityLevel, 2),
            ('!', '=') => (BinaryOperatorType.NotEqual, EqualityLevel, 2),
            ('<', '=') => (BinaryOperatorType.LessOrEqual, RelationalLevel, 2),
            ('>', '=') => (BinaryOperatorType.GreaterOrEqual, RelationalLevel, 2),
            ('<', '<') => (BinaryOperatorType.ShiftLeft, ShiftLevel, 2),
            ('>', '>') => (BinaryOperatorType.ShiftRight, ShiftLevel, 2),
            ('|', _) => (BinaryOperatorType.Or, BitwiseOrLevel, 1),
            ('&', _) => (BinaryOperatorType.And, BitwiseAndLevel, 1),
            ('<', _) => (BinaryOperatorType.Less, RelationalLevel, 1),
            ('>', _) => (BinaryOperatorType.Greater, RelationalLevel, 1),
            ('-', _) => (BinaryOperatorType.Minus, AdditiveLevel, 1),
            ('+', _) => (BinaryOperatorType.Add, AdditiveLevel, 1),
            ('/', _) => (BinaryOperatorType.Div, MultiplicativeLevel, 1),
            ('*', _) => (BinaryOperatorType.Mul, MultiplicativeLevel, 1),
            _ => (BinaryOperatorType.Add, UnaryLevel, 0),
        };
        return length > 0 && operatorLevel == level;
    }

    /// <summary>
    ///     Zero or more chainable prefix operators (<c>-</c>, <c>~</c>, <c>!</c>) applied to a postfix expression.
    ///     The chain is collected iteratively (a source may legally be tens of thousands of operators long) and
    ///     folded from the innermost operator outward.
    /// </summary>
    private Expr ParseUnary()
    {
        List<UnaryOperatorType>? operators = null;
        while (!this.AtEnd)
        {
            UnaryOperatorType type;
            switch (this.source[this.position])
            {
            case '-':
                type = UnaryOperatorType.Neg;
                break;
            case '~':
                type = UnaryOperatorType.Complement;
                break;
            case '!':
                type = UnaryOperatorType.LogicalNot;
                break;
            default:
                goto operand;
            }

            (operators ??= []).Add(type);
            this.position++;
            this.SkipTrivia();
        }

        operand:
        Expr expression = this.ParsePostfix();
        if (operators is not null)
        {
            for (int index = operators.Count - 1; index >= 0; index--)
            {
                expression = new UnaryOp(operators[index], expression);
            }
        }

        return expression;
    }

    /// <summary>A term followed by zero or more call argument lists; evaluation later rejects calls.</summary>
    private Expr ParsePostfix()
    {
        Expr expression = this.ParseTerm();
        while (this.TryToken('('))
        {
            ImmutableArray<Expr>.Builder arguments = ImmutableArray.CreateBuilder<Expr>();
            if (!this.TryToken(')'))
            {
                arguments.Add(this.ParseExpr());
                while (this.TryToken(','))
                {
                    arguments.Add(this.ParseExpr());
                }

                this.ExpectToken(')');
            }

            expression = new Call(expression, arguments.ToImmutable());
        }

        return expression;
    }

    /// <summary>A parenthesized expression, an identifier, or an integer literal (which skips leading trivia itself).</summary>
    private Expr ParseTerm()
    {
        if (this.TryToken('('))
        {
            Expr inner = this.ParseExpr();
            this.ExpectToken(')');
            return inner;
        }

        Identifier? identifier = this.TryParseIdentifier();
        if (identifier is not null)
        {
            return identifier;
        }

        this.SkipTrivia();
        int sign = this.ParseSign();
        Expr? literal = this.TryParseRadixLiteral(sign, 16) ??
                        this.TryParseRadixLiteral(sign, 2) ??
                        this.TryParseRadixLiteral(sign, 8) ??
                        this.TryParseDecimalLiteral(sign);
        if (literal is null)
        {
            throw this.Fail("an expression");
        }

        this.SkipTrivia();
        return literal;
    }

    private int ParseSign()
    {
        if (!this.AtEnd)
        {
            char current = this.source[this.position];
            if (current == '+')
            {
                this.position++;
                return 1;
            }

            if (current == '-')
            {
                this.position++;
                return -1;
            }
        }

        return 1;
    }

    /// <summary>
    ///     <c>0x</c>/<c>0b</c>/<c>0o</c> (either case) followed by at least one digit of that radix, with <c>_</c>
    ///     separators and an ignored C-style <c>u</c>/<c>l</c> suffix. Restores the cursor when the spelling does
    ///     not match so the next radix (or the decimal form) can be tried from the same place.
    /// </summary>
    private Expr? TryParseRadixLiteral(int sign, int radix)
    {
        char marker = radix switch
        {
            16 => 'x',
            2 => 'b',
            _ => 'o',
        };
        int start = this.position;
        if (this.position + 1 >= this.source.Length ||
            this.source[this.position] != '0' ||
            (this.source[this.position + 1] | 0x20) != marker)
        {
            return null;
        }

        this.position += 2;
        int digitsStart = this.position;
        int digitCount = this.SkipDigitRun(radix);
        if (digitCount == 0)
        {
            this.position = start;
            return null;
        }

        BigInteger magnitude = ParseMagnitude(this.source.AsSpan(digitsStart, this.position - digitsStart), radix, digitCount);
        this.SkipIntegerSuffix();
        return CreateRadixLiteral(sign, magnitude);
    }

    private Expr? TryParseDecimalLiteral(int sign)
    {
        int start = this.position;
        int digitCount = this.SkipDigitRun(10);
        if (digitCount == 0)
        {
            this.position = start;
            return null;
        }

        BigInteger magnitude = ParseMagnitude(this.source.AsSpan(start, this.position - start), 10, digitCount);
        this.SkipIntegerSuffix();
        return new Literal(sign * magnitude);
    }

    /// <summary>Advances over digits and separators of the radix and returns how many real digits were seen.</summary>
    private int SkipDigitRun(int radix)
    {
        int digitCount = 0;
        while (!this.AtEnd)
        {
            char current = this.source[this.position];
            if (current == '_')
            {
                this.position++;
                continue;
            }

            if (DigitValue(current, radix) < 0)
            {
                break;
            }

            digitCount++;
            this.position++;
        }

        return digitCount;
    }

    private void SkipIntegerSuffix()
    {
        while (!this.AtEnd && (this.source[this.position] | 0x20) is 'u' or 'l')
        {
            this.position++;
        }
    }

    private static int DigitValue(char character, int radix)
    {
        int value;
        if (character is >= '0' and <= '9')
        {
            value = character - '0';
        }
        else if ((character | 0x20) is >= 'a' and <= 'f')
        {
            // Letters count 10..15; the range check below admits them for radix 16 only.
            value = (character | 0x20) - 'a' + 10;
        }
        else
        {
            return -1;
        }

        return value < radix ? value : -1;
    }

    private static string NormalizeDigits(ReadOnlySpan<char> run, int digitCount)
    {
        return string.Create(digitCount, run.ToString(), static (destination, text) =>
        {
            int index = 0;
            foreach (char character in text)
            {
                if (character != '_')
                {
                    destination[index++] = character;
                }
            }
        });
    }

    /// <summary>Accumulates the digit run; the common short case stays in a <see cref="ulong"/>.</summary>
    private static BigInteger ParseMagnitude(ReadOnlySpan<char> run, int radix, int digitCount)
    {
        int safeDigits = radix switch
        {
            2 => 63,
            8 => 21,
            10 => 18,
            _ => 15,
        };

        // Stryker disable once Equality : a run of exactly safeDigits digits decodes identically on both paths; the boundary is a performance choice.
        if (digitCount <= safeDigits)
        {
            ulong value = 0;
            foreach (char character in run)
            {
                if (character != '_')
                {
                    value = (value * (uint)radix) + (uint)DigitValue(character, radix);
                }
            }

            return new BigInteger(value);
        }

        BigInteger result = BigInteger.Zero;
        foreach (char character in run)
        {
            if (character != '_')
            {
                result = (result * radix) + DigitValue(character, radix);
            }
        }

        return result;
    }

    /// <summary>
    ///     Preserves the established 32-bit two's-complement projection for non-decimal expressions while retaining
    ///     the unsigned mathematical spelling for width-aware enum evaluation.
    /// </summary>
    private static Literal CreateRadixLiteral(int sign, BigInteger magnitude)
    {
        BigInteger exact = sign * magnitude;
        BigInteger projected = exact;
        if (magnitude <= uint.MaxValue)
        {
            projected = sign * new BigInteger(unchecked((int)(uint)magnitude));
        }

        return new Literal(exact, projected);
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
                return;
            }

            this.position++;
        }
    }

    private bool StartsWith(string text)
    {
        return this.source.AsSpan(this.position).StartsWith(text, StringComparison.Ordinal);
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

        var identifier = new Identifier(this.source.Substring(start, this.position - start));
        this.SkipTrivia();
        return identifier;
    }

    private Identifier ExpectIdentifier()
    {
        return this.TryParseIdentifier() ?? throw this.Fail("an identifier");
    }

    private CStructLayoutException Fail(string expected)
    {
        int line = 1;
        int column = 1;
        int end = Math.Min(this.position, this.source.Length);
        for (int index = 0; index < end; index++)
        {
            if (this.source[index] == '\n')
            {
                line++;
                column = 1;
            }
            else
            {
                column++;
            }
        }

        string found = this.AtEnd
                           ? "end of input"
                           : $"'{DescribeCharacter(this.source[this.position])}'";
        return new CStructLayoutException(
            $"{SyntaxErrorPrefix}unexpected {found} at line {line}, column {column}; expected {expected}.");
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
