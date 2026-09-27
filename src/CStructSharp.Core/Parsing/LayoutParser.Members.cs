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

/// <summary>The members of a struct or union: fields and their declarators and suffixes, inline composites, and <c>if</c>/<c>switch</c> groups.</summary>
internal sealed partial class LayoutParser
{
    /// <summary>A struct or union body up to its closing brace, with the <c>if</c>/<c>switch</c> groups declared in it.</summary>
    /// <param name="isUnion">Whether the body is a union's.</param>
    /// <param name="groups">The body's groups, nested ones included, in the order they close.</param>
    /// <returns>The members, in declaration order.</returns>
    private List<Field> ParseCompositeBody(bool isUnion, out ImmutableArray<ConditionalGroup> groups)
    {
        List<ConditionalGroup>? outer = this.bodyGroups;
        var own = new List<ConditionalGroup>();
        this.bodyGroups = own;
        try
        {
            List<Field> fields = this.ParseCompositeMembers(isUnion);
            groups = own.ToImmutableArray();
            return fields;
        }
        finally
        {
            this.bodyGroups = outer;
        }
    }

    /// <summary>
    ///     A struct or union body up to its closing brace: fields, inline structs and unions, and - in a struct only -
    ///     <c>if</c>/<c>switch</c> groups, whose members become conditional fields. A union member may itself be an
    ///     inline struct or union, but a union has no conditional members.
    /// </summary>
    /// <param name="isUnion">Whether the body is a union's.</param>
    /// <returns>The members, in declaration order.</returns>
    private List<Field> ParseCompositeMembers(bool isUnion)
    {
        var fields = new List<Field>();
        while (true)
        {
            if (!isUnion && this.StartsWithKeywordThen("if", '('))
            {
                this.ParseConditional(fields);
            }
            else if (!isUnion && this.StartsWithKeywordThen("switch", '('))
            {
                this.ParseSwitch(fields);
            }
            else if (this.TryStartInlineComposite(out bool memberIsUnion, out Expr? alignment, out Identifier? tag))
            {
                this.ParseInlineComposite(memberIsUnion, alignment, tag, fields);
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
        List<Field> fields = this.ParseCompositeBody(isUnion, out ImmutableArray<ConditionalGroup> groups);
        this.ExpectToken('}');
        if (tag is null)
        {
            Identifier name = this.TryParseIdentifier() ?? new Identifier(string.Empty);
            this.ExpectToken(';');
            destination.Add(new Struct(name, fields.ToImmutableList(), isUnion, alignment) { Groups = groups });
            return;
        }

        this.hoisted.Add(new Struct(tag, fields.ToImmutableList(), isUnion, alignment ?? this.CurrentPack) { Groups = groups });
        if (this.TryToken(';'))
        {
            int resume = this.position;
            int hoistedCount = this.hoisted.Count;
            this.position = bodyStart;
            List<Field> promoted = this.ParseCompositeBody(isUnion, out ImmutableArray<ConditionalGroup> promotedGroups);
            this.position = resume;

            // The second pass hoists the body's own tags again; the first pass already declared them.
            this.hoisted.RemoveRange(hoistedCount, this.hoisted.Count - hoistedCount);
            destination.Add(new Struct(new Identifier(string.Empty), promoted.ToImmutableList(), isUnion, alignment) { Groups = promotedGroups });
            return;
        }

        destination.AddRange(this.ParseFieldDeclaration(tag, isUnion ? "union" : "struct"));
    }

    /// <summary><c>if (condition) { members } [else { members }]</c>: one group whose arms' members are placed in the body.</summary>
    private void ParseConditional(List<Field> destination)
    {
        this.SkipKeyword("if");
        this.ExpectToken('(');
        Expr condition = this.ParseExpr();
        this.ExpectToken(')');
        this.ExpectToken('{');
        List<Field> yes = this.ParseCompositeMembers(isUnion: false);
        this.ExpectToken('}');
        List<Field> no = [];
        if (this.TryKeyword("else"))
        {
            this.ExpectToken('{');
            no = this.ParseCompositeMembers(isUnion: false);
            this.ExpectToken('}');
        }

        var group = new ConditionalGroup(condition);
        this.bodyGroups!.Add(group);
        ApplyCondition(yes, group, 1, destination);
        ApplyCondition(no, group, 0, destination);
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
            List<Field> armFields = this.ParseCompositeMembers(isUnion: false);
            this.ExpectToken('}');
            arms.Add((tag, armFields));
        }

        List<Field> fallback = [];
        if (this.TryKeyword("default"))
        {
            this.ExpectToken(':');
            this.ExpectToken('{');
            fallback = this.ParseCompositeMembers(isUnion: false);
            this.ExpectToken('}');
        }

        this.ExpectToken('}');

        var labels = new Expr[arms.Count];
        for (int index = 0; index < arms.Count; index++)
        {
            labels[index] = arms[index].Tag;
        }

        // The group keeps every label, an empty arm's too, so normalization rejects duplicate and non-constant labels
        // whatever the data selects.
        var group = new ConditionalGroup(selector, labels);
        this.bodyGroups!.Add(group);
        var tags = new HashSet<Expr>();
        for (int armIndex = 0; armIndex < arms.Count; armIndex++)
        {
            (Expr tag, List<Field> armFields) = arms[armIndex];
            if (!tags.Add(tag))
            {
                throw new CStructLayoutException("Duplicate switch case.");
            }

            ApplyCondition(armFields, group, armIndex, destination);
        }

        ApplyCondition(fallback, group, -1, destination);
    }

    /// <summary>
    ///     Makes the members of one <c>if</c>/<c>switch</c> arm conditional: each gets the arm as its outermost branch,
    ///     before any branch it already has from a nested group.
    /// </summary>
    /// <param name="fields">The arm's members.</param>
    /// <param name="group">The <c>if</c> or <c>switch</c> group.</param>
    /// <param name="arm">The arm's index in the group.</param>
    /// <param name="destination">The enclosing body's members, which receive the arm's.</param>
    private static void ApplyCondition(List<Field> fields, ConditionalGroup group, int arm, List<Field> destination)
    {
        var branch = new ConditionalBranch(group, arm);
        foreach (Field field in fields)
        {
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
        (Expr? alignmentOverride, Expr? offsetAssertion, Expr? pointerCount) = this.ParseDeclaratorSuffixes();

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
                offsetAssertion,
                pointerCount),
        };

        while (this.TryToken(','))
        {
            List<Identifier> declaratorWords = this.ParseQualifiedWords();
            List<Expr?> declaratorDimensions = this.ParseArrayDimensions();
            Expr? declaratorBitSize = this.TryParseBitSize();
            (Expr? declaratorAlignment, Expr? declaratorOffset, Expr? declaratorCount) = this.ParseDeclaratorSuffixes();
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
                    declaratorOffset,
                    declaratorCount));
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

