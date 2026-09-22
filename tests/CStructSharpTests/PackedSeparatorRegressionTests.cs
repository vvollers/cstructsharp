namespace CStructSharp.Tests;

/// <summary>Checks that packed SysV separators start an independently bounded run for the following bitfields.</summary>
[TestClass]
public class PackedSeparatorRegressionTests
{
    /// <summary>A leading separator after ordinary bytes aligns a wider field without reading outside its packed run.</summary>
    /// <param name="prefixSize">The ordinary prefix length in bytes.</param>
    /// <param name="valueOffset">The aligned bitfield byte offset.</param>
    [TestMethod]
    [DataRow(1, 4)]
    [DataRow(3, 4)]
    [DataRow(5, 8)]
    public void LeadingSeparator_WiderStorageStartsAtItsAlignedByte(int prefixSize, int valueOffset)
    {
        var layout = new CStruct($"struct root {{ uint8 prefix[{prefixSize}]; uint32 :0; uint64 value:1; }};");
        byte[] bytes = new byte[valueOffset + 1];
        bytes.AsSpan(0, prefixSize).Fill(99);
        bytes[valueOffset] = 1;
        Assert.AreEqual(bytes.Length, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual((long)valueOffset, layout.ResolveAddress(bytes, "root.value"));
        dynamic parsed = layout.Parse(bytes, "root");
        Assert.AreEqual(1UL, Convert.ToUInt64(parsed.value));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));

        using var stream = new MemoryStream((byte[])bytes.Clone());
        layout.Update(stream, "root.value", 0UL);
        bytes[valueOffset] = 0;
        CollectionAssert.AreEqual(bytes, stream.ToArray());
    }

    /// <summary>A separator between narrow and wide fields ends the first run before starting the aligned second one.</summary>
    [TestMethod]
    public void SeparatorBetweenRuns_PreservesBothValues()
    {
        var layout = new CStruct("struct root { uint8 prefix; uint8 before:1; uint32 :0; uint64 after:1; };");
        byte[] bytes = [99, 1, 0, 0, 1,];
        dynamic parsed = layout.Parse(bytes, "root");
        Assert.AreEqual(1, Convert.ToInt32(parsed.before));
        Assert.AreEqual(1UL, Convert.ToUInt64(parsed.after));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
    }
}
