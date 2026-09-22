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
///     The productions mirror the parser-combinator grammar this class replaced one for one, including its
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

    /// <summary>
    ///     A top-level <c>struct</c>/<c>union</c> line: a named body (<c>struct X { } [var] [;]</c>), an anonymous body
    ///     whose trailing name is the type (<c>struct { } name;</c>, the dissect spelling of a typedef), or a forward
    ///     declaration (<c>struct X;</c>) which declares nothing because a self-referential pointer never needed it.
    /// </summary>
    private void ParseTopLevelComposite(string keyword, bool isUnion, List<CStructElement> destination)
    {
        this.SkipKeyword(keyword);
        Expr? alignment = this.TryParseAlignmentOverride();
        if (this.TryToken('{'))
        {
            List<Field> fields = this.ParseCompositeMembers(isUnion);
            this.ExpectToken('}');
            Identifier name = this.ExpectIdentifier();
            this.ExpectToken(';');
            destination.Add(new Struct(name, fields.ToImmutableList(), isUnion, alignment ?? this.CurrentPack));
            return;
        }

        Identifier tag = this.ExpectIdentifier();
        alignment ??= this.TryParseAlignmentOverride();
        if (this.TryToken(';'))
        {
            return;
        }

        this.ExpectToken('{');
        List<Field> members = this.ParseCompositeMembers(isUnion);
        this.ExpectToken('}');

        // `struct X { ... } variable;` declares an object of the type; the variable itself is not part of a layout.
        // A declaration keyword is the next declaration (the semicolon after a body has always been optional).
        if (!this.AtDeclarationKeyword() && this.TryParseIdentifier() is not null)
        {
            this.ExpectToken(';');
        }
        else
        {
            this.TryToken(';');
        }

        destination.Add(new Struct(tag, members.ToImmutableList(), isUnion, alignment ?? this.CurrentPack));
    }

    private Expr? CurrentPack => this.packStack.Count == 0 ? null : this.packStack[^1];

    /// <summary>
    ///     <c>typedef</c> spellings, tried in order: an anonymous or tagged composite body followed by one or more
    ///     declarators (<c>typedef struct _X { } X, *PX;</c>), a tagged body with no declarator at all
    ///     (<c>typedef struct X { };</c>, which declares the tag), a tag alias (<c>typedef struct tag alias;</c>), and
    ///     the plain alias <c>typedef type [&lt;|&gt;] [*...] name [\[N\]...];</c>.
    /// </summary>
    private void ParseTypedef(List<CStructElement> destination)
    {
        this.SkipKeyword("typedef");
        int afterKeyword = this.position;
        if (this.StartsWithKeyword("union") || this.StartsWithKeyword("struct"))
        {
            bool isUnion = this.source[this.position] == 'u';
            this.SkipKeyword(isUnion ? "union" : "struct");
            int afterCompositeKeyword = this.position;

            // Anonymous form: the alignment override (if any) sits directly after the keyword and `{` follows.
            Expr? alignment = this.TryParseAlignmentOverride();
            if (this.TryToken('{'))
            {
                List<Field> fields = this.ParseCompositeMembers(isUnion);
                this.ExpectToken('}');
                Identifier alias = this.ExpectIdentifier();
                var declared = new Struct(alias, fields.ToImmutableList(), isUnion, alignment ?? this.CurrentPack);
                destination.Add(new Typedef(alias, declared));
                this.ParseTypedefDeclaratorTail(alias, destination);
                return;
            }

            // Tagged form: `typedef struct Tag [@align(N)] { ... } [;] Alias [, *Alias2 ...];` - committed once the
            // brace is seen, because the plain alias form can never accept a `{` after the tag.
            this.position = afterCompositeKeyword;
            Identifier? tag = this.TryParseIdentifier();
            if (tag is not null)
            {
                Expr? tagAlignment = this.TryParseAlignmentOverride();
                if (this.TryToken('{'))
                {
                    List<Field> fields = this.ParseCompositeMembers(isUnion);
                    this.ExpectToken('}');
                    var declared = new Struct(tag, fields.ToImmutableList(), isUnion, tagAlignment ?? this.CurrentPack);
                    if (this.AtDeclarationKeyword() || (this.TryToken(';') && !this.AtBareIdentifierStatement()))
                    {
                        // `typedef struct NAME { ... };` - the tag is the only name (the semicolon is as optional
                        // as after any other body when the next declaration follows directly).
                        destination.Add(declared);
                        return;
                    }

                    Identifier alias = this.ExpectIdentifier();
                    destination.Add(new Typedef(alias, declared));
                    this.ParseTypedefDeclaratorTail(alias, destination);
                    return;
                }

                // `typedef struct tag alias;` - an alias of an already declared (or later declared) tag.
                Identifier? tagAlias = this.TryParseIdentifier();
                if (tagAlias is not null)
                {
                    this.ExpectToken(';');
                    destination.Add(new Typedef(tagAlias, new Identifier(tag.Name)) { TypeKeywordHint = isUnion ? "union" : "struct", });
                    return;
                }
            }

            this.position = afterKeyword;
        }
        else if (this.StartsWithKeyword("enum") || this.StartsWithKeyword("flag"))
        {
            this.ParseTypedefEnum(destination);
            return;
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

        // `typedef unsigned long long name;` - the remaining words before the alias belong to the type spelling.
        var words = new List<Identifier> { new(typeName), };
        while (true)
        {
            int stars = 0;
            while (this.TryToken('*'))
            {
                stars++;
            }

            Identifier word = this.ExpectIdentifier();
            List<Expr?> dimensions = this.ParseArrayDimensions();
            if (stars > 0 || dimensions.Count > 0 || this.AtEnd || this.source[this.position] is ';' or ',')
            {
                string joined = string.Join(" ", words.Select(item => item.Name));
                this.AddPlainTypedef(destination, joined, stars, word, dimensions);
                while (this.TryToken(','))
                {
                    int moreStars = 0;
                    while (this.TryToken('*'))
                    {
                        moreStars++;
                    }

                    Identifier more = this.ExpectIdentifier();
                    this.AddPlainTypedef(destination, joined, moreStars, more, this.ParseArrayDimensions());
                }

                this.ExpectToken(';');
                return;
            }

            words.Add(word);
        }
    }

    /// <summary>
    ///     <c>typedef enum [Tag] [: type] { ... } Alias [, *Alias2 ...];</c> (and the <c>flag</c> spelling): a tagged
    ///     body declares the tag and aliases it, an anonymous body is the alias's own enum, and
    ///     <c>typedef enum Tag Alias;</c> aliases an enum declared elsewhere.
    /// </summary>
    private void ParseTypedefEnum(List<CStructElement> destination)
    {
        bool isFlag = this.source[this.position] == 'f';
        this.SkipKeyword(isFlag ? "flag" : "enum");
        Identifier? tag = this.TryParseIdentifier();
        if (tag is not null && !this.AtEnd && this.source[this.position] is not ('{' or ':'))
        {
            Identifier tagAlias = this.ExpectIdentifier();
            this.ExpectToken(';');
            destination.Add(new Typedef(tagAlias, new Identifier(tag.Name)) { TypeKeywordHint = "enum", });
            return;
        }

        Identifier? declaredType = this.ParseEnumStorage();
        this.ExpectToken('{');
        List<EnumValue> values = this.ParseEnumMembers();
        this.ExpectToken('}');
        Identifier type = ResolveEnumStorage(declaredType, values);
        if (tag is not null && (this.AtDeclarationKeyword() || (this.TryToken(';') && !this.AtBareIdentifierStatement())))
        {
            destination.Add(CStructSharpEnum.CreateUnevaluated(tag, values.ToImmutableArray(), type, isFlag));
            return;
        }

        Identifier alias = this.ExpectIdentifier();
        if (tag is null || tag.Name == alias.Name)
        {
            // An anonymous body is the alias's own enum; an alias equal to the tag never collides with itself.
            destination.Add(CStructSharpEnum.CreateUnevaluated(alias, values.ToImmutableArray(), type, isFlag));
        }
        else
        {
            destination.Add(CStructSharpEnum.CreateUnevaluated(tag, values.ToImmutableArray(), type, isFlag));
            destination.Add(new Typedef(alias, new Identifier(tag.Name)) { TypeKeywordHint = "enum", });
        }

        this.ParseTypedefDeclaratorTail(alias, destination);
    }

    /// <summary>One plain-typedef declarator: pointer depth, an optional fixed array shape, and the alias name.</summary>
    private void AddPlainTypedef(List<CStructElement> destination, string typeName, int stars, Identifier alias, List<Expr?> dimensions)
    {
        IReadOnlyList<Expr> shape = Field.NoArray;
        if (dimensions.Count > 0)
        {
            foreach (Expr? dimension in dimensions)
            {
                if (dimension is null)
                {
                    throw new CStructLayoutException(SyntaxErrorPrefix + "A typedef array needs a count in every dimension.");
                }
            }

            shape = BuildArrayCount(dimensions);
        }

        destination.Add(new Typedef(alias, new Identifier(typeName + new string('*', stars))) { ArrayShape = shape, });
    }

    /// <summary>After the first alias of a composite typedef: <c>[, [*...] alias]* ;</c>, each extra alias naming the first one.</summary>
    private void ParseTypedefDeclaratorTail(Identifier firstAlias, List<CStructElement> destination)
    {
        while (this.TryToken(','))
        {
            int stars = 0;
            while (this.TryToken('*'))
            {
                stars++;
            }

            Identifier alias = this.ExpectIdentifier();
            destination.Add(new Typedef(alias, new Identifier(firstAlias.Name + new string('*', stars))));
        }

        this.ExpectToken(';');
    }

    /// <summary>Whether the cursor is at <c>identifier ;</c> - the historical <c>typedef struct tag { }; alias;</c> spelling.</summary>
    private bool AtBareIdentifierStatement()
    {
        if (!this.IsIdentifierStart(this.position))
        {
            return false;
        }

        int start = this.position;
        _ = this.TryParseIdentifier();
        bool bare = !this.AtEnd && this.source[this.position] == ';';
        this.position = start;
        return bare;
    }

    private List<Field> ParseCompositeMembers(bool isUnion)
    {
        return isUnion ? this.ParseUnionMembers() : this.ParseStructMembers();
    }

    /// <summary><c>enum Name [: type] { members };</c> — the semicolon is required.</summary>
    private void ParseEnum(bool isFlag, List<CStructElement> destination)
    {
        this.SkipKeyword(isFlag ? "flag" : "enum");
        Identifier? name = this.TryParseIdentifier();
        Identifier? declaredType = this.ParseEnumStorage();
        this.ExpectToken('{');
        List<EnumValue> values = this.ParseEnumMembers();
        this.ExpectToken('}');
        this.ExpectToken(';');
        if (name is not null)
        {
            destination.Add(CStructSharpEnum.CreateUnevaluated(name, values.ToImmutableArray(), ResolveEnumStorage(declaredType, values), isFlag));
            return;
        }

        // An anonymous enum (`enum { A = 3, B };`) declares no type; its members are integer constants, exactly as
        // C and dissect treat them. An omitted value is the previous member plus one (or, for a flag, the next bit
        // above every literal seen so far).
        Identifier? previous = null;
        BigInteger highestBitSeen = BigInteger.Zero;
        foreach (EnumValue member in values)
        {
            Expr value;
            if (!ReferenceEquals(member.Value, NoneExpr.Instance))
            {
                value = member.Value;
            }
            else if (isFlag)
            {
                value = new Literal(highestBitSeen.IsZero ? BigInteger.One : BigInteger.One << (int)highestBitSeen.GetBitLength());
            }
            else
            {
                value = previous is null ? new Literal(0) : new BinaryOp(BinaryOperatorType.Add, previous, new Literal(1));
            }

            if (isFlag)
            {
                if (value is not Literal literal)
                {
                    throw new CStructLayoutException(SyntaxErrorPrefix + "An anonymous flag member needs a literal value: " + member.Name.Name);
                }

                if (literal.ExactValue > highestBitSeen)
                {
                    highestBitSeen = literal.ExactValue;
                }
            }

            destination.Add(new Defines(member.Name, value));
            previous = member.Name;
        }
    }

    /// <summary>
    ///     The optional <c>: type</c> backing; the spelling may be several words (<c>unsigned int</c>), like a field
    ///     type. <see langword="null"/> means the default applies once the members are known.
    /// </summary>
    private Identifier? ParseEnumStorage()
    {
        return this.TryToken(':') ? (Identifier)this.ParseTypeArgument() : this.defaultEnumStorage;
    }

    /// <summary>
    ///     The storage of an enum declared without one: the configured spelling, or the rule C compilers apply -
    ///     an <c>int</c>-sized 32 bits, unsigned (so <c>0x80000000</c> fits, and dissect's <c>uint32</c> default is
    ///     matched) unless a member is written as a negative number.
    /// </summary>
    private static Identifier ResolveEnumStorage(Identifier? declared, List<EnumValue> values)
    {
        if (declared is not null)
        {
            return declared;
        }

        foreach (EnumValue member in values)
        {
            if (member.Value is UnaryOp { Type: UnaryOperatorType.Neg, Expr: Literal, } or Literal { ExactValue.Sign: < 0, })
            {
                return Identifier.INT32;
            }
        }

        return Identifier.UINT32;
    }

    private List<EnumValue> ParseEnumMembers()
    {
        var values = new List<EnumValue>();
        this.SkipWhitespace();
        if (!this.IsIdentifierPart(this.position))
        {
            return values;
        }

        values.Add(this.ParseEnumMember());
        while (true)
        {
            // The comma is optional: a value can never be followed by a name, so a member that starts on the next
            // line (the dissect and Windows-header habit) is unambiguous, and a trailing comma is allowed as in C.
            _ = this.TryToken(',');
            if (!this.IsIdentifierPart(this.position))
            {
                break;
            }

            values.Add(this.ParseEnumMember());
        }

        return values;
    }

    /// <summary>One member: an identifier optionally followed by <c>=</c>, whitespace, and an expression.</summary>
    private EnumValue ParseEnumMember()
    {
        this.SkipWhitespace();
        Identifier name = this.ParseEnumMemberName();
        if (this.TryChar('='))
        {
            this.SkipWhitespace();
            return new EnumValue(name, this.ParseExpr());
        }

        return new EnumValue(name);
    }

    /// <summary>
    ///     An enum member name may start with, or even consist of, digits (<c>32BIT_MACHINE</c>, or <c>0</c> in a
    ///     character-code enum, as Windows headers and dissect allow); everywhere else an identifier still starts
    ///     with a letter or underscore.
    /// </summary>
    private Identifier ParseEnumMemberName()
    {
        if (this.IsIdentifierStart(this.position))
        {
            return this.ExpectIdentifier();
        }

        int start = this.position;
        while (this.IsIdentifierPart(this.position))
        {
            this.position++;
        }

        if (this.position == start)
        {
            throw this.Fail("an enum member name");
        }

        var name = new Identifier(this.source[start..this.position]) { SourceOffset = start, };
        this.SkipTrivia();
        return name;
    }

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
            else if (this.TryStartInlineComposite(out bool isUnion, out Expr? alignment, out Identifier? tag))
            {
                this.ParseInlineComposite(isUnion, alignment, tag, fields);
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

    /// <summary>A union body: plain fields and inline composites (a union member may itself be an inline struct or union), never conditionals.</summary>
    private List<Field> ParseUnionMembers()
    {
        var fields = new List<Field>();
        while (true)
        {
            if (this.TryStartInlineComposite(out bool isUnion, out Expr? alignment, out Identifier? tag))
            {
                this.ParseInlineComposite(isUnion, alignment, tag, fields);
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

    /// <summary>
    ///     Reports whether <c>struct</c>/<c>union</c> begins an inline composite (the keyword, an optional alignment
    ///     override, then <c>{</c>). When it does, the cursor is left after the brace; otherwise it is restored so
    ///     the same text parses as a field with a <c>struct</c>/<c>union</c> keyword hint.
    /// </summary>
    private bool TryStartInlineComposite(out bool isUnion, out Expr? alignment, out Identifier? tag)
    {
        alignment = null;
        isUnion = false;
        tag = null;
        string keyword;
        if (this.StartsWithKeyword("struct"))
        {
            keyword = "struct";
        }
        else if (this.StartsWithKeyword("union"))
        {
            keyword = "union";
            isUnion = true;
        }
        else
        {
            return false;
        }

        int start = this.position;
        this.SkipKeyword(keyword);
        alignment = this.TryParseAlignmentOverride();
        if (this.TryToken('{'))
        {
            return true;
        }

        // `struct tag { ... } [member];` - a tagged body declares the tag globally (hoisted) and, with a member name,
        // a field of it; without one its fields are promoted, as dissect and MSVC's anonymous-member extension do.
        tag = this.TryParseIdentifier();
        if (tag is not null)
        {
            alignment ??= this.TryParseAlignmentOverride();
            if (this.TryToken('{'))
            {
                return true;
            }
        }

        this.position = start;
        alignment = null;
        isUnion = false;
        tag = null;
        return false;
    }

    /// <summary>
    ///     <c>struct|union [tag] [@align(N)] { members } [name];</c> — cursor starts after the opening brace; no name
    ///     means an anonymous promoted member. A tag is hoisted as a global declaration; its named member is then an
    ///     ordinary field of that type (with every declarator form), and its unnamed member is the promoted body,
    ///     parsed a second time so the two declarations share no syntax nodes.
    /// </summary>
    private void ParseInlineComposite(bool isUnion, Expr? alignment, Identifier? tag, List<Field> destination)
    {
        int bodyStart = this.position;
        List<Field> fields = this.ParseCompositeMembers(isUnion);
        this.ExpectToken('}');
        if (tag is null)
        {
            Identifier name = this.TryParseIdentifier() ?? new Identifier(string.Empty);
            this.ExpectToken(';');
            destination.Add(new Struct(name, fields.ToImmutableList(), isUnion, alignment));
            return;
        }

        this.hoisted.Add(new Struct(tag, fields.ToImmutableList(), isUnion, alignment ?? this.CurrentPack));
        if (this.TryToken(';'))
        {
            int resume = this.position;
            int hoistedCount = this.hoisted.Count;
            this.position = bodyStart;
            List<Field> promoted = this.ParseCompositeMembers(isUnion);
            this.position = resume;

            // The second pass hoists the body's own tags again; the first pass already declared them.
            this.hoisted.RemoveRange(hoistedCount, this.hoisted.Count - hoistedCount);
            destination.Add(new Struct(new Identifier(string.Empty), promoted.ToImmutableList(), isUnion, alignment));
            return;
        }

        destination.AddRange(this.ParseFieldDeclaration(tag, isUnion ? "union" : "struct"));
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
    ///     with a bit width is an anonymous bitfield whose one word is the type.
    /// </summary>
    private List<Field> ParseFieldDeclaration(Identifier? leadingType = null, string? typeKeywordHint = null)
    {
        if (leadingType is null)
        {
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
        }

        List<Identifier> words = this.ParseQualifiedWords();
        if (leadingType is not null)
        {
            // The type was declared in place (`struct tag { ... } member;`); only the declarators remain.
            words.Insert(0, leadingType);
        }

        if (words.Count == 0)
        {
            throw this.Fail("a field type");
        }

        if (this.TryStartFunctionPointer(out Identifier? functionPointerName))
        {
            // `return_type (*name)(parameters)`: the value is an opaque code address of pointer width, stored
            // exactly as a `void *`; the signature is recognized and discarded.
            this.ExpectToken(';');
            return [MakeField(new Identifier("void"), null, functionPointerName, 1, EmptyDimensions, null, null, null),];
        }

        List<Expr?> dimensions = this.ParseArrayDimensions();
        Expr? bitSize = this.TryParseBitSize();
        (Expr? alignmentOverride, Expr? offsetAssertion) = this.ParsePlacementSuffix();

        bool firstDeclaratorIsAnonymous = words.Count == 1 && bitSize is not null;
        Identifier typeIdentifier = firstDeclaratorIsAnonymous ? new Identifier(words[0].Name) { SourceOffset = words[0].SourceOffset, } : BuildTypeIdentifier(words);
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

    /// <summary>Recognizes <c>(*name)(...)</c> after a return type; the cursor is left after the parameter list, or restored on a miss.</summary>
    private bool TryStartFunctionPointer(out Identifier? name)
    {
        name = null;
        if (this.AtEnd || this.source[this.position] != '(')
        {
            return false;
        }

        int start = this.position;
        this.position++;
        this.SkipTrivia();
        if (!this.TryToken('*'))
        {
            this.position = start;
            return false;
        }

        name = this.TryParseIdentifier();
        if (name is null || !this.TryToken(')') || this.AtEnd || this.source[this.position] != '(')
        {
            this.position = start;
            name = null;
            return false;
        }

        int depth = 0;
        while (!this.AtEnd)
        {
            char current = this.source[this.position++];
            if (current == '(')
            {
                depth++;
            }
            else if (current == ')' && --depth == 0)
            {
                this.SkipTrivia();
                return true;
            }
        }

        throw this.Fail("')'");
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
        // `_` is the conventional name of a reserved/padding field (dissect headers use it, several times per
        // struct). Such a field is unnamed, like an anonymous bitfield: read and skipped, written as zeroes, never
        // a member of the result, and free to repeat.
        return new Field(
            type,
            name is null or { Name: "_", } ? new Identifier(string.Empty) : name,
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

        return new Identifier(builder.ToString()) { SourceOffset = words.Count > 0 ? words[0].SourceOffset : -1, };
    }

    /// <summary>
    ///     Builds one declarator's <see cref="Field.ArrayCount"/> from zero or more parsed bracket pairs,
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
    ///     <c>restrict</c>) that are consumed and discarded. A word is an extended identifier: letters,
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

            words.Add(new Identifier(this.source.Substring(start, this.position - start)) { SourceOffset = start, });
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
    ///     A declarator carries at most one trailing placement suffix: <c>@align(N)</c> (alignment override)
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
    private const int BitwiseXorLevel = 3;
    private const int BitwiseAndLevel = 4;
    private const int EqualityLevel = 5;
    private const int RelationalLevel = 6;
    private const int ShiftLevel = 7;
    private const int AdditiveLevel = 8;
    private const int MultiplicativeLevel = 9;
    private const int UnaryLevel = 10;

    /// <summary>A conditional expression: the C ternary at the lowest precedence, right-associative.</summary>
    private Expr ParseExpr()
    {
        Expr condition = this.ParseBinary(LogicalOrLevel);
        if (this.AtEnd || this.source[this.position] != '?')
        {
            return condition;
        }

        this.position++;
        this.SkipTrivia();
        Expr whenTrue = this.ParseExpr();
        this.ExpectToken(':');
        Expr whenFalse = this.ParseExpr();
        return new ConditionalExpr(condition, whenTrue, whenFalse);
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
            ('^', _) => (BinaryOperatorType.Xor, BitwiseXorLevel, 1),
            ('&', _) => (BinaryOperatorType.And, BitwiseAndLevel, 1),
            ('<', _) => (BinaryOperatorType.Less, RelationalLevel, 1),
            ('>', _) => (BinaryOperatorType.Greater, RelationalLevel, 1),
            ('-', _) => (BinaryOperatorType.Minus, AdditiveLevel, 1),
            ('+', _) => (BinaryOperatorType.Add, AdditiveLevel, 1),
            ('/', _) => (BinaryOperatorType.Div, MultiplicativeLevel, 1),
            ('%', _) => (BinaryOperatorType.Mod, MultiplicativeLevel, 1),
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
            bool typeArguments = expression is Identifier { Name: "sizeof" or "offsetof", PointerDepth: 0, };
            if (!this.TryToken(')'))
            {
                arguments.Add(typeArguments ? this.ParseTypeArgument() : this.ParseExpr());
                while (this.TryToken(','))
                {
                    arguments.Add(typeArguments ? this.ParseTypeArgument() : this.ParseExpr());
                }

                this.ExpectToken(')');
            }

            expression = new Call(expression, arguments.ToImmutable());
        }

        return expression;
    }

    /// <summary>A <c>sizeof</c>/<c>offsetof</c> argument: a type spelling (words and pointer stars, e.g. <c>unsigned int</c>, <c>uint8*</c>) or a field name.</summary>
    /// <returns>A type identifier whose pointer stars follow all of its name words.</returns>
    /// <exception cref="CStructLayoutException">No type or field name starts at the current position.</exception>
    private Expr ParseTypeArgument()
    {
        var builder = new StringBuilder();
        while (this.IsIdentifierStart(this.position))
        {
            Identifier word = this.ExpectIdentifier();
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(word.Name);
        }

        if (builder.Length == 0)
        {
            throw this.Fail("a type or field name");
        }

        // Pointer stars are a suffix. A later word belongs to an invalid expression, not to the type name;
        // leave it for the caller's delimiter check instead of silently joining h*o into a pointer to ho.
        while (this.TryToken('*'))
        {
            builder.Append('*');
        }

        return new Identifier(builder.ToString());
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
            // `Enum.Member` names one member of a named enum or flag (the compiler publishes it as a constant);
            // `hdr.n` or `a.b.n` names a nested struct's field, published under the path while it is read.
            if (!this.AtEnd && this.source[this.position] == '.' && this.IsIdentifierStart(this.position + 1))
            {
                string qualified = identifier.Name;
                while (!this.AtEnd && this.source[this.position] == '.' && this.IsIdentifierStart(this.position + 1))
                {
                    this.position++;
                    qualified += "." + this.ExpectIdentifier().Name;
                }

                this.usesQualifiedIdentifiers = true;
                return new Identifier(qualified);
            }

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
        var destination = new char[digitCount];
        int index = 0;
        foreach (char character in run)
        {
            if (character != '_')
            {
                destination[index++] = character;
            }
        }

        return new string(destination);
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

    private CStructLayoutException Fail(string expected)
    {
        (int line, int column) = LocatePosition(this.source, this.position);
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
