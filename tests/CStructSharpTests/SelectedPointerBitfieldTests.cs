namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks that selecting pointer storage cannot seed a fake bitfield unit in its union target.</summary>
[TestClass]
public class SelectedPointerBitfieldTests
{
    /// <summary>A selected pointer reads each union bitfield from the target's actual storage unit.</summary>
    /// <param name="rawFirst">Whether the ordinary byte view precedes the bitfield view.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectedPointer_UsesTheUnionBitfieldStorage(bool rawFirst)
    {
        string members = rawFirst ? "uint8 raw; uint8 bits : 3;" : "uint8 bits : 3; uint8 raw;";
        var layout = new CStruct("union choice { " + members + " }; struct root { choice *item; };", pointerSize: 1);
        using var source = new MemoryStream(new byte[] { 1, 5, });
        var pointer = (Pointer)layout.ReadValue(source, "root.item")!;
        Assert.AreEqual(1L, pointer.Address);
        Assert.IsTrue(pointer.IsDereferenced);
        var target = (UnionValue)pointer.Value!;
        Assert.AreEqual(5, target.Get<int>("bits"));
        Assert.AreEqual((byte)5, target.Get<byte>("raw"));
        Assert.AreEqual(1L, source.Position);
    }
}
