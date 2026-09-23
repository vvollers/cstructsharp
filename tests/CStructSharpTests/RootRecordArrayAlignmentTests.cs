namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks root array aliases place their records at the configured stream alignment.</summary>
[TestClass]
public class RootRecordArrayAlignmentTests
{
    /// <summary>An aligned root array advances past an odd input position before reading its first record.</summary>
    [TestMethod]
    public void RootArray_AlignsItsFirstRecord()
    {
        var layout = new CStruct("struct item { uint8 first; uint16 second; }; typedef item pair[2];", aligned: true);
        using var source = new MemoryStream(new byte[] { 0, 0xEE, 0xA1, 0, 0xB2, 0xC3, 0xD4, 0, 0xE5, 0xF6, });
        source.Position = 1;
        StructValue[] values = layout.ReadValue<StructValue[]>(source, "pair");
        Assert.HasCount(2, values);
        Assert.AreEqual((byte)0xA1, values[0]["first"]);
        Assert.AreEqual((ushort)0xC3B2, values[0]["second"]);
        Assert.AreEqual((byte)0xD4, values[1]["first"]);
        Assert.AreEqual((ushort)0xF6E5, values[1]["second"]);
        Assert.AreEqual(10L, source.Position);
    }
}
