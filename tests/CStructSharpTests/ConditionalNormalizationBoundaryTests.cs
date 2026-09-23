namespace CStructSharp.Tests;

/// <summary>Checks nested runtime expression nodes when switch labels are frozen into constants.</summary>
[TestClass]
public class ConditionalNormalizationBoundaryTests
{
    /// <summary>Each part of a conditional expression retains its children during case-constant normalization.</summary>
    /// <param name="condition">The expression with a nested condition, true arm or false arm.</param>
    [TestMethod]
    [DataRow("(tag+0)?1:1")]
    [DataRow("tag?(tag+1):1")]
    [DataRow("tag?1:(tag+2)")]
    public void RuntimeConditional_PreservesNestedExpressions(string condition)
    {
        var layout = new CStruct("struct root { uint8 tag; switch (tag) { case 1: {} } if (" + condition + ") { uint8 value; } };");

        dynamic parsed = layout.Parse(new byte[] { 1, 42, }.AsSpan(), "root");

        Assert.AreEqual((byte)42, (byte)parsed.value);
    }
}
