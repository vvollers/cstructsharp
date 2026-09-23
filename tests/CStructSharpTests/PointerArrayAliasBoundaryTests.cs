namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks pointer-bearing array aliases fail with a shape-specific diagnostic.</summary>
[TestClass]
public class PointerArrayAliasBoundaryTests
{
    /// <summary>An intervening pointer alias cannot erase an unsupported array target, even if unused.</summary>
    /// <param name="chained">Whether another ordinary alias separates the pointer from the field.</param>
    /// <param name="used">Whether a field instantiates the pointer alias.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void PointerAlias_CannotHideAnArrayTarget(bool chained, bool used)
    {
        string aliases = chained ? "typedef pair *middle; typedef middle link;" : "typedef pair *link;";
        string member = used ? "link value;" : "uint8 value;";
        Assert.Throws<CStructLayoutException>(() =>
            new CStruct("typedef uint8 pair[2]; " + aliases + " struct root { " + member + " };"));
    }

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
