namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks enum-root reads from an unaligned position when layout alignment is disabled.</summary>
[TestClass]
public class ReaderEnumOriginTests
{
    /// <summary>Direct and aliased enum roots begin at the supplied stream position, not the next natural boundary.</summary>
    /// <param name="root">The exported enum or its typedef alias.</param>
    [TestMethod]
    [DataRow("kind")]
    [DataRow("alias")]
    public void UnalignedEnumRoot_PreservesTheRequestedOrigin(string root)
    {
        var layout = new CStruct("enum kind : uint16 { VALUE = 0x1234 }; typedef kind alias;", aligned: false);
        using var source = new MemoryStream(new byte[] { 99, 0x34, 0x12, 0x56, }) { Position = 1, };

        var value = (EnumValueResult)layout.ReadValue(source, root)!;

        Assert.AreEqual("VALUE", value.Name);
        Assert.AreEqual(3L, source.Position);
    }
}
