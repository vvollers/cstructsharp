namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks fixed-record arrays use absolute child placement when their field starts off the record boundary.</summary>
[TestClass]
public class MisalignedRecordArrayTests
{
    /// <summary>A smaller field alignment must not authorize relative-offset block decoding.</summary>
    [TestMethod]
    public void FieldAlignmentOverride_PreservesAbsoluteChildOffsets()
    {
        var layout = new CStruct("struct item { uint8 first; uint16 second; }; struct root { uint8 count; uint8 prefix[count]; item values[2] @align(1); uint8 tail; };", aligned: true, isLittleEndian: true);
        byte[] bytes = [0, 0xA1, 0xB2, 0xC3, 0xD4, 0xEE, 0x16, 0x27, 99, 0,];
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        var values = ((IEnumerable<object?>)parsed.values).Cast<StructValue>().ToArray();
        Assert.AreEqual((byte)0xA1, values[0]["first"]);
        Assert.AreEqual((ushort)0xC3B2, values[0]["second"]);
        Assert.AreEqual((byte)0xD4, values[1]["first"]);
        Assert.AreEqual((ushort)0x2716, values[1]["second"]);
        Assert.AreEqual((byte)99, (byte)parsed.tail);
    }
}
