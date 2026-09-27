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

/// <summary>Layout expressions and integer literals: operators by precedence, <c>sizeof</c>/<c>alignof</c>, and radix literals with digit separators and suffixes.</summary>
internal sealed partial class LayoutParser
{
    // The binary precedence rows, from the loosest to the unary row that ends ParseBinary's recursion.
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

    /// <summary>A left-associative chain of the binary operators of one precedence row; the unary row ends the recursion.</summary>
    /// <param name="level">The precedence row, from the loosest (0) to the unary row.</param>
    /// <returns>The expression.</returns>
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

    /// <summary>Consumes a binary operator of the given precedence row at the cursor, and the trivia after it.</summary>
    /// <param name="level">The precedence row.</param>
    /// <param name="type">The operator, when one was consumed.</param>
    /// <returns>Whether an operator of that row was at the cursor.</returns>
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

    /// <summary>Consumes an optional <c>+</c> or <c>-</c> before a literal.</summary>
    /// <returns>-1 for a minus sign; 1 otherwise.</returns>
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

    /// <summary>A decimal integer with <c>_</c> separators and an ignored C-style suffix; restores the cursor when there is no digit.</summary>
    /// <param name="sign">The sign already consumed: 1 or -1.</param>
    /// <returns>The literal, or <see langword="null"/>.</returns>
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

    /// <summary>Skips a C integer suffix (<c>u</c>, <c>l</c>, in any case and order), which does not change the value.</summary>
    private void SkipIntegerSuffix()
    {
        while (!this.AtEnd && (this.source[this.position] | 0x20) is 'u' or 'l')
        {
            this.position++;
        }
    }

    /// <summary>The value of one digit in a radix.</summary>
    /// <param name="character">The character.</param>
    /// <param name="radix">2, 8, 10 or 16.</param>
    /// <returns>The digit's value, or -1 when the character is not a digit of that radix.</returns>
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

    /// <summary>The digits of a run without its <c>_</c> separators.</summary>
    /// <param name="run">The digits and separators.</param>
    /// <param name="digitCount">The number of digits in the run.</param>
    /// <returns>The digits.</returns>
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
}
