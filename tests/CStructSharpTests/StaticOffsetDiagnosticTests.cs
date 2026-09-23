namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks field context when a static offset assertion exceeds the supported expression range.</summary>
[TestClass]
public class StaticOffsetDiagnosticTests
{
    /// <summary>An overflowing asserted offset identifies its field rather than reporting an anonymous expression.</summary>
    [TestMethod]
    public void OverflowingOffsetAssertion_NamesItsField()
    {
        const string definition = "struct root { uint8 first; uint8 next @ (2147483647+1); };";

        // Static placement rejects the offset assertion before a binary source is required.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));

        StringAssert.Contains(failure.Message, "Cannot evaluate offset assertion for next:");
        StringAssert.Contains(failure.Message, "outside the 32-bit range");
    }
}
