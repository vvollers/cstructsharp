namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks an enum array does not publish its final member as a scalar layout count.</summary>
[TestClass]
public class EnumArrayCaptureBoundaryTests
{
    /// <summary>An existing count remains available when the equally named decoded field is an enum array.</summary>
    [TestMethod]
    public void EnumArray_DoesNotReplaceScalarCount()
    {
        var layout = new CStruct("#define count 1\nenum kind : uint8 { first = 1, second = 2 }; struct root { kind count[2]; uint8 data[count]; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 1, 2, 0xA5, 0xB6, });
        StructValue result = layout.Parse(source, "root");
        Assert.AreEqual((byte)0xB6, result["tail"]);
        Assert.AreEqual(4L, source.Position);
    }
}
