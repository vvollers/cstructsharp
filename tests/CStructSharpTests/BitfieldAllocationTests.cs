namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary><see cref="CStructCompilationOptions.BitfieldAllocation"/>: which end of a storage unit the first bitfield takes.</summary>
[TestClass]
public class BitfieldAllocationTests
{
    private const string Layout = "struct root { uint16 a : 4; uint16 b : 12; uint8 c : 3; uint8 d : 5; };";

    /// <summary>High-bit-first allocation reads the dissect/RFC way: bytes <c>12 34</c> big-endian give a=1, b=0x234.</summary>
    [TestMethod]
    public void HighBitFirst_TakesTheTopBitsFirst()
    {
        var options = new CStructCompilationOptions { BitfieldAllocation = BitfieldAllocation.HighBitFirst, };
        var big = new CStruct(Layout, isLittleEndian: false, compilationOptions: options);
        var low = new CStruct(Layout, isLittleEndian: false);
        byte[] bytes = [0x12, 0x34, 0b101_00110,];

        dynamic high = big.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual(1, (int)high.a);
        Assert.AreEqual(0x234, (int)high.b);
        Assert.AreEqual(0b101, (int)high.c);
        Assert.AreEqual(0b00110, (int)high.d);

        dynamic conventional = low.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual(4, (int)conventional.a);
        Assert.AreEqual(0x123, (int)conventional.b);
        Assert.AreEqual(0b110, (int)conventional.c);

        // Storage-unit grouping and sizes are unaffected.
        Assert.AreEqual(3, big.GetStructSizeInBytes("root"));
        CollectionAssert.AreEqual(bytes, big.Serialize("root", high));
        byte[] written = big.Serialize("root", new Dictionary<string, object?> { ["a"] = 0xF, ["b"] = 0, ["c"] = 1, ["d"] = 0, });
        CollectionAssert.AreEqual(new byte[] { 0xF0, 0x00, 0b001_00000, }, written);

        using var stream = new MemoryStream((byte[])bytes.Clone());
        big.UpdateStream(stream, "root.a", 0xA);
        Assert.AreEqual(0xA2, stream.ToArray()[0]);
        Assert.AreEqual(0x234, big.ReadValue<int>(stream.ToArray().AsSpan(), "root.b"));
        Assert.AreEqual(0b101, big.ReadValue<int>(stream.ToArray().AsSpan(), "root.c"));

        Assert.AreNotSame(CStruct.GetOrCompile(Layout), CStruct.GetOrCompile(Layout, compilationOptions: options));
    }

    /// <summary>Debug ranges and addresses describe the storage unit, which the allocation order does not move; a stream write matches serialize.</summary>
    [TestMethod]
    public void HighBitFirst_KeepsUnitPlacement()
    {
        var options = new CStructCompilationOptions { BitfieldAllocation = BitfieldAllocation.HighBitFirst, };
        var layout = new CStruct(Layout, isLittleEndian: false, compilationOptions: options);
        byte[] bytes = [0x12, 0x34, 0x26,];
        using var stream = new MemoryStream(bytes);

        List<DebugData> debug = layout.ParseStreamWithDebug(stream, "root").DebugData;
        Assert.IsTrue(debug.Any(item => item.DebugStackString == "root.b" && item.CurPos == 0 && item.EndPos == 2));
        Assert.IsTrue(debug.Any(item => item.DebugStackString == "root.d" && item.CurPos == 2 && item.EndPos == 3));
        stream.Position = 0;
        Assert.AreEqual(0, layout.ResolveAddress(stream, "root.b"));
        Assert.AreEqual(2, layout.ResolveAddress(stream, "root.c"));

        using var target = new MemoryStream();
        layout.WriteStream(target, "root", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 0x234, ["c"] = 1, ["d"] = 6, });
        CollectionAssert.AreEqual(bytes, target.ToArray());
    }
}
