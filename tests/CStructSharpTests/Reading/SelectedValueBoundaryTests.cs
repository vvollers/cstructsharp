namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks natural value shapes and stream extents when reading individual paths.</summary>
[TestClass]
public class SelectedValueBoundaryTests
{
    /// <summary>A selected union member reached through a pointer uses its resolved address even when that address is unaligned.</summary>
    /// <param name="address">The odd pointer target address.</param>
    /// <param name="member">The scalar or bitfield view selected inside the union.</param>
    /// <param name="expected">The value stored at that exact address.</param>
    [TestMethod]
    [DataRow(1, "low", 5)]
    [DataRow(3, "low", 5)]
    [DataRow(1, "raw", 46757)]
    [DataRow(3, "raw", 46757)]
    [DataRow(1, "label", 46757)]
    [DataRow(3, "label", 46757)]
    public void UnalignedUnionSelection_UsesTheResolvedAddress(int address, string member, int expected)
    {
        var layout = new CStruct("enum mode : uint16 { A=46757 }; union choice { uint16 low:3; uint16 raw; mode label; }; struct root { choice *target; };", pointerSize: 1, aligned: true);
        byte[] bytes = new byte[address + 3];
        bytes[0] = (byte)address;
        bytes[address] = 0xA5;
        bytes[address + 1] = 0xB6;
        bytes[address + 2] = 0x5A;
        using var stream = new MemoryStream(bytes);
        string path = "root.target.value." + member;
        Assert.AreEqual((long)address, layout.ResolveAddress(stream, path));
        stream.Position = 0;
        Assert.AreEqual(expected, layout.ReadValue<int>(stream, path));
        Assert.AreEqual(expected, layout.ReadValue<int>(bytes.AsSpan(), path));

        stream.Position = 0;
        UnionValue debugUnion = Assert.IsInstanceOfType<UnionValue>(layout.ReadValueWithDebug(stream, "root.target.value").Value);
        object? debugValue = debugUnion[member];
        Assert.IsNotNull(debugValue);
        int debugNumber = debugValue is EnumValueResult enumeration ? (int)enumeration.Value : Convert.ToInt32(debugValue);
        Assert.AreEqual(expected, debugNumber);
    }

    /// <summary>A selected enum array aligns its first byte once, not every element to the field's alignment override.</summary>
    [TestMethod]
    public void SelectedEnumArray_AlignsOnlyTheFieldStart()
    {
        var layout = new CStruct("enum mode : uint8 { A=1, B=2 }; struct root { mode values[2] @align(4); uint8 tail; };", aligned: true);
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 0, });
        EnumValueResult[] values = layout.ReadValue<EnumValueResult[]>(stream, "root.values");
        Assert.HasCount(2, values);
        Assert.AreEqual("A", values[0].Name);
        Assert.AreEqual("B", values[1].Name);
        Assert.AreEqual(2L, stream.Position);
        Assert.AreEqual(3, stream.ReadByte());
    }

    /// <summary>A selected pointer array stores consecutive pointer numbers even when its field has a larger alignment override.</summary>
    [TestMethod]
    public void SelectedPointerArray_AlignsOnlyTheFieldStart()
    {
        var layout = new CStruct("struct root { uint8 *values[2] @align(4); uint8 tail; };", pointerSize: 1, aligned: true);
        using var stream = new MemoryStream(new byte[] { 3, 4, 0xEE, 0xA1, 0xB2, });
        Pointer[] values = layout.ReadValue<Pointer[]>(stream, "root.values");
        Assert.HasCount(2, values);
        Assert.AreEqual(3L, values[0].Address);
        Assert.AreEqual(4L, values[1].Address);
        Assert.AreEqual((byte)0xA1, values[0].Value);
        Assert.AreEqual((byte)0xB2, values[1].Value);
        Assert.AreEqual(2L, stream.Position);
    }

    /// <summary>A selected composite array keeps its resolved union start while each record places its own child fields.</summary>
    [TestMethod]
    public void SelectedCompositeArray_KeepsItsUnionStart()
    {
        var layout = new CStruct("struct item { uint8 first; uint16 second; }; union choice { item entries[2]; uint64 raw; }; struct root { choice *target; };", pointerSize: 1, aligned: true);
        using var stream = new MemoryStream(new byte[] { 1, 0xA1, 0xB2, 0xC3, 0xD4, 0xE5, 0x16, 0x27, 0x38, });
        StructValue[] values = layout.ReadValue<StructValue[]>(stream, "root.target.value.entries");
        Assert.HasCount(2, values);
        Assert.AreEqual((byte)0xA1, values[0]["first"]);
        Assert.AreEqual((ushort)0xC3B2, values[0]["second"]);
        Assert.AreEqual((byte)0xD4, values[1]["first"]);
        Assert.AreEqual((ushort)0x2716, values[1]["second"]);
        Assert.AreEqual(8L, stream.Position);
    }

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
