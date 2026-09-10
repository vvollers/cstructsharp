namespace CStructSharp.Tests;

using Pidgin;

/// <summary>Groups tests for literals so changes to this behavior are caught.</summary>
[TestClass]
public class Literals
{
    /// <summary>
    ///     The low-level character parser accepts 0, 1, and the underscore separator.
    /// </summary>
    /// <remarks>
    ///     Other digits, letters, spaces, and punctuation must raise a parse error. This checks individual characters,
    ///     not whether a complete binary number is valid.
    /// </remarks>
    [TestMethod]
    public void TestBinaryChar()
    {
        const string binaryChars = "01_";
        const string nonBinaryChars = "Z%Q 2982-";

        foreach (char c in binaryChars)
        {
            CStructDefinitionParser.BinaryDigit.ParseOrThrow(c.ToString());
        }

        foreach (char c in nonBinaryChars)
        {
            Assert.Throws<ParseException>(() => CStructDefinitionParser.BinaryDigit.ParseOrThrow(c.ToString()));
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
    public void TestBinaryLiteral()
    {
        Assert.AreEqual(0b1, CStructDefinitionParser.LiteralBinary.ParseOrThrow("0b1").Value);
        Assert.AreEqual(-0b1000, CStructDefinitionParser.LiteralBinary.ParseOrThrow("-0b1000").Value);
        Assert.AreEqual(0b1000_1000, CStructDefinitionParser.LiteralBinary.ParseOrThrow("0b1000_1000").Value);
        Assert.AreEqual(-0b1001_0110, CStructDefinitionParser.LiteralBinary.ParseOrThrow("-0b1001_0110").Value);
        Assert.AreEqual(0b001001, CStructDefinitionParser.LiteralBinary.ParseOrThrow("0b001001;92").Value);
        Assert.Throws<ParseException>(() => CStructDefinitionParser.LiteralBinary.ParseOrThrow("0b23456F").Value);
    }

    /// <summary>
    ///     This parser returns digit text rather than a number.
    /// </summary>
    /// <remarks>
    ///     It removes underscores from 1001_0110 and stops before the semicolon in 1010;987. Starting with 2 is invalid
    ///     because binary notation only has digits 0 and 1.
    /// </remarks>
    [TestMethod]
    public void TestBinaryString()
    {
        Assert.IsTrue(CStructDefinitionParser.BinaryString.ParseOrThrow("1001001").Equals("1001001"));
        Assert.IsTrue(CStructDefinitionParser.BinaryString.ParseOrThrow("1001_0110").Equals("10010110"));
        Assert.IsTrue(CStructDefinitionParser.BinaryString.ParseOrThrow("1010;987").Equals("1010"));
        Assert.Throws<ParseException>(() => CStructDefinitionParser.BinaryString.ParseOrThrow("23456"));
    }

    /// <summary>
    ///     Digits 0 through 9 and the underscore separator are accepted one character at a time.
    /// </summary>
    /// <remarks>
    ///     A minus sign is rejected here because signs belong to the complete-number parser. This separates recognizing
    ///     digits from interpreting an entire signed number.
    /// </remarks>
    [TestMethod]
    public void TestDecimalChars()
    {
        const string decimalChars = "0123456789_";
        const string nonDecimalChars = "Z%Q -";

        foreach (char c in decimalChars)
        {
            CStructDefinitionParser.Digit.ParseOrThrow(c.ToString());
        }

        foreach (char c in nonDecimalChars)
        {
            Assert.Throws<ParseException>(() => CStructDefinitionParser.Digit.ParseOrThrow(c.ToString()));
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
    public void TestDecimalLiteral()
    {
        Assert.AreEqual(12345, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("12345").Value);
        Assert.AreEqual(-12345, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("-12345").Value);
        Assert.AreEqual(123456, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("123_456").Value);
        Assert.AreEqual(-123456, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("-123_456").Value);
        Assert.AreEqual(1234, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("1234;92").Value);
        Assert.Throws<ParseException>(() => CStructDefinitionParser.LiteralDecimal.ParseOrThrow("FFFFFF").Value);
    }

    /// <summary>
    ///     The result stays a string: 12_314 becomes 12314 after separators are removed.
    /// </summary>
    /// <remarks>
    ///     A semicolon ends the digit run, while an input starting with letters fails. Numeric conversion is
    ///     deliberately tested separately.
    /// </remarks>
    [TestMethod]
    public void TestDecimalString()
    {
        Assert.IsTrue(CStructDefinitionParser.DigitString.ParseOrThrow("12314").Equals("12314"));
        Assert.IsTrue(CStructDefinitionParser.DigitString.ParseOrThrow("12_314").Equals("12314"));
        Assert.IsTrue(CStructDefinitionParser.DigitString.ParseOrThrow("12314;987").Equals("12314"));
        Assert.Throws<ParseException>(() => CStructDefinitionParser.DigitString.ParseOrThrow("FFFFFF"));
    }

    /// <summary>
    ///     The 0x prefix selects hexadecimal, where A through F represent values 10 through 15.
    /// </summary>
    /// <remarks>
    ///     Underscores do not change the number and a leading minus changes its sign. The test also checks that a
    ///     semicolon ends the token and that letters outside the hex alphabet are rejected.
    /// </remarks>
    [TestMethod]
    public void TestHexLiteral()
    {
        Assert.AreEqual(0x12345, CStructDefinitionParser.LiteralHex.ParseOrThrow("0x12345").Value);
        Assert.AreEqual(-0x12345, CStructDefinitionParser.LiteralHex.ParseOrThrow("-0x12345").Value);
        Assert.AreEqual(0x123456, CStructDefinitionParser.LiteralHex.ParseOrThrow("0x123_456").Value);
        Assert.AreEqual(-0x123456, CStructDefinitionParser.LiteralHex.ParseOrThrow("-0x123_456").Value);
        Assert.AreEqual(0x1234, CStructDefinitionParser.LiteralHex.ParseOrThrow("0x1234;92").Value);
        Assert.Throws<ParseException>(() => CStructDefinitionParser.LiteralHex.ParseOrThrow("0xQQQQYYY").Value);
    }

    /// <summary>
    ///     89__AF2 must normalize to the text 89AF2, preserving the digits while removing separators.
    /// </summary>
    /// <remarks>
    ///     Parsing stops at a semicolon. QWERTY fails immediately because Q is not a hexadecimal digit; this test does
    ///     not yet convert the accepted text into an integer.
    /// </remarks>
    [TestMethod]
    public void TestHexString()
    {
        Assert.IsTrue(CStructDefinitionParser.HexString.ParseOrThrow("89AF2").Equals("89AF2"));
        Assert.IsTrue(CStructDefinitionParser.HexString.ParseOrThrow("89__AF2").Equals("89AF2"));
        Assert.IsTrue(CStructDefinitionParser.HexString.ParseOrThrow("89AF2;987").Equals("89AF2"));
        Assert.Throws<ParseException>(() => CStructDefinitionParser.HexString.ParseOrThrow("QWERTY"));
    }

    /// <summary>
    ///     The shared parser chooses binary, octal, decimal, or hexadecimal from the prefix.
    /// </summary>
    /// <remarks>
    ///     Here octal uses the library's explicit 0o notation. Leading spaces are allowed, and trailing text after a
    ///     completed token is left for another parser; this is not a whole-file validation test.
    /// </remarks>
    [TestMethod]
    public void TestLiteral()
    {
        Assert.AreEqual(0b1, CStructDefinitionParser.Literal.ParseOrThrow("0b1").Value);
        Assert.AreEqual(Convert.ToInt32("673747", 8), CStructDefinitionParser.Literal.ParseOrThrow("0o673747").Value);
        Assert.AreEqual(0x12345, CStructDefinitionParser.Literal.ParseOrThrow("0x12345").Value);
        Assert.AreEqual(12345, CStructDefinitionParser.Literal.ParseOrThrow("12345").Value);

        Assert.AreEqual(0b1, CStructDefinitionParser.Literal.ParseOrThrow("  0b1").Value);
        Assert.AreEqual(Convert.ToInt32("673747", 8), CStructDefinitionParser.Literal.ParseOrThrow("  0o673747").Value);
        Assert.AreEqual(0x12345, CStructDefinitionParser.Literal.ParseOrThrow("  0x12345").Value);
        Assert.AreEqual(12345, CStructDefinitionParser.Literal.ParseOrThrow("  12345").Value);

        Assert.AreEqual(0b1, CStructDefinitionParser.Literal.ParseOrThrow("0b1  575").Value);
        Assert.AreEqual(
                        Convert.ToInt32("673747", 8),
                        CStructDefinitionParser.Literal.ParseOrThrow("0o673747  575").Value);
        Assert.AreEqual(0x12345, CStructDefinitionParser.Literal.ParseOrThrow("0x12345  575").Value);
        Assert.AreEqual(12345, CStructDefinitionParser.Literal.ParseOrThrow("12345  575").Value);

        Assert.AreEqual(0b1, CStructDefinitionParser.Literal.ParseOrThrow("  0b1  575").Value);
        Assert.AreEqual(
                        Convert.ToInt32("673747", 8),
                        CStructDefinitionParser.Literal.ParseOrThrow("  0o673747  575").Value);
        Assert.AreEqual(0x12345, CStructDefinitionParser.Literal.ParseOrThrow("  0x12345  575").Value);
        Assert.AreEqual(12345, CStructDefinitionParser.Literal.ParseOrThrow("  12345  575").Value);

        Assert.Throws<ParseException>(() => CStructDefinitionParser.Literal.ParseOrThrow("%tBEQOFKF").Value);
    }

    /// <summary>
    ///     The library uses 0o to introduce base-eight numbers.
    /// </summary>
    /// <remarks>
    ///     Only digits 0 through 7 contribute to the value; underscores are ignored and a leading minus is retained.
    ///     Results are compared with C# base-eight conversion, and a token beginning with 9 must fail.
    /// </remarks>
    [TestMethod]
    public void TestOctalLiteral()
    {
        Assert.AreEqual(
                        Convert.ToInt32("673747", 8),
                        CStructDefinitionParser.LiteralOctal.ParseOrThrow("0o673747").Value);
        Assert.AreEqual(
                        -Convert.ToInt32("673747", 8),
                        CStructDefinitionParser.LiteralOctal.ParseOrThrow("-0o673747").Value);
        Assert.AreEqual(
                        Convert.ToInt32("673747", 8),
                        CStructDefinitionParser.LiteralOctal.ParseOrThrow("0o673_747").Value);
        Assert.AreEqual(
                        -Convert.ToInt32("673747", 8),
                        CStructDefinitionParser.LiteralOctal.ParseOrThrow("-0o673_747").Value);
        Assert.AreEqual(
                        Convert.ToInt32("673747", 8),
                        CStructDefinitionParser.LiteralOctal.ParseOrThrow("0o673_747;92").Value);
        Assert.Throws<ParseException>(() => CStructDefinitionParser.LiteralOctal.ParseOrThrow("0o98F98").Value);
    }

    /// <summary>
    ///     767_226 becomes the digit string 767226, and a semicolon ends the token.
    /// </summary>
    /// <remarks>
    ///     An input starting with 8 fails because octal has no digit 8. Keeping this check separate from integer
    ///     conversion makes token-boundary errors easier to locate.
    /// </remarks>
    [TestMethod]
    public void TestOctalString()
    {
        Assert.IsTrue(CStructDefinitionParser.OctalString.ParseOrThrow("767226").Equals("767226"));
        Assert.IsTrue(CStructDefinitionParser.OctalString.ParseOrThrow("767_226").Equals("767226"));
        Assert.IsTrue(CStructDefinitionParser.OctalString.ParseOrThrow("767226;987").Equals("767226"));
        Assert.Throws<ParseException>(() => CStructDefinitionParser.OctalString.ParseOrThrow("89FFF"));
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
    public void TestIntegerLiteralSuffix()
    {
        Assert.AreEqual(1, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("1U").Value);
        Assert.AreEqual(1, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("1u").Value);
        Assert.AreEqual(100, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("100UL").Value);
        Assert.AreEqual(100, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("100LU").Value);
        Assert.AreEqual(100, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("100LL").Value);
        Assert.AreEqual(100, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("100ULL").Value);
        Assert.AreEqual(-5, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("-5U").Value);
        Assert.AreEqual(1, CStructDefinitionParser.LiteralDecimal.ParseOrThrow("1U;92").Value);

        Assert.AreEqual(0x1, CStructDefinitionParser.LiteralHex.ParseOrThrow("0x1UL").Value);
        Assert.AreEqual(0b1, CStructDefinitionParser.LiteralBinary.ParseOrThrow("0b1U").Value);
        Assert.AreEqual(
                        Convert.ToInt32("17", 8),
                        CStructDefinitionParser.LiteralOctal.ParseOrThrow("0o17U").Value);

        Assert.AreEqual(1, CStructDefinitionParser.Literal.ParseOrThrow("1U").Value);
    }

    /// <summary>
    ///     Both upper- and lowercase A through F are valid, along with decimal digits and underscores.
    /// </summary>
    /// <remarks>
    ///     Z, punctuation, and spaces must fail this one-character parser. The check establishes the alphabet used by
    ///     hexadecimal constants in layout definitions.
    /// </remarks>
    [TestMethod]
    public void TextHexadecimalChar()
    {
        const string hexChars = "0123456789ABCDEFabcdef_";
        const string nonHexChars = "Z%Q ";

        foreach (char c in hexChars)
        {
            CStructDefinitionParser.HexDigit.ParseOrThrow(c.ToString());
        }

        foreach (char c in nonHexChars)
        {
            Assert.Throws<ParseException>(() => CStructDefinitionParser.HexDigit.ParseOrThrow(c.ToString()));
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
    public void TextOctalChar()
    {
        const string octalChars = "01234567_";
        const string nonOctalChars = "89abQ";

        foreach (char c in octalChars)
        {
            CStructDefinitionParser.OctalDigit.ParseOrThrow(c.ToString());
        }

        foreach (char c in nonOctalChars)
        {
            Assert.Throws<ParseException>(() => CStructDefinitionParser.OctalDigit.ParseOrThrow(c.ToString()));
        }
    }
}
