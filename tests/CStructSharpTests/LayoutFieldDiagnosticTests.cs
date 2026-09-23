namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks field-specific diagnostics for invalid static array and bitfield expressions.</summary>
[TestClass]
public class LayoutFieldDiagnosticTests
{
    /// <summary>An unsupported call in a condition identifies the failure as an invalid layout expression.</summary>
    [TestMethod]
    public void UnsupportedConditionCall_ExplainsTheDeclarationFailure()
    {
        const string definition = "struct root { if (unsupported(1)) { uint8 value; } };";

        // Conditions accept expressions, not arbitrary function execution.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));

        StringAssert.Contains(failure.Message, "Layout declaration contains an invalid expression:");
        Assert.IsInstanceOfType<NotSupportedException>(failure.InnerException);
    }

    /// <summary>The diagnostic distinguishes a negative count, overflowing width, negative width and named zero width.</summary>
    /// <param name="field">The invalid member declaration.</param>
    /// <param name="reason">The field-specific explanation.</param>
    [TestMethod]
    [DataRow("uint8 values[-1-1];", "Array length cannot be negative: values")]
    [DataRow("uint8 bits : (2147483647+1);", "Cannot evaluate bitfield width for bits:")]
    [DataRow("uint8 bits : -1;", "Bitfield width cannot be negative: bits")]
    [DataRow("uint8 bits : 0;", "Bitfield width must be greater than zero (only an unnamed ': 0' separator may be zero): bits")]
    public void InvalidStaticField_ExplainsItsExactRestriction(string field, string reason)
    {
        string definition = "struct root { " + field + " };";

        // The invalid static expression must fail during layout construction, before any bytes are needed.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));

        StringAssert.Contains(failure.Message, reason);
    }
}