    /// <summary>Builds one parsed declarator, carrying its placement suffix and optional pointer count.</summary>
    private static Field MakeField(
        Identifier type,
        string? typeKeywordHint,
        Identifier? name,
        int pointerDepth,
        List<Expr?> dimensions,
        Expr? bitSize,
        Expr? alignmentOverride,
        Expr? offsetAssertion,
        Expr? pointerCount = null)
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
            offsetAssertion)
        {
            PointerCountExpression = pointerCount,
        };
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

    /// <summary>Consumes one type qualifier (<c>const</c>, <c>volatile</c>, <c>restrict</c>), which the layout ignores.</summary>
    /// <returns>Whether a qualifier was at the cursor.</returns>
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

    /// <summary>A bitfield width: <c>: expression</c>.</summary>
    /// <returns>The width expression, or <see langword="null"/> when the declarator has none.</returns>
    private Expr? TryParseBitSize()
    {
        return this.TryToken(':') ? this.ParseExpr() : null;
    }

    /// <summary>
    ///     A declarator carries at most one trailing placement suffix - <c>@align(N)</c> (alignment override)
    ///     or <c>@N</c> (offset assertion) - and at most one <c>@count(N)</c> pointer element count, in either
    ///     order. The named forms are tried first because an offset assertion may itself start with a name.
    /// </summary>
    private (Expr? AlignmentOverride, Expr? OffsetAssertion, Expr? PointerCount) ParseDeclaratorSuffixes()
    {
        Expr? alignment = null;
        Expr? offset = null;
        Expr? count = null;
        while (!this.AtEnd && this.source[this.position] == '@')
        {
            if (this.TryParseNamedSuffix("@count") is { } parsedCount)
            {
                if (count is not null)
                {
                    throw new CStructLayoutException(SyntaxErrorPrefix + "A declarator can have only one @count suffix.");
                }

                count = parsedCount;
                continue;
            }

            if (alignment is not null || offset is not null)
            {
                // A second placement suffix is not part of the grammar; the declarator must end here.
                break;
            }

            alignment = this.TryParseAlignmentOverride();
            if (alignment is null)
            {
                this.position++;
                this.SkipTrivia();
                offset = this.ParseExpr();
            }
        }

        return (alignment, offset, count);
    }

    /// <summary>
    ///     <c>@align(expression)</c> when present; otherwise the cursor is unchanged. <c>@align</c> not followed by
    ///     <c>(</c> is left for the caller (an offset assertion may name an identifier starting with "align").
    /// </summary>
    private Expr? TryParseAlignmentOverride()
    {
        return this.TryParseNamedSuffix("@align");
    }

    /// <summary>
    ///     <c>keyword(expression)</c> for a named suffix such as <c>@align</c> or <c>@count</c>; otherwise the cursor
    ///     is unchanged. The keyword not followed by <c>(</c> is left for the caller.
    /// </summary>
    private Expr? TryParseNamedSuffix(string keyword)
    {
        if (!this.StartsWith(keyword))
        {
            return null;
        }

        int start = this.position;
        this.position += keyword.Length;
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
}
