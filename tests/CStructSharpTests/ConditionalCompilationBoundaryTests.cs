namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks construction validates the complete condition attached to a nested member.</summary>
[TestClass]
public class ConditionalCompilationBoundaryTests
{
    /// <summary>Individually short selectors do not bypass the limit for their combined member condition.</summary>
    [TestMethod]
    public void NestedConditions_ValidateCombinedExpressionAtConstruction()
    {
        // Each selector fits separately; their three-node combined condition must be checked before reading.
        CStructLayoutException error = Assert.Throws<CStructLayoutException>(() =>
            new CStruct(
                "struct root { if (1) { if (1) { uint8 value; } } };",
                compilationOptions: new CStructCompilationOptions { MaxExpressionTokens = 2, }));
        StringAssert.Contains(error.Message, "Maximum expression evaluation work exceeded");
    }
}
