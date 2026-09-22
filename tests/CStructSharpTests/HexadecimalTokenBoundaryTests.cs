namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Parsing;

/// <summary>Checks that hexadecimal digits exclude punctuation around the alphabetic digit range.</summary>
[TestClass]
public class HexadecimalTokenBoundaryTests
{
    /// <summary>ASCII punctuation cannot become a hexadecimal digit through case folding.</summary>
    /// <param name="source">A prefixed literal containing an invalid hexadecimal character.</param>
    [TestMethod]
    [DataRow("0x@")]
    [DataRow("0x`")]
    [DataRow("0xG")]
    [DataRow("0x/")]
    public void HexadecimalLiterals_RejectNonDigits(string source)
    {
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLiteral(source, 16));
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseExpression(source));
    }
}
