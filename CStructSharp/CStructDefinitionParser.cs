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
        Tok("&").ThenReturn(BinaryOperatorType.And));

    public static readonly Parser<char, Func<Expr, Expr, Expr>> Or
        = Binary(Tok("|").ThenReturn(BinaryOperatorType.Or));

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

    public static readonly Parser<char, Expr> LiteralBinary = Map(
            (sign, lit) => CreateRadixLiteral(sign, lit),
            Sign,
            CIString("0b").Then(BinaryString.Select(o => ParseBigInteger(o, 2)))).
        Select<Expr>(c => c).
        Labelled("Binary Literal");

    public static readonly Parser<char, Expr> LiteralHex = Map(
            (sign, lit) => CreateRadixLiteral(sign, lit),
            Sign,
            CIString("0x").Then(HexString.Select(o => ParseBigInteger(o, 16)))).
        Select<Expr>(c => c).
        Labelled("Hex Literal");

    public static readonly Parser<char, Expr> LiteralOctal = Map(
            (sign, lit) => CreateRadixLiteral(sign, lit),
            Sign,
            CIString("0o").Then(OctalString.Select(o => ParseBigInteger(o, 8)))).
        Select<Expr>(c => c).
        Labelled("Octal Literal");

    public static readonly Parser<char, Expr> LiteralDecimal = Map(
            (sign, lit) => new Literal(sign * lit),
            Sign,
            DigitString.Assert(o => o.Length > 0).Select(o => BigInteger.Parse(o, CultureInfo.InvariantCulture))).
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
                Operator.Prefix(Neg).And(Operator.Prefix(Complement)),
                Operator.InfixL(Div).And(Operator.InfixL(Mul)),
                Operator.InfixL(Minus).And(Operator.InfixL(Add)),
                Operator.InfixL(ShiftLeft).And(Operator.InfixL(ShiftRight)),
                Operator.InfixL(And),
                Operator.InfixL(Or),
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
            TypedefKeyword.Then(SkipWhiteSpacesAndComments).Then(Identifier),
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

    public static readonly Parser<char, Expr> BitSize = Tok(':').Then(Expr);

    public static readonly Parser<char, Field> Field = Map(
            (fields, arr, bitSize, _) =>
            {
                string typeName = string.Join(
                    " ",
                    fields.SkipLast(1).Select(o => o.Name).Where(name => !string.IsNullOrWhiteSpace(name)));
                int pointerDepth = fields.Sum(o => o.PointerDepth);

                return new Field(
                    new Identifier(typeName),
                    fields.Last(),
                    arr.HasValue ? arr.Value.HasValue ? arr.Value.Value : Structure.Field.UnknownArraysize : Structure.Field.NoArray,
                    bitSize.HasValue ? bitSize.Value : NoneExpr.Instance,
                    pointerDepth);
            },
            ExtendedIdentifier.AtLeastOnce(),
            Array.Optional(),
            BitSize.Optional(),
            Tok(SemiColon).IgnoreResult()).
        Labelled("Field");

    public static readonly Parser<char, Field> StructOrField = Rec(() => InnerStruct!.Or(Field));

    public static readonly Parser<char, Field> InnerStruct = Map(
            (fields, name) => new Struct(name, [.. fields,], false),
            StructKeyword.Then(SkipWhiteSpacesAndComments).Then(OpenBrace).Then(StructOrField.Many()),
            SkipWhiteSpacesAndComments.Before(CloseBrace).Then(Identifier).Before(SemiColon)).
        Select<Field>(s => s).
        Labelled("Struct");

    public static readonly Parser<char, CStructElement> Struct = Map(
            (name, fields) => new Struct(name, [.. fields,], false),
            StructKeyword.Then(SkipWhiteSpacesAndComments).Then(Identifier),
            SkipWhiteSpacesAndComments.Then(OpenBrace).
                Then(SkipWhiteSpacesAndComments).
                Then(StructOrField.Many().Before(CloseBrace).Before(SemiColon.Optional()))).
        Select<CStructElement>(s => s).
        Labelled("Struct");

    public static readonly Parser<char, CStructElement> Union = Map(
            (name, fields) => new Struct(name, [.. fields], true),
            UnionKeyword.Then(SkipWhiteSpacesAndComments).Then(Identifier),
            SkipWhiteSpacesAndComments.Then(OpenBrace).
                Then(SkipWhiteSpacesAndComments).
                Then(Field.Many().Before(CloseBrace).Before(SemiColon.Optional()))).
        Select<CStructElement>(s => s).
        Labelled("Union");

    public static readonly Parser<char, CStructElement> Typedefstruct = Map(
            (strct, name) => new Typedef(name, strct),
            TypedefKeyword.Then(SkipWhiteSpacesAndComments).Then(Struct.Cast<Struct>()),
            SkipWhiteSpacesAndComments.Then(Identifier).Before(SemiColon)).
        Select<CStructElement>(s => s).
        Labelled("Typedef Struct");

    public static Parser<char, IEnumerable<CStructElement>> Parser =>
        OneOf(Struct, Union, Try(Typedefstruct), Typedef, Enum, Define).
            Many().
            Between(SkipWhiteSpacesAndComments).
            Before(Parser<char>.End);
}
