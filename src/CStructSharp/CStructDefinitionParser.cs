// ReSharper disable MemberCanBePrivate.Global
namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Numerics;
using CStructSharp.Structure;
using Pidgin;
using Pidgin.Comment;
using Pidgin.Expression;
using static Pidgin.Parser;
using BinaryOperatorType = CStructSharp.Structure.BinaryOperatorType;
using CStructSharpEnum = CStructSharp.Structure.Enum;
using UnaryOperatorType = CStructSharp.Structure.UnaryOperatorType;

/// <summary>
///     Parses the supported C-like layout language into the small model classes used by <see cref="CStruct"/>.
///     Use <see cref="Parser"/> or <see cref="Expr"/> to parse a complete layout or a standalone expression.
/// </summary>
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1201:ElementsMustAppearInTheCorrectOrder", Justification = "custom ordering for clarity")]
internal static class CStructDefinitionParser
{
    public static readonly Parser<char, Func<Expr, Expr, Expr>> Equal = Binary(Tok("==").ThenReturn(BinaryOperatorType.Equal));
    public static readonly Parser<char, Func<Expr, Expr, Expr>> NotEqual = Binary(Tok("!=").ThenReturn(BinaryOperatorType.NotEqual));
    public static readonly Parser<char, Func<Expr, Expr, Expr>> LessOrEqual = Binary(Tok("<=").ThenReturn(BinaryOperatorType.LessOrEqual));
    public static readonly Parser<char, Func<Expr, Expr, Expr>> GreaterOrEqual = Binary(Tok(">=").ThenReturn(BinaryOperatorType.GreaterOrEqual));
    public static readonly Parser<char, Func<Expr, Expr, Expr>> Less = Binary(Tok("<").ThenReturn(BinaryOperatorType.Less));
    public static readonly Parser<char, Func<Expr, Expr, Expr>> Greater = Binary(Tok(">").ThenReturn(BinaryOperatorType.Greater));
    public static readonly Parser<char, Func<Expr, Expr>> LogicalNot = Unary(Tok("!").ThenReturn(UnaryOperatorType.LogicalNot));

    public static readonly Parser<char, Func<Expr, Expr, Expr>> LogicalAnd = Binary(Tok("&&").ThenReturn(BinaryOperatorType.LogicalAnd));
    public static readonly Parser<char, Func<Expr, Expr, Expr>> LogicalOr = Binary(Tok("||").ThenReturn(BinaryOperatorType.LogicalOr));

    public static readonly Parser<char, char> OpenBrace = Tok('{');
    public static readonly Parser<char, char> CloseBrace = Tok('}');
    public static readonly Parser<char, char> Colon = Tok(':');
    public static readonly Parser<char, char> Comma = Tok(',');
    public static readonly Parser<char, char> SemiColon = Tok(';');
    public static readonly Parser<char, string> EnumKeyword = Tok("enum");
    public static readonly Parser<char, string> StructKeyword = Tok("struct");
    public static readonly Parser<char, string> UnionKeyword = Tok("union");
    public static readonly Parser<char, string> TypedefKeyword = Tok("typedef");
    public static readonly Parser<char, string> DefineKeyword = Tok("#define");

    /// <summary>Adds whitespace and comment handling around a parser token.</summary>
    public static Parser<char, T> Tok<T>(Parser<char, T> p)
    {
        return Try(p).Before(Rec(() => SkipWhiteSpacesAndComments));
    }

    /// <summary>Creates a character token that ignores surrounding whitespace and comments.</summary>
    public static Parser<char, char> Tok(char value)
    {
        return Tok(Char(value));
    }

    /// <summary>Creates a text token that ignores surrounding whitespace and comments.</summary>
    public static Parser<char, string> Tok(string value)
    {
        return Tok(String(value));
    }

    public static readonly Parser<char, Func<Expr, Expr, Expr>> Add = Binary(
        Tok("+").ThenReturn(BinaryOperatorType.Add));

    public static readonly Parser<char, Func<Expr, Expr, Expr>> Minus = Binary(
        Tok("-").ThenReturn(BinaryOperatorType.Minus));

    public static readonly Parser<char, Func<Expr, Expr, Expr>> Div = Binary(
        Tok("/").ThenReturn(BinaryOperatorType.Div));

    public static readonly Parser<char, Func<Expr, Expr, Expr>> Mul = Binary(
        Tok("*").ThenReturn(BinaryOperatorType.Mul));

    public static readonly Parser<char, Func<Expr, Expr, Expr>> And = Binary(
        Tok(Char('&').Before(Not(Char('&')))).ThenReturn(BinaryOperatorType.And));

