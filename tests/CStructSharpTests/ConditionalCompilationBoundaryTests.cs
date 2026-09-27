namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks that construction validates each conditional selector against the expression limits.</summary>
[TestClass]
public class ConditionalCompilationBoundaryTests
{
    /// <summary>
    ///     Selection evaluates each group's own selector, never a combination of nested conditions, so the limits
    ///     apply to each selector: nested short selectors are accepted and one long selector is rejected.
    /// </summary>
    [TestMethod]
    public void EachSelector_IsCheckedAgainstTheLimitsOnItsOwn()
    {
        var options = new CStructCompilationOptions { MaxExpressionTokens = 2, };

        var nested = new CStruct("struct root { if (1) { if (1) { uint8 value; } } };", compilationOptions: options);
        Assert.AreEqual((byte)7, nested.ReadValue<byte>(new byte[] { 7, }, "root.value"));

        CStructLayoutException error = Assert.Throws<CStructLayoutException>(() =>
            new CStruct("struct root { if (1 + 1 + 1) { uint8 value; } };", compilationOptions: options));
        StringAssert.Contains(error.Message, "Maximum expression evaluation work exceeded");
    }
}
