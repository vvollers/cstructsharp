namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Distinguishes supported arrays of pointer aliases from unsupported pointer-to-array aliases.</summary>
[TestClass]
public class PointerArrayAliasBoundaryTests
{
    /// <summary>An array of an existing scalar pointer alias retains the pointer storage at every element.</summary>
    [TestMethod]
    public void ArrayOfPointerAliases_PreservesAddressesAndTargets()
    {
        var layout = new CStruct("typedef uint8 *address; typedef address pair[2]; struct root { pair values; };", pointerSize: 1);
        using var source = new MemoryStream(new byte[] { 2, 3, 0xA5, 0xB6, });
        Pointer[] values = layout.ReadValue<Pointer[]>(source, "root.values");
        Assert.HasCount(2, values);
        Assert.AreEqual(2L, values[0].Address);
        Assert.AreEqual(3L, values[1].Address);
        Assert.AreEqual((byte)0xA5, values[0].Value);
        Assert.AreEqual((byte)0xB6, values[1].Value);
        Assert.AreEqual(2L, source.Position);
    }

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
