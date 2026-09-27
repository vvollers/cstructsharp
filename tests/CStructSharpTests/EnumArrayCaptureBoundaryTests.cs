namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks an enum array does not publish its final member as a scalar layout count.</summary>
[TestClass]
public class EnumArrayCaptureBoundaryTests
{
    /// <summary>
    ///     An enum array read under a name a definition also supplies makes that name unusable while its value is in
    ///     effect: the array is not an integer, and the definition's older value must not stand in for it.
    /// </summary>
    [TestMethod]
    public void EnumArray_MakesASharedNameUnusable()
    {
        var layout = new CStruct("#define count 1\nenum kind : uint8 { first = 1, second = 2 }; struct root { kind count[2]; uint8 data[count]; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 1, 2, 0xA5, 0xB6, });
        CStructReadException failure = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(source, "root"));
        StringAssert.Contains(failure.Message, "'count' is an array");
    }
}