    public static readonly Parser<char, Func<Expr, Expr, Expr>> Or
        = Binary(Tok(Char('|').Before(Not(Char('|')))).ThenReturn(BinaryOperatorType.Or));

    public static readonly Parser<char, Func<Expr, Expr>> Neg = Unary(Tok("-").ThenReturn(UnaryOperatorType.Neg));

    public static readonly Parser<char, Func<Expr, Expr, Expr>> ShiftRight = Binary(
        Tok(">>").ThenReturn(BinaryOperatorType.ShiftRight));

    public static readonly Parser<char, Func<Expr, Expr, Expr>> ShiftLeft = Binary(
        Tok("<<").ThenReturn(BinaryOperatorType.ShiftLeft));

    public static readonly Parser<char, Func<Expr, Expr>> Complement = Unary(
        Tok("~").ThenReturn(UnaryOperatorType.Complement));

    public static readonly Parser<char, int> Sign
        = Char('+').ThenReturn(1).Or(Char('-').ThenReturn(-1)).Or(Parser<char>.Return(1));

    public static readonly Parser<char, string> FlexibleHexDigit = Parser<char>.Token(char.IsAsciiHexDigit).
        Select(c => c.ToString()).
        Or(Char('_').ThenReturn(string.Empty));

    public static readonly Parser<char, string> Digit = FlexibleDigit(10);
    public static readonly Parser<char, string> HexDigit = FlexibleHexDigit;
    public static readonly Parser<char, string> OctalDigit = FlexibleDigit(8);
    public static readonly Parser<char, string> BinaryDigit = FlexibleDigit(2);

    public static readonly Parser<char, string> DigitString = Digit.AtLeastOnce().Select(string.Concat).
        Assert(value => value.Length > 0);

    public static readonly Parser<char, string> HexString = HexDigit.AtLeastOnce().Select(string.Concat).
        Assert(value => value.Length > 0);

    public static readonly Parser<char, string> OctalString = OctalDigit.AtLeastOnce().Select(string.Concat).
        Assert(value => value.Length > 0);

    public static readonly Parser<char, string> BinaryString = BinaryDigit.AtLeastOnce().Select(string.Concat).
        Assert(value => value.Length > 0);

    /// <summary>
    ///     Recognizes and discards a trailing C-style integer-literal suffix (any combination of <c>u</c>/<c>U</c>
    ///     and <c>l</c>/<c>L</c>). Portable's expressions are already exact-integer, so a suffix carries no width
    ///     information and has no effect on the parsed value.
    /// </summary>
    public static readonly Parser<char, Unit> IntegerLiteralSuffix =
        OneOf(Char('u'), Char('U'), Char('l'), Char('L')).SkipMany();

    public static readonly Parser<char, Expr> LiteralBinary = Map(
            (sign, lit) => CreateRadixLiteral(sign, lit),
            Sign,
            CIString("0b").Then(BinaryString.Select(o => ParseBigInteger(o, 2)))).
        Before(IntegerLiteralSuffix).
        Select<Expr>(c => c).
        Labelled("Binary Literal");

    public static readonly Parser<char, Expr> LiteralHex = Map(
            (sign, lit) => CreateRadixLiteral(sign, lit),
            Sign,
            CIString("0x").Then(HexString.Select(o => ParseBigInteger(o, 16)))).
        Before(IntegerLiteralSuffix).
        Select<Expr>(c => c).
        Labelled("Hex Literal");

    public static readonly Parser<char, Expr> LiteralOctal = Map(
            (sign, lit) => CreateRadixLiteral(sign, lit),
            Sign,
            CIString("0o").Then(OctalString.Select(o => ParseBigInteger(o, 8)))).
        Before(IntegerLiteralSuffix).
        Select<Expr>(c => c).
        Labelled("Octal Literal");

    public static readonly Parser<char, Expr> LiteralDecimal = Map(
            (sign, lit) => new Literal(sign * lit),
            Sign,
            DigitString.Assert(o => o.Length > 0).Select(o => BigInteger.Parse(o, CultureInfo.InvariantCulture))).
        Before(IntegerLiteralSuffix).
        Select<Expr>(c => c).
        Labelled("Decimal Literal");

    private static readonly Parser<char, Unit> SkipWhiteSpacesAndComments = OneOf(
            CommentParser.SkipLineComment(Try(String("//"))).Before(SkipWhitespaces),
            CommentParser.SkipBlockComment(Try(String("/*")), String("*/")).Before(SkipWhitespaces)).
        SkipMany().
        Between(SkipWhitespaces);

