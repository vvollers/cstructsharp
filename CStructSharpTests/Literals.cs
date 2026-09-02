namespace CStructSharp.Tests;

using Pidgin;

/// <summary>Groups tests for literals so changes to this behavior are caught.</summary>
[TestClass]
public class Literals
{
    /// <summary>
    ///     Validates the lexical character set allowed for binary numeric input, including separators. Correct tokenization is
    ///     required before any C-style literal can be evaluated safely.
    /// </summary>
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
    ///     Parses signed binary literals using 0b prefix and underscore separators. This mirrors binary constant usage in
    ///     low-level C headers.
    /// </summary>
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
    ///     Extracts binary digit sequences as normalized text rather than converting to an integer. This isolates tokenizer
    ///     behavior from numeric conversion logic.
    /// </summary>
    [TestMethod]
    public void TestBinaryString()
    {
        Assert.IsTrue(CStructDefinitionParser.BinaryString.ParseOrThrow("1001001").Equals("1001001"));
        Assert.IsTrue(CStructDefinitionParser.BinaryString.ParseOrThrow("1001_0110").Equals("10010110"));
        Assert.IsTrue(CStructDefinitionParser.BinaryString.ParseOrThrow("1010;987").Equals("1010"));
        Assert.Throws<ParseException>(() => CStructDefinitionParser.BinaryString.ParseOrThrow("23456"));
    }

    /// <summary>
    ///     Validates decimal digit tokenization rules, including accepted separators. This is the lexical basis for plain
    ///     integer constants in C expressions.
    /// </summary>
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
    ///     Parses signed decimal integer literals with optional grouping underscores. It confirms conversion and rejection
    ///     behavior for non-decimal content.
    /// </summary>
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
    ///     Parses decimal digit runs as text output, focusing on normalization and stopping rules. It differs from
    ///     TestDecimalLiteral by avoiding numeric conversion.
    /// </summary>
    [TestMethod]
    public void TestDecimalString()
    {
        Assert.IsTrue(CStructDefinitionParser.DigitString.ParseOrThrow("12314").Equals("12314"));
        Assert.IsTrue(CStructDefinitionParser.DigitString.ParseOrThrow("12_314").Equals("12314"));
        Assert.IsTrue(CStructDefinitionParser.DigitString.ParseOrThrow("12314;987").Equals("12314"));
        Assert.Throws<ParseException>(() => CStructDefinitionParser.DigitString.ParseOrThrow("FFFFFF"));
    }

    /// <summary>
    ///     Parses signed hexadecimal literals with 0x prefix and separators, a core C notation for flags and magic values. The
    ///     test also verifies invalid digit handling.
    /// </summary>
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
    ///     Parses hexadecimal character runs into normalized text form. This isolates lexing behavior from integer conversion
    ///     performed in TestHexLiteral.
    /// </summary>
    [TestMethod]
    public void TestHexString()
    {
        Assert.IsTrue(CStructDefinitionParser.HexString.ParseOrThrow("89AF2").Equals("89AF2"));
        Assert.IsTrue(CStructDefinitionParser.HexString.ParseOrThrow("89__AF2").Equals("89AF2"));
        Assert.IsTrue(CStructDefinitionParser.HexString.ParseOrThrow("89AF2;987").Equals("89AF2"));
        Assert.Throws<ParseException>(() => CStructDefinitionParser.HexString.ParseOrThrow("QWERTY"));
    }

    /// <summary>
    ///     Validates the umbrella literal parser that accepts binary, octal, decimal, and hexadecimal forms under one entry
    ///     point. This matches real C definitions where multiple bases are mixed.
    /// </summary>
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
    ///     Parses signed octal literals with 0o prefix and separators. This supports C-like schemas and tools that still use
    ///     octal constants.
    /// </summary>
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
    ///     Parses octal digit runs as normalized text and verifies stop conditions on invalid characters. It complements
    ///     TestOctalLiteral by testing lexing independently.
    /// </summary>
    [TestMethod]
    public void TestOctalString()
    {
        Assert.IsTrue(CStructDefinitionParser.OctalString.ParseOrThrow("767226").Equals("767226"));
        Assert.IsTrue(CStructDefinitionParser.OctalString.ParseOrThrow("767_226").Equals("767226"));
        Assert.IsTrue(CStructDefinitionParser.OctalString.ParseOrThrow("767226;987").Equals("767226"));
        Assert.Throws<ParseException>(() => CStructDefinitionParser.OctalString.ParseOrThrow("89FFF"));
    }

    /// <summary>
    ///     Validates allowed single-character tokens for hexadecimal digits in both upper and lower case plus separators. This
    ///     ensures predictable lexing before hex conversion.
    /// </summary>
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
    ///     Validates allowed single-character tokens for octal digits and separators. This guards parser correctness for octal
    ///     literal input.
    /// </summary>
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
