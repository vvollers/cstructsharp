namespace CStructSharp.Tests;

/// <summary>Checks line continuation length depends on the following newline, not preceding whitespace.</summary>
[TestClass]
public class WhitespaceContinuationBoundaryTests
{
    /// <summary>A carriage return before a continued LF cannot consume the next declaration's first letter.</summary>
    [TestMethod]
    public void CarriageReturnBeforeLfContinuation_PreservesNextToken()
    {
        var layout = new CStruct("\r\\\nstruct root { uint8 value; };");
        dynamic parsed = layout.Parse(new byte[] { 42, }.AsSpan(), "root");
        Assert.AreEqual((byte)42, (byte)parsed.value);
    }
}
