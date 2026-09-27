namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Parsing;

/// <summary>
///     Checks integer literals and the digit parsers behind them: binary, octal, decimal and hexadecimal digits,
///     separators, signs and suffixes.
/// </summary>
[TestClass]
public class LiteralTests
{
    /// <summary>
    ///     The low-level character parser accepts 0, 1, and the underscore separator.
    /// </summary>
    /// <remarks>
    ///     Other digits, letters, spaces, and punctuation must raise a parse error. This checks individual characters,
    ///     not whether a complete binary number is valid.
    /// </remarks>
    [TestMethod]
    public void BinaryDigit_AcceptsOnlyZeroOneAndSeparator()
    {
        const string binaryChars = "01_";
        const string nonBinaryChars = "Z%Q 2982-";

        foreach (char c in binaryChars)
        {
            Assert.IsTrue(LayoutParser.IsDigitOrSeparator(c, 2), c.ToString());
        }

        foreach (char c in nonBinaryChars)
        {
            Assert.IsFalse(LayoutParser.IsDigitOrSeparator(c, 2), c.ToString());
        }
    }

    /// <summary>
    ///     The 0b prefix selects base two, and underscores only improve readability: 0b1000_1000 is 136.
    /// </summary>
    /// <remarks>
    ///     Negative signs are preserved. The token parser stops before a semicolon, but an input beginning with invalid
    ///     binary digits must fail.
    /// </remarks>
    [TestMethod]
    public void BinaryLiteral_ReadsBaseTwoWithSeparatorsAndSign()
    {
        Assert.AreEqual(0b1, LayoutParser.ParseLiteral("0b1", 2).Evaluate());
        Assert.AreEqual(-0b1000, LayoutParser.ParseLiteral("-0b1000", 2).Evaluate());
        Assert.AreEqual(0b1000_1000, LayoutParser.ParseLiteral("0b1000_1000", 2).Evaluate());
        Assert.AreEqual(-0b1001_0110, LayoutParser.ParseLiteral("-0b1001_0110", 2).Evaluate());
        Assert.AreEqual(0b001001, LayoutParser.ParseLiteral("0b001001;92", 2).Evaluate());
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLiteral("0b23456F", 2).Evaluate());
    }

    /// <summary>
    ///     This parser returns digit text rather than a number.
    /// </summary>
    /// <remarks>
    ///     It removes underscores from 1001_0110 and stops before the semicolon in 1010;987. Starting with 2 is invalid
    ///     because binary notation only has digits 0 and 1.
    /// </remarks>
    [TestMethod]
    public void BinaryDigits_NormalizeToText()
    {
        Assert.IsTrue(LayoutParser.ParseDigits("1001001", 2).Equals("1001001"));
        Assert.IsTrue(LayoutParser.ParseDigits("1001_0110", 2).Equals("10010110"));
        Assert.IsTrue(LayoutParser.ParseDigits("1010;987", 2).Equals("1010"));
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseDigits("23456", 2));
    }

    /// <summary>
    ///     Digits 0 through 9 and the underscore separator are accepted one character at a time.
    /// </summary>
    /// <remarks>
    ///     A minus sign is rejected here because signs belong to the complete-number parser. This separates recognizing
    ///     digits from interpreting an entire signed number.
    /// </remarks>
    [TestMethod]
    public void DecimalDigit_AcceptsDigitsAndSeparator()
    {
        const string decimalChars = "0123456789_";
        const string nonDecimalChars = "Z%Q -";

        foreach (char c in decimalChars)
        {
            Assert.IsTrue(LayoutParser.IsDigitOrSeparator(c, 10), c.ToString());
        }

        foreach (char c in nonDecimalChars)
        {
            Assert.IsFalse(LayoutParser.IsDigitOrSeparator(c, 10), c.ToString());
        }
    }

    /// <summary>
    ///     123_456 must become the integer 123456, and a leading minus must make it negative.
    /// </summary>
    /// <remarks>
    ///     Parsing 1234;92 returns the first number, 1234. Letters at the start must fail because this parser expects a
    ///     decimal token.
    /// </remarks>
    [TestMethod]
    public void DecimalLiteral_ReadsSeparatorsAndSign()
    {
        Assert.AreEqual(12345, LayoutParser.ParseLiteral("12345", 10).Evaluate());
        Assert.AreEqual(-12345, LayoutParser.ParseLiteral("-12345", 10).Evaluate());
        Assert.AreEqual(123456, LayoutParser.ParseLiteral("123_456", 10).Evaluate());
        Assert.AreEqual(-123456, LayoutParser.ParseLiteral("-123_456", 10).Evaluate());
        Assert.AreEqual(1234, LayoutParser.ParseLiteral("1234;92", 10).Evaluate());
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLiteral("FFFFFF", 10).Evaluate());
    }

    /// <summary>
    ///     The result stays a string: 12_314 becomes 12314 after separators are removed.
    /// </summary>
    /// <remarks>
    ///     A semicolon ends the digit run, while an input starting with letters fails. Numeric conversion is
    ///     deliberately tested separately.
    /// </remarks>
    [TestMethod]
    public void DecimalDigits_NormalizeToText()
    {
        Assert.IsTrue(LayoutParser.ParseDigits("12314", 10).Equals("12314"));
        Assert.IsTrue(LayoutParser.ParseDigits("12_314", 10).Equals("12314"));
        Assert.IsTrue(LayoutParser.ParseDigits("12314;987", 10).Equals("12314"));
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseDigits("FFFFFF", 10));
    }

    /// <summary>
    ///     The 0x prefix selects hexadecimal, where A through F represent values 10 through 15.
    /// </summary>
    /// <remarks>
    ///     Underscores do not change the number and a leading minus changes its sign. The test also checks that a
    ///     semicolon ends the token and that letters outside the hex alphabet are rejected.
    /// </remarks>
    [TestMethod]
    public void HexadecimalLiteral_ReadsBaseSixteenWithSeparatorsAndSign()
    {
        Assert.AreEqual(0x12345, LayoutParser.ParseLiteral("0x12345", 16).Evaluate());
        Assert.AreEqual(-0x12345, LayoutParser.ParseLiteral("-0x12345", 16).Evaluate());
        Assert.AreEqual(0x123456, LayoutParser.ParseLiteral("0x123_456", 16).Evaluate());
        Assert.AreEqual(-0x123456, LayoutParser.ParseLiteral("-0x123_456", 16).Evaluate());
        Assert.AreEqual(0x1234, LayoutParser.ParseLiteral("0x1234;92", 16).Evaluate());
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLiteral("0xQQQQYYY", 16).Evaluate());
    }

    /// <summary>
    ///     89__AF2 must normalize to the text 89AF2, preserving the digits while removing separators.
    /// </summary>
    /// <remarks>
    ///     Parsing stops at a semicolon. QWERTY fails immediately because Q is not a hexadecimal digit; this test does
    ///     not yet convert the accepted text into an integer.
    /// </remarks>
    [TestMethod]
    public void HexadecimalDigits_NormalizeToText()
    {
        Assert.IsTrue(LayoutParser.ParseDigits("89AF2", 16).Equals("89AF2"));
        Assert.IsTrue(LayoutParser.ParseDigits("89__AF2", 16).Equals("89AF2"));
        Assert.IsTrue(LayoutParser.ParseDigits("89AF2;987", 16).Equals("89AF2"));
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseDigits("QWERTY", 16));
    }

    /// <summary>
    ///     The shared parser chooses binary, octal, decimal, or hexadecimal from the prefix.
    /// </summary>
    /// <remarks>
    ///     Here octal uses the library's explicit 0o notation. Leading spaces are allowed, and trailing text after a
    ///     completed token is left for another parser; this is not a whole-file validation test.
    /// </remarks>
    [TestMethod]
    public void Literal_ChoosesItsBaseFromThePrefix()
    {
        Assert.AreEqual(0b1, LayoutParser.ParseLiteral("0b1").Evaluate());
        Assert.AreEqual(Convert.ToInt32("673747", 8), LayoutParser.ParseLiteral("0o673747").Evaluate());
        Assert.AreEqual(0x12345, LayoutParser.ParseLiteral("0x12345").Evaluate());
        Assert.AreEqual(12345, LayoutParser.ParseLiteral("12345").Evaluate());

        Assert.AreEqual(0b1, LayoutParser.ParseLiteral("  0b1").Evaluate());
        Assert.AreEqual(Convert.ToInt32("673747", 8), LayoutParser.ParseLiteral("  0o673747").Evaluate());
        Assert.AreEqual(0x12345, LayoutParser.ParseLiteral("  0x12345").Evaluate());
        Assert.AreEqual(12345, LayoutParser.ParseLiteral("  12345").Evaluate());

        Assert.AreEqual(0b1, LayoutParser.ParseLiteral("0b1  575").Evaluate());
        Assert.AreEqual(
                        Convert.ToInt32("673747", 8),
                        LayoutParser.ParseLiteral("0o673747  575").Evaluate());
        Assert.AreEqual(0x12345, LayoutParser.ParseLiteral("0x12345  575").Evaluate());
        Assert.AreEqual(12345, LayoutParser.ParseLiteral("12345  575").Evaluate());

        Assert.AreEqual(0b1, LayoutParser.ParseLiteral("  0b1  575").Evaluate());
        Assert.AreEqual(
                        Convert.ToInt32("673747", 8),
                        LayoutParser.ParseLiteral("  0o673747  575").Evaluate());
        Assert.AreEqual(0x12345, LayoutParser.ParseLiteral("  0x12345  575").Evaluate());
        Assert.AreEqual(12345, LayoutParser.ParseLiteral("  12345  575").Evaluate());

        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLiteral("%tBEQOFKF").Evaluate());
    }

    /// <summary>
    ///     The library uses 0o to introduce base-eight numbers.
    /// </summary>
    /// <remarks>
    ///     Only digits 0 through 7 contribute to the value; underscores are ignored and a leading minus is retained.
    ///     Results are compared with C# base-eight conversion, and a token beginning with 9 must fail.
    /// </remarks>
    [TestMethod]
    public void OctalLiteral_ReadsBaseEightWithSeparatorsAndSign()
    {
        Assert.AreEqual(
                        Convert.ToInt32("673747", 8),
                        LayoutParser.ParseLiteral("0o673747", 8).Evaluate());
        Assert.AreEqual(
                        -Convert.ToInt32("673747", 8),
                        LayoutParser.ParseLiteral("-0o673747", 8).Evaluate());
        Assert.AreEqual(
                        Convert.ToInt32("673747", 8),
                        LayoutParser.ParseLiteral("0o673_747", 8).Evaluate());
        Assert.AreEqual(
                        -Convert.ToInt32("673747", 8),
                        LayoutParser.ParseLiteral("-0o673_747", 8).Evaluate());
        Assert.AreEqual(
                        Convert.ToInt32("673747", 8),
                        LayoutParser.ParseLiteral("0o673_747;92", 8).Evaluate());
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLiteral("0o98F98", 8).Evaluate());
    }

    /// <summary>
    ///     767_226 becomes the digit string 767226, and a semicolon ends the token.
    /// </summary>
    /// <remarks>
    ///     An input starting with 8 fails because octal has no digit 8. Keeping this check separate from integer
    ///     conversion makes token-boundary errors easier to locate.
    /// </remarks>
    [TestMethod]
    public void OctalDigits_NormalizeToText()
    {
        Assert.IsTrue(LayoutParser.ParseDigits("767226", 8).Equals("767226"));
        Assert.IsTrue(LayoutParser.ParseDigits("767_226", 8).Equals("767226"));
        Assert.IsTrue(LayoutParser.ParseDigits("767226;987", 8).Equals("767226"));
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseDigits("89FFF", 8));
    }

    /// <summary>
    ///     A trailing u/U/l/L combination (in any order or repetition) is recognized and discarded without changing
    ///     the parsed value, matching the "no semantic effect" framing for this deliberately permissive feature.
    /// </summary>
    /// <remarks>
    ///     Portable's expressions are already exact-integer, so C's width/signedness suffix rules carry no
    ///     information Portable needs. This still stops exactly at a semicolon, just like every other literal test.
    /// </remarks>
    [TestMethod]
    public void IntegerSuffix_IsAcceptedAndIgnored()
    {
        Assert.AreEqual(1, LayoutParser.ParseLiteral("1U", 10).Evaluate());
        Assert.AreEqual(1, LayoutParser.ParseLiteral("1u", 10).Evaluate());
        Assert.AreEqual(100, LayoutParser.ParseLiteral("100UL", 10).Evaluate());
        Assert.AreEqual(100, LayoutParser.ParseLiteral("100LU", 10).Evaluate());
        Assert.AreEqual(100, LayoutParser.ParseLiteral("100LL", 10).Evaluate());
        Assert.AreEqual(100, LayoutParser.ParseLiteral("100ULL", 10).Evaluate());
        Assert.AreEqual(-5, LayoutParser.ParseLiteral("-5U", 10).Evaluate());
        Assert.AreEqual(1, LayoutParser.ParseLiteral("1U;92", 10).Evaluate());

        Assert.AreEqual(0x1, LayoutParser.ParseLiteral("0x1UL", 16).Evaluate());
        Assert.AreEqual(0b1, LayoutParser.ParseLiteral("0b1U", 2).Evaluate());
        Assert.AreEqual(
                        Convert.ToInt32("17", 8),
                        LayoutParser.ParseLiteral("0o17U", 8).Evaluate());

        Assert.AreEqual(1, LayoutParser.ParseLiteral("1U").Evaluate());
    }

    /// <summary>
    ///     Both upper- and lowercase A through F are valid, along with decimal digits and underscores.
    /// </summary>
    /// <remarks>
    ///     Z, punctuation, and spaces must fail this one-character parser. The check establishes the alphabet used by
    ///     hexadecimal constants in layout definitions.
    /// </remarks>
    [TestMethod]
    public void HexadecimalDigit_AcceptsBothCases()
    {
        const string hexChars = "0123456789ABCDEFabcdef_";
        const string nonHexChars = "Z%Q ";

        foreach (char c in hexChars)
        {
            Assert.IsTrue(LayoutParser.IsDigitOrSeparator(c, 16), c.ToString());
        }

        foreach (char c in nonHexChars)
        {
            Assert.IsFalse(LayoutParser.IsDigitOrSeparator(c, 16), c.ToString());
        }
    }

    /// <summary>
    ///     Only 0 through 7 and the underscore separator belong to this character parser.
    /// </summary>
    /// <remarks>
    ///     Digits 8 and 9 and letters must be rejected. This is a check of the octal alphabet, not of binary data read
    ///     from a stream.
    /// </remarks>
    [TestMethod]
    public void OctalDigit_AcceptsZeroToSeven()
    {
        const string octalChars = "01234567_";
        const string nonOctalChars = "89abQ";

        foreach (char c in octalChars)
        {
            Assert.IsTrue(LayoutParser.IsDigitOrSeparator(c, 8), c.ToString());
        }

        foreach (char c in nonOctalChars)
        {
            Assert.IsFalse(LayoutParser.IsDigitOrSeparator(c, 8), c.ToString());
        }
    }

    /// <summary>ASCII punctuation cannot become a hexadecimal digit through case folding.</summary>
    /// <param name="source">A prefixed literal containing an invalid hexadecimal character.</param>
    [TestMethod]
    [DataRow("0x@")]
    [DataRow("0x`")]
    [DataRow("0xG")]
    [DataRow("0x/")]
    public void HexadecimalLiterals_RejectNonDigits(string source)
    {
        // Require hexadecimal input; automatic literal parsing may accept just the leading decimal zero.
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLiteral(source, 16));

        // The expression entry point must consume the complete input instead of accepting a valid prefix.
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseExpression(source));
    }
}