    public static readonly Parser<char, Expr> Literal = SkipWhiteSpacesAndComments.
        Then(OneOf(Try(LiteralHex), Try(LiteralBinary), Try(LiteralOctal), Try(LiteralDecimal))).
        Before(SkipWhiteSpacesAndComments);

    /// <summary>Parses one digit in the requested base and permits underscores as visual separators.</summary>
    public static Parser<char, string> FlexibleDigit(int @base)
    {
        return Parser<char>.Token(c => c >= '0' && c < '0' + @base).
            Select(c => c.ToString()).
            Or(Char('_').ThenReturn(string.Empty));
    }

    /// <summary>Parses an unsigned arbitrary-precision literal in a non-decimal radix.</summary>
    private static BigInteger ParseBigInteger(string digits, int radix)
    {
        BigInteger result = BigInteger.Zero;
        foreach (char digit in digits)
        {
            int value = char.IsDigit(digit) ? digit - '0' : char.ToUpperInvariant(digit) - 'A' + 10;
            result = (result * radix) + value;
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

    /// <summary>Wraps a parser so it accepts parentheses around its value.</summary>
    public static Parser<char, T> Parenthesised<T>(Parser<char, T> parser)
    {
        return parser.Between(Tok("("), Tok(")"));
    }

    /// <summary>Turns a parsed binary operator into the function used by the expression parser.</summary>
    public static Parser<char, Func<Expr, Expr, Expr>> Binary(Parser<char, BinaryOperatorType> op)
    {
        return op.Select<Func<Expr, Expr, Expr>>(type => (l, r) => new BinaryOp(type, l, r));
    }

    /// <summary>Turns a parsed unary operator into the function used by the expression parser.</summary>
    public static Parser<char, Func<Expr, Expr>> Unary(Parser<char, UnaryOperatorType> op)
    {
        return op.Select<Func<Expr, Expr>>(type => o => new UnaryOp(type, o));
    }

    public static readonly char[] ExtraIdentifierChars = ['*', '>', '<',];

    public static Parser<char, char> ExtendedIdentifierChar { get; } = Parser<char>.
        Token(c => char.IsLetter(c) || ExtraIdentifierChars.Contains(c)).
        Labelled("extended identifier letter");

    public static Parser<char, char> ExtendedIdentifierCharOrDigit { get; } = Parser<char>.
        Token(c => char.IsLetterOrDigit(c) || ExtraIdentifierChars.Contains(c)).
        Labelled("extended identifier letter or digit");

    public static readonly Parser<char, Identifier> Identifier
        = Tok(
                Map(
                    (first, rest) => first + rest,
                    OneOf(Letter, Char('_')),
                    OneOf(LetterOrDigit, Char('_')).ManyString())).
            Select(name => new Identifier(name)).
            Labelled("Identifier");

    public static readonly Parser<char, Identifier> ExtendedIdentifier
        = Tok(
                Map(
                    (first, rest) => first + rest,
                    OneOf(ExtendedIdentifierChar, Char('_')),
                    OneOf(ExtendedIdentifierCharOrDigit, Char('_')).ManyString())).
            Select(name => new Identifier(name)).
            Labelled("Identifier");

    // The order below is the language's precedence order: tighter rows appear before looser rows, and operators in
    // one row deliberately share C-style left-associative precedence.
    public static readonly Parser<char, Expr> Expr = ExpressionParser.Build<char, Expr>(expr => (
            OneOf(Try(Parenthesised(expr)), Try(Identifier.Cast<Expr>()), Try(Literal)), [
                Operator.PostfixChainable(Call(expr)),
                Operator.PrefixChainable(Neg, Complement, LogicalNot),
                Operator.InfixL(Div).And(Operator.InfixL(Mul)),
                Operator.InfixL(Minus).And(Operator.InfixL(Add)),
                Operator.InfixL(ShiftLeft).And(Operator.InfixL(ShiftRight)),
                Operator.InfixL(LessOrEqual).And(Operator.InfixL(GreaterOrEqual)).And(Operator.InfixL(Less)).And(Operator.InfixL(Greater)),
                Operator.InfixL(Equal).And(Operator.InfixL(NotEqual)),
                Operator.InfixL(And),
                Operator.InfixL(Or),
                Operator.InfixL(LogicalAnd),
                Operator.InfixL(LogicalOr),
            ])).
        Labelled("expression");

    /// <summary>Parses a parenthesized argument list after an expression; evaluation later rejects calls until the language supports them.</summary>
    public static Parser<char, Func<Expr, Expr>> Call(Parser<char, Expr> subExpr)
    {
        return Parenthesised(subExpr.Separated(Tok(","))).
            Select<Func<Expr, Expr>>(args => method => new Call(method, [.. args,])).
            Labelled("function call");
    }

    public static readonly Parser<char, CStructElement> Define = Map(
            (name, value) => new Defines(name, value),
            DefineKeyword.Then(SkipWhiteSpacesAndComments).Then(Identifier),
            SkipWhiteSpacesAndComments.Then(Expr).Before(SkipWhiteSpacesAndComments)).
        Select<CStructElement>(s => s);

    public static readonly Parser<char, CStructElement> Typedef = Map(
            (underlyingType, pointerStars, aliasName) => new Typedef(
                aliasName,
                new Identifier(underlyingType.Name + new string('*', pointerStars.Count()))),
            TypedefKeyword.Then(SkipWhiteSpacesAndComments).Then(Map(
                (type, suffix) => suffix.HasValue ? new Identifier(type.Name + suffix.Value) : type,
                Identifier,
                OneOf(Tok('<'), Tok('>')).Optional())),
            Tok('*').Many(),
            SkipWhiteSpacesAndComments.Then(Identifier).Before(SemiColon)).
        Select<CStructElement>(s => s).
        Labelled("Typedef");

    public static Parser<char, EnumValue> EnumValue =>
        Try(
                SkipWhitespaces.Then(
                    Identifier.Bind(o =>
                        Char('=').Then(SkipWhitespaces).Then(Expr).Select(l => new EnumValue(o, l))))).
            Or(SkipWhitespaces.Then(Identifier.Select(l => new EnumValue(l))));

    public static Parser<char, IEnumerable<EnumValue>> EnumValues =>
        SkipWhitespaces.Then(Tok(EnumValue)).Separated(Comma.Before(SkipWhitespaces));

    public static Parser<char, IEnumerable<EnumValue>> EnumValuesInBrackets =>
        EnumValues.Between(OpenBrace, CloseBrace);

    private static readonly Parser<char, Identifier> EnumTypePart
        = SkipWhiteSpacesAndComments.Then(Colon).Then(Identifier);

    public static readonly Parser<char, CStructElement> Enum = EnumKeyword.Then(SkipWhiteSpacesAndComments).
        Then(Identifier).
        Bind(id => SkipWhiteSpacesAndComments.Then(EnumTypePart.Optional()).
            Bind(type => SkipWhiteSpacesAndComments.Then(EnumValuesInBrackets.Before(SemiColon)).
                Select<CStructElement>(o => CStructSharpEnum.CreateUnevaluated(
                    id,
                    [.. o,],
                    type.HasValue ? type.Value : Structure.Identifier.BYTE))));

    public static readonly Parser<char, Maybe<Expr>> Array = Map(
        (_, expr, _) => expr,
        Tok('[').IgnoreResult(),
        Expr.Optional(),
        Tok(']').IgnoreResult());

    /// <summary>
    ///     Builds one declarator's <see cref="Field.ArrayCount"/> from zero or more parsed bracket pairs
    ///     (LANG-05), outermost dimension first. An empty bracket pair (<c>char name[];</c>) is accepted only as
    ///     the sole dimension of a one-dimensional array - the same restriction this language already enforced
    ///     before LANG-05, now stated explicitly for the N-dimensional case rather than being structurally
    ///     impossible to violate.
    /// </summary>
    private static IReadOnlyList<Expr> BuildArrayCount(IReadOnlyList<Maybe<Expr>> dimensions)
    {
        if (dimensions.Count == 0)
        {
            return Structure.Field.NoArray;
        }

        if (dimensions.Count == 1)
        {
            return dimensions[0].HasValue ? [dimensions[0].Value,] : [Structure.Field.UnknownArraysize,];
        }

        if (dimensions.Any(dimension => !dimension.HasValue))
        {
            throw new InvalidOperationException(
                "An unsized array dimension ([]) is allowed only as the sole dimension of a one-dimensional array.");
        }

        return dimensions.Select(dimension => dimension.Value).ToArray();
    }

    public static readonly Parser<char, Expr> BitSize = Tok(':').Then(Expr);

    /// <summary>
    ///     An explicit per-declarator alignment override (LANG-15 field-level slice), e.g. <c>value @align(8);</c>.
    ///     <c>@</c> is not used anywhere else in this grammar, so this token collides with nothing. The argument is a
    ///     full expression, matching how <see cref="BitSize"/> already accepts a <c>#define</c>d constant, not only
    ///     a literal.
    /// </summary>
    public static readonly Parser<char, Expr> AlignmentOverride = Tok("@align").Then(Parenthesised(Expr));

    /// <summary>
    ///     An explicit per-declarator byte-offset assertion (LANG-15 field-level slice), e.g. <c>value @4;</c>.
    ///     Checked against the field's computed placement at compile time rather than ever repositioning it.
    /// </summary>
    public static readonly Parser<char, Expr> OffsetAssertion = Tok('@').Then(Expr);

    /// <summary>
    ///     A declarator carries at most one trailing placement suffix - either <see cref="AlignmentOverride"/> or
    ///     <see cref="OffsetAssertion"/>, tried in that order since <c>@align(</c> is the more specific literal
    ///     prefix. Absent when neither is written.
    /// </summary>
    private static readonly Parser<char, (Expr? AlignmentOverride, Expr? OffsetAssertion)> PlacementSuffix =
        OneOf(
                Try(AlignmentOverride).Select(expr => ((Expr?)expr, (Expr?)null)),
                Try(OffsetAssertion).Select(expr => ((Expr?)null, (Expr?)expr))).
            Optional().
            Select(maybe => maybe.HasValue ? maybe.Value : ((Expr?)null, (Expr?)null));

    public static readonly Parser<char, string> ConstKeyword = Tok("const");
    public static readonly Parser<char, string> VolatileKeyword = Tok("volatile");
    public static readonly Parser<char, string> RestrictKeyword = Tok("restrict");

    /// <summary>One layout-neutral qualifier from the closed accepted set (LANG-13); recognized and discarded.</summary>
    private static readonly Parser<char, Unit> TypeQualifier =
        OneOf(ConstKeyword, VolatileKeyword, RestrictKeyword).IgnoreResult();

    /// <summary>
    ///     An identifier token optionally preceded by zero or more layout-neutral qualifiers, which are consumed
    ///     and discarded before the token itself is parsed. Handles a qualifier before the type
    ///     (<c>const uint8 value;</c>) and after a pointer star (<c>uint8 * const value;</c>, since a lone
    ///     <c>*</c> and <c>const</c> are separate whitespace-separated tokens) uniformly, with no special-casing
    ///     for either position.
    /// </summary>
    private static readonly Parser<char, Identifier> QualifiedIdentifierToken =
        TypeQualifier.SkipMany().Then(ExtendedIdentifier);

    /// <summary>
    ///     An optional <c>struct</c>/<c>union</c>/<c>enum</c> keyword written before a field's type reference
    ///     (LANG-01), e.g. <c>struct child value;</c>. Recorded as a hint on the produced <see cref="Field"/> and
    ///     checked against the referenced declaration's actual kind at compile time; it does not become part of the
    ///     type name itself.
    /// </summary>
    private static readonly Parser<char, string> TagKeyword = OneOf(StructKeyword, UnionKeyword, EnumKeyword);

    /// <summary>
    ///     Parses one comma-separated declarator after the first (its own pointer stars, optional array, optional
    ///     bit width, and optional trailing placement suffix), sharing the enclosing <see cref="FieldGroup"/>'s type.
    ///     A declarator with no name tokens at all is an anonymous nonzero-width bitfield (LANG-17), e.g. the
    ///     <c>:3</c> in <c>uint8 flag:1, :3, other:4;</c> - allowed only when a bit width is present, since a
    ///     declarator with neither a name nor a bit width carries no information.
    /// </summary>
    private static readonly Parser<char, (Identifier? Name, int PointerDepth, IReadOnlyList<Maybe<Expr>> Array, Maybe<Expr> BitSize, Expr? AlignmentOverride, Expr? OffsetAssertion)>
        Declarator = Map(
            (words, arr, bitSize, suffix) =>
            {
                List<Identifier> wordList = words.ToList();
                if (wordList.Count == 0 && !bitSize.HasValue)
                {
                    throw new InvalidOperationException("A declarator must have a name, a bit width, or both.");
                }

                return (
                    Name: wordList.Count > 0 ? wordList[^1] : null,
                    PointerDepth: wordList.Sum(o => o.PointerDepth),
                    Array: (IReadOnlyList<Maybe<Expr>>)arr.ToList(),
                    BitSize: bitSize,
                    AlignmentOverride: suffix.AlignmentOverride,
                    OffsetAssertion: suffix.OffsetAssertion);
            },
            QualifiedIdentifierToken.Many(),
            Array.Many(),
            BitSize.Optional(),
            PlacementSuffix);

    /// <summary>
    ///     Parses one field declaration with one or more comma-separated declarators sharing one type (LANG-12),
    ///     e.g. <c>uint8 *a, b[4];</c>. Each declarator carries its own pointer stars, array, and bit width -
    ///     matching C's declarator-list semantics, where a leading star belongs to the declarator it precedes, not
    ///     every name in the list. <see cref="Field"/> itself stays a single-declarator parser used only by its one
    ///     existing direct caller; every real struct/union body consumes this production instead.
    /// </summary>
    public static readonly Parser<char, IEnumerable<Field>> FieldGroup = Map(
            (keywordHint, fields, arr, bitSize, suffix, rest, _) =>
            {
                string? typeKeywordHint = keywordHint.HasValue ? keywordHint.Value : null;
                List<Identifier> fieldList = fields.ToList();

                // A one-token word run with a bit width has no name token to spare - that one word is the whole
                // type and the declarator is an anonymous nonzero-width bitfield (LANG-17), e.g. `uint8 :3;`. A
                // run of two or more tokens keeps today's "last word is the name" rule unchanged, even when a bit
                // width follows (`uint8 flag:1;`), since that case is never ambiguous.
                bool firstDeclaratorIsAnonymous = fieldList.Count == 1 && bitSize.HasValue;

                string typeName = firstDeclaratorIsAnonymous
                    ? fieldList[0].Name
                    : string.Join(
                        " ",
                        fieldList.SkipLast(1).Select(o => o.Name).Where(name => !string.IsNullOrWhiteSpace(name)));
                var typeIdentifier = new Identifier(typeName);
                int firstPointerDepth = firstDeclaratorIsAnonymous ? 0 : fieldList.Sum(o => o.PointerDepth);

                Field MakeField(
                    Identifier? name,
                    int pointerDepth,
                    IReadOnlyList<Maybe<Expr>> declaratorArray,
                    Maybe<Expr> declaratorBitSize,
                    Expr? declaratorAlignmentOverride,
                    Expr? declaratorOffsetAssertion)
                {
                    IReadOnlyList<Expr> arrayCount = BuildArrayCount(declaratorArray);
                    return new Field(
                        typeIdentifier,
                        name ?? new Identifier(string.Empty),
                        arrayCount,
                        declaratorBitSize.HasValue ? declaratorBitSize.Value : NoneExpr.Instance,
                        pointerDepth,
                        typeKeywordHint,
                        declaratorAlignmentOverride,
                        declaratorOffsetAssertion);
                }

                var result = new List<Field>
                {
                    MakeField(
                        firstDeclaratorIsAnonymous ? null : fieldList[^1],
                        firstPointerDepth,
                        arr.ToList(),
                        bitSize,
                        suffix.AlignmentOverride,
                        suffix.OffsetAssertion),
                };
                foreach ((Identifier? Name, int PointerDepth, IReadOnlyList<Maybe<Expr>> Array, Maybe<Expr> BitSize, Expr? AlignmentOverride, Expr? OffsetAssertion) declarator in rest)
                {
                    result.Add(
                        MakeField(
                            declarator.Name,
                            declarator.PointerDepth,
                            declarator.Array,
                            declarator.BitSize,
                            declarator.AlignmentOverride,
                            declarator.OffsetAssertion));
                }

                return (IEnumerable<Field>)result;
            },
            TagKeyword.Optional(),
            QualifiedIdentifierToken.AtLeastOnce(),
            Array.Many(),
            BitSize.Optional(),
            PlacementSuffix,
            Comma.Then(Declarator).Many(),
            Tok(SemiColon).IgnoreResult()).
        Labelled("Field");

    /// <summary>Flattens conditional groups while retaining a predicate on each direct member.</summary>
    public static readonly Parser<char, IEnumerable<Field>> ConditionalFields = Map(
        (condition, yes, no) => ApplyIf(
            condition,
            yes.SelectMany(group => group),
            no.HasValue ? no.Value.SelectMany(group => group) : Enumerable.Empty<Field>()),
        Tok("if").Then(Tok('(')).Then(Rec(() => Expr)).Before(Tok(')')),
        OpenBrace.Then(Rec(() => StructOrField!).Many()).Before(CloseBrace),
        Tok("else").Then(OpenBrace).Then(Rec(() => StructOrField!).Many()).Before(CloseBrace).Optional());

    /// <summary>Parses tagged alternatives with explicit braces and no fall-through.</summary>
    public static readonly Parser<char, IEnumerable<Field>> SwitchFields = Map(
        (selector, cases, fallback) => ApplySwitch(selector, cases, fallback.HasValue ? fallback.Value.SelectMany(group => group) : Enumerable.Empty<Field>()),
        Tok("switch").Then(Tok('(')).Then(Rec(() => Expr)).Before(Tok(')')).Before(OpenBrace),
        Map(
            (tag, fields) => (Tag: tag, Fields: fields.SelectMany(group => group)),
            Tok("case").Then(Rec(() => Expr)).Before(Colon),
            OpenBrace.Then(Rec(() => StructOrField!).Many()).Before(CloseBrace)).Many(),
        Tok("default").Then(Colon).Then(OpenBrace).Then(Rec(() => StructOrField!).Many()).Before(CloseBrace).Optional().Before(CloseBrace));

    private static IEnumerable<Field> ApplySwitch(Expr selector, IEnumerable<(Expr Tag, IEnumerable<Field> Fields)> cases, IEnumerable<Field> fallback)
    {
        var arms = cases.ToArray();
        var group = new ConditionalGroup(selector, arms.Select(arm => arm.Tag).ToArray());
        Expr any = new Literal(0);
        var result = new List<Field>();
        var tags = new HashSet<Expr>();
        var labels = new List<Expr>();
        int armIndex = 0;
        foreach (var arm in arms)
        {
            if (!tags.Add(arm.Tag))
            {
                throw new CStructLayoutException("Duplicate switch case.");
            }

            labels.Add(arm.Tag);
            var condition = new BinaryOp(BinaryOperatorType.Equal, selector, arm.Tag);
            result.AddRange(ApplyCondition(condition, arm.Fields, group, armIndex++));
            any = new BinaryOp(BinaryOperatorType.LogicalOr, any, condition);
        }

        result.AddRange(ApplyCondition(new UnaryOp(UnaryOperatorType.LogicalNot, any), fallback, group, -1));

        // This marker is removed during normalization. Keeping it even for empty arms makes
        // duplicate and non-constant labels a compilation error regardless of runtime selection.
        result.Insert(0, new SwitchCaseValidation(labels));
        return result;
    }

    private static IEnumerable<Field> ApplyIf(Expr condition, IEnumerable<Field> yes, IEnumerable<Field> no)
    {
        var group = new ConditionalGroup(condition);
        return ApplyCondition(condition, yes, group, 1).Concat(
            ApplyCondition(new UnaryOp(UnaryOperatorType.LogicalNot, condition), no, group, 0));
    }

    private static IEnumerable<Field> ApplyCondition(Expr condition, IEnumerable<Field> fields, ConditionalGroup group, int arm)
    {
        foreach (Field field in fields)
        {
            field.Condition = field.Condition is null ? condition : new BinaryOp(BinaryOperatorType.LogicalAnd, condition, field.Condition);
            field.BranchConditions = new[] { new ConditionalBranch(group, arm) }.Concat(field.BranchConditions).ToArray();
            yield return field;
        }
    }

    public static readonly Parser<char, IEnumerable<Field>> StructOrField =
        Rec(() => Try(ConditionalFields).Or(Try(SwitchFields)).Or(Try(InnerStruct!).Select(f => (IEnumerable<Field>)new[] { f, }).Or(FieldGroup)));

    /// <summary>
    ///     A trailing name is optional (LANG-14): when omitted, this inline struct is an anonymous promoted
    ///     member - its own fields are spliced directly into the containing struct's addressable namespace
    ///     (<c>root.x</c>, not <c>root.&lt;anonymous&gt;.x</c>) rather than nested under a name of its own. The
    ///     empty-<see cref="Identifier"/> sentinel reuses the same "no name" representation LANG-17's anonymous
    ///     bitfields already established.
    /// </summary>
    public static readonly Parser<char, Field> InnerStruct = Map(
            (alignOverride, fields, name) => new Struct(
                name.HasValue ? name.Value : new Identifier(string.Empty),
                [.. fields.SelectMany(group => group),],
                false,
                alignOverride.HasValue ? alignOverride.Value : null),
            StructKeyword.Then(SkipWhiteSpacesAndComments).Then(AlignmentOverride.Optional()),
            SkipWhiteSpacesAndComments.Then(OpenBrace).Then(StructOrField.Many()),
            SkipWhiteSpacesAndComments.Before(CloseBrace).Then(Identifier.Optional()).Before(SemiColon)).
        Select<Field>(s => s).
        Labelled("Struct");

    public static readonly Parser<char, CStructElement> Struct = Map(
            (name, alignOverride, fields) => new Struct(
                name,
                [.. fields.SelectMany(group => group),],
                false,
                alignOverride.HasValue ? alignOverride.Value : null),
            StructKeyword.Then(SkipWhiteSpacesAndComments).Then(Identifier),
            AlignmentOverride.Optional(),
            SkipWhiteSpacesAndComments.Then(OpenBrace).
                Then(SkipWhiteSpacesAndComments).
                Then(StructOrField.Many().Before(CloseBrace).Before(SemiColon.Optional()))).
        Select<CStructElement>(s => s).
        Labelled("Struct");

    public static readonly Parser<char, CStructElement> Union = Map(
            (name, alignOverride, fields) => new Struct(
                name,
                [.. fields.SelectMany(group => group)],
                true,
                alignOverride.HasValue ? alignOverride.Value : null),
            UnionKeyword.Then(SkipWhiteSpacesAndComments).Then(Identifier),
            AlignmentOverride.Optional(),
            SkipWhiteSpacesAndComments.Then(OpenBrace).
                Then(SkipWhiteSpacesAndComments).
                Then(FieldGroup.Many().Before(CloseBrace).Before(SemiColon.Optional()))).
        Select<CStructElement>(s => s).
        Labelled("Union");

    public static readonly Parser<char, CStructElement> Typedefstruct = Map(
            (strct, name) => new Typedef(name, strct),
            TypedefKeyword.Then(SkipWhiteSpacesAndComments).Then(Struct.Cast<Struct>()),
            SkipWhiteSpacesAndComments.Then(Identifier).Before(SemiColon)).
        Select<CStructElement>(s => s).
        Labelled("Typedef Struct");

    /// <summary>Parses <c>typedef union tag { ... } alias;</c>, the named-tag form, mirroring <see cref="Typedefstruct"/> for unions.</summary>
    public static readonly Parser<char, CStructElement> Typedefunion = Map(
            (strct, name) => new Typedef(name, strct),
            TypedefKeyword.Then(SkipWhiteSpacesAndComments).Then(Union.Cast<Struct>()),
            SkipWhiteSpacesAndComments.Then(Identifier).Before(SemiColon)).
        Select<CStructElement>(s => s).
        Labelled("Typedef Union");

    /// <summary>Parses <c>typedef struct { ... } Name;</c>, the anonymous inline form with no tag between "struct" and "{".</summary>
    public static readonly Parser<char, CStructElement> AnonymousTypedefStruct = Map(
            (alignOverride, fields, name) => new Typedef(
                name,
                new Struct(
                    name,
                    [.. fields.SelectMany(group => group),],
                    false,
                    alignOverride.HasValue ? alignOverride.Value : null)),
            TypedefKeyword.Then(SkipWhiteSpacesAndComments).Then(StructKeyword).
                Then(SkipWhiteSpacesAndComments).Then(AlignmentOverride.Optional()),
            SkipWhiteSpacesAndComments.Then(OpenBrace).Then(StructOrField.Many()),
            SkipWhiteSpacesAndComments.Before(CloseBrace).Then(Identifier).Before(SemiColon)).
        Select<CStructElement>(s => s).
        Labelled("Anonymous Typedef Struct");

    /// <summary>Parses <c>typedef union { ... } Name;</c>, the anonymous inline form with no tag between "union" and "{".</summary>
    public static readonly Parser<char, CStructElement> AnonymousTypedefUnion = Map(
            (alignOverride, fields, name) => new Typedef(
                name,
                new Struct(
                    name,
                    [.. fields.SelectMany(group => group),],
                    true,
                    alignOverride.HasValue ? alignOverride.Value : null)),
            TypedefKeyword.Then(SkipWhiteSpacesAndComments).Then(UnionKeyword).
                Then(SkipWhiteSpacesAndComments).Then(AlignmentOverride.Optional()),
            SkipWhiteSpacesAndComments.Then(OpenBrace).Then(FieldGroup.Many()),
            SkipWhiteSpacesAndComments.Before(CloseBrace).Then(Identifier).Before(SemiColon)).
        Select<CStructElement>(s => s).
        Labelled("Anonymous Typedef Union");

    public static Parser<char, IEnumerable<CStructElement>> Parser =>
        OneOf(
                Struct,
                Union,
                Try(AnonymousTypedefUnion),
                Try(AnonymousTypedefStruct),
                Try(Typedefunion),
                Try(Typedefstruct),
                Typedef,
                Enum,
                Define).
            Many().
            Between(SkipWhiteSpacesAndComments).
            Before(Parser<char>.End);
}
