namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Parsing;

/// <summary>Checks incomplete typedefs and directive boundaries at the single-declaration parser entry point.</summary>
[TestClass]
public class ParserDirectiveEndingTests
{
    /// <summary>Incomplete declarations preserve their specific missing-token explanation.</summary>
    /// <param name="source">A truncated declaration or a directive crossing an invalid line boundary.</param>
    /// <param name="expected">The missing token or directive boundary named by the parser.</param>
    [TestMethod]
    [DataRow("typedef enum Tag", "'{'")]
    [DataRow("#ifdef\nNAME\n#endif", "an identifier on the #ifdef line")]
    [DataRow("#ifdef\r\nNAME\r\n#endif", "an identifier on the #ifdef line")]
    [DataRow("#ifdef ABSENT\n", "#endif")]
    public void IncompleteDeclaration_ExplainsItsOwnBoundary(string source, string expected)
    {
        // A complete-layout parser has additional end checks that could conceal the missing local validation.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseElement(source));
        StringAssert.Contains(failure.Message, expected);
    }
}
