namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks address traversal across rows whose encoded elements do not have a fixed byte size.</summary>
[TestClass]
public class SelectedDynamicArrayTests
{
    /// <summary>A packed count uses its placed storage window, byte order and independent bit direction.</summary>
    /// <param name="littleEndian">Whether the storage bytes use little-endian order.</param>
    /// <param name="highBitFirst">Whether the count occupies the high bits of its storage unit.</param>
    /// <param name="countHex">The encoded count: forward-filling units use two bytes; reverse-filling units retain four.</param>
    [TestMethod]
    [DataRow(true, false, "0300")]
    [DataRow(false, false, "00000003")]
    [DataRow(true, true, "00008001")]
    [DataRow(false, true, "0180")]
    public void PackedCount_ControlsFollowingArrayPlacement(bool littleEndian, bool highBitFirst, string countHex)
    {
        var layout = new CStruct(
            "struct root { uint32 count:9; uint8 values[count]; uint8 tail; };",
            isLittleEndian: littleEndian,
            compilationOptions: new CStructCompilationOptions
            {
                BitfieldAllocation = highBitFirst ? BitfieldAllocation.HighBitFirst : BitfieldAllocation.LowBitFirst,
            });
        byte[] bytes = [.. Convert.FromHexString(countHex), 11, 22, 33, 99,];

        Assert.AreEqual(3, layout.GetArrayLength(bytes, "root.values"));
        Assert.AreEqual((long)(bytes.Length - 1), layout.ResolveAddress(bytes, "root.tail"));
        Assert.AreEqual((byte)33, layout.ReadValue<byte>(bytes, "root.values[2]"));
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(bytes, "root.tail"));
    }

    /// <summary>Resolving a row skips every preceding dynamic record and preserves the selected row's array shape.</summary>
    [TestMethod]
    public void DynamicRecordRows_KeepEveryLeafAndTheRemainingDimension()
    {
        var layout = new CStruct("struct item { uint8 count; uint8 values[count]; }; struct root { item cells[2][2]; uint8 tail; };");
        byte[] bytes = [1, 11, 2, 21, 22, 3, 31, 32, 33, 1, 41, 99,];

        Assert.AreEqual(5L, layout.ResolveAddress(bytes, "root.cells[1]"));
        Assert.AreEqual(9L, layout.ResolveAddress(bytes, "root.cells[1][1]"));
        Assert.AreEqual(11L, layout.ResolveAddress(bytes, "root.tail"));
        StructValue[] row = layout.ReadValue<StructValue[]>(bytes, "root.cells[1]");
        Assert.HasCount(2, row);
        Assert.AreEqual((byte)3, row[0].Get<byte>("count"));
        Assert.AreEqual((byte)33, row[0].Get<byte>("values[2]"));
        Assert.AreEqual((byte)41, row[1].Get<byte>("values[0]"));
        Assert.AreEqual((byte)41, layout.ReadValue<byte>(bytes, "root.cells[1][1].values[0]"));
    }

    /// <summary>Variable-length integer rows advance by the number of leaf values in every skipped row.</summary>
    [TestMethod]
    public void VariableIntegerRows_CountLeavesRatherThanRows()
    {
        var layout = new CStruct("struct root { uleb128 values[2][2]; uint8 tail; };");
        byte[] bytes = [1, 0x80, 1, 0x81, 1, 3, 99,];

        Assert.AreEqual(3L, layout.ResolveAddress(bytes, "root.values[1]"));
        Assert.AreEqual(5L, layout.ResolveAddress(bytes, "root.values[1][1]"));
        Assert.AreEqual(6L, layout.ResolveAddress(bytes, "root.tail"));
        Assert.AreEqual(129UL, layout.ReadValue<ulong>(bytes, "root.values[1][0]"));
        Assert.AreEqual(3UL, layout.ReadValue<ulong>(bytes, "root.values[1][1]"));
    }
}
