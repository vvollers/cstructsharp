namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks pointer-bearing array aliases fail with a shape-specific diagnostic.</summary>
[TestClass]
public class PointerArrayAliasBoundaryTests
{
    /// <summary>The pointer stored in an array alias cannot silently become an inline primitive array.</summary>
    [TestMethod]
    public void PointerBearingArrayAlias_IsRejectedAtTheMember()
    {
        // Diagnose the unsupported alias before attempting to consume any binary input.
        CStructLayoutException error = Assert.Throws<CStructLayoutException>(() =>
            new CStruct("typedef uint8 *pair[2]; struct root { pair values; };"));
        StringAssert.Contains(error.Message, "A pointer to a typedef array is not supported: values");
    }
}
