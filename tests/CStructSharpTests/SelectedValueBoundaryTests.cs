namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks natural value shapes and stream extents when reading individual paths.</summary>
[TestClass]
public class SelectedValueBoundaryTests
{
    /// <summary>One accessor of a double pointer returns the remaining pointer, not its final scalar target.</summary>
    [TestMethod]
    public void DoublePointer_PreservesRemainingPointerLevel()
    {
        var layout = new CStruct("struct root { uint8 **target; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 1, 2, 0xA5, });
        Pointer pointer = layout.ReadValue<Pointer>(stream, "root.target.value");
        Assert.AreEqual(2L, pointer.Address);
        Assert.AreEqual(1, pointer.Depth);
        Assert.IsTrue(pointer.IsDereferenced);
        Assert.AreEqual((byte)0xA5, pointer.Value);

        stream.Position = 0;
        Assert.AreEqual((byte)0xA5, layout.ReadValue<byte>(stream, "root.target.value.value"));
    }

    /// <summary>A composite array and its selected element retain their distinct collection and record shapes.</summary>
    [TestMethod]
    public void CompositeArray_SeparatesWholeArrayFromOneElement()
    {
        var layout = new CStruct("struct item { uint8 value; }; struct root { item items[2]; uint8 tail; };");
        using var stream = new MemoryStream(new byte[] { 11, 22, 33, });
        StructValue[] items = layout.ReadValue<StructValue[]>(stream, "root.items");
        Assert.HasCount(2, items);
        Assert.AreEqual((byte)11, items[0]["value"]);
        Assert.AreEqual((byte)22, items[1]["value"]);
        Assert.AreEqual(2L, stream.Position);

        stream.Position = 0;
        StructValue selected = layout.ReadValue<StructValue>(stream, "root.items[1]");
        Assert.AreEqual((byte)22, selected["value"]);
        Assert.AreEqual(2L, stream.Position);
    }

    /// <summary>A selected bitfield consumes its complete storage unit after a nonzero field offset.</summary>
    [TestMethod]
    public void SelectedBitfield_CompletesItsStorageExtent()
    {
        var layout = new CStruct("struct root { uint8 prefix; uint16 first:3; uint16 second:13; uint8 tail; };");
        using var stream = new MemoryStream(new byte[] { 0x99, 0xFF, 0xFF, 0x55, });
        Assert.AreEqual((ushort)8191, layout.ReadValue<ushort>(stream, "root.second"));
        Assert.AreEqual(3L, stream.Position);
        Assert.AreEqual(0x55, stream.ReadByte());
    }
}
