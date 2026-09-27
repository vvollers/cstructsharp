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

/// <summary>The top-level declarations: structs and unions, typedefs, and enums and flags.</summary>
internal sealed partial class LayoutParser
{
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
            List<Field> fields = this.ParseCompositeBody(isUnion, out ImmutableArray<ConditionalGroup> groups);
            this.ExpectToken('}');
            Identifier name = this.ExpectIdentifier();
            this.ExpectToken(';');
            destination.Add(new Struct(name, fields.ToImmutableList(), isUnion, alignment ?? this.CurrentPack) { Groups = groups });
            return;
        }

        Identifier tag = this.ExpectIdentifier();
        alignment ??= this.TryParseAlignmentOverride();
        if (this.TryToken(';'))
        {
            return;
        }

        this.ExpectToken('{');
        List<Field> members = this.ParseCompositeBody(isUnion, out ImmutableArray<ConditionalGroup> memberGroups);
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

        destination.Add(new Struct(tag, members.ToImmutableList(), isUnion, alignment ?? this.CurrentPack) { Groups = memberGroups });
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
                List<Field> fields = this.ParseCompositeBody(isUnion, out ImmutableArray<ConditionalGroup> groups);
                this.ExpectToken('}');
                Identifier alias = this.ExpectIdentifier();
                var declared = new Struct(alias, fields.ToImmutableList(), isUnion, alignment ?? this.CurrentPack) { Groups = groups };
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
                    List<Field> fields = this.ParseCompositeBody(isUnion, out ImmutableArray<ConditionalGroup> groups);
                    this.ExpectToken('}');
                    var declared = new Struct(tag, fields.ToImmutableList(), isUnion, tagAlignment ?? this.CurrentPack) { Groups = groups };
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

    /// <summary>The members of an enum or flag body, up to its closing brace; the commas between them are optional.</summary>
    /// <returns>The members, in declaration order.</returns>
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
}
