namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks diagnostics after layout-dependent calls have become constant array counts.</summary>
[TestClass]
public class CompiledArrayDiagnosticTests
{
    /// <summary>An overflowing folded count identifies its array field in one or several dimensions.</summary>
    /// <param name="secondDimension">An optional second fixed dimension.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("[2]")]
    public void FoldedArrayCount_ReportsTheFieldWhenOutsideTheSupportedRange(string secondDimension)
    {
        string definition = "struct root { uint8 payload[sizeof(uint8)+2147483647]" + secondDimension + "; };";

        // sizeof is resolved during model compilation, so this checks the final compiled count diagnostic.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));

        StringAssert.Contains(failure.Message, "Cannot evaluate array length for payload:");
        StringAssert.Contains(failure.Message, "outside the 32-bit range");
    }
}
