namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>Checks dynamic integer values and exact encoded extents.</summary>
[TestClass]
public class Leb128Tests
{
    /// <summary>LEB128 classification accepts exactly its four case-sensitive codec spellings.</summary>
    [TestMethod]
    public void CodecNames_RejectEmptyAndUnrelatedSpellings()
    {
        foreach (string name in new[] { "uleb128_32", "uleb128_64", "sleb128_32", "sleb128_64", })
        {
            Assert.IsTrue(CStructSharp.Codecs.Leb128Codec.IsType(name), name);
        }

        foreach (string name in new[] { string.Empty, "uint32", "leb128", "uleb128_16", "ULEB128_32", })
        {
            Assert.IsFalse(CStructSharp.Codecs.Leb128Codec.IsType(name), name);
        }
    }

    /// <summary>Variable-width arrays preserve selected positions and explain why an update cannot change encoded length.</summary>
    [TestMethod]
    public void DynamicArrays_ResolveActualExtents()
    {
        var parser = new CStruct("struct root { uleb128_32 count; sleb128_32 values[count]; uint8 tail; };", aligned: false);
        byte[] bytes = [3, 0x7f, 0x80, 1, 0x80, 0x7f, 99];
        using var stream = new MemoryStream(bytes);
        dynamic parsed = parser.Parse(stream, "root");
        Assert.AreEqual(-1, (int)parsed.values[0]);
        Assert.AreEqual(128, (int)parsed.values[1]);
        Assert.AreEqual(-128, (int)parsed.values[2]);
        Assert.AreEqual((byte)99, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed));
        stream.Position = 0;
        Assert.AreEqual(6L, parser.ResolveAddress(stream, "root.tail"));
        stream.Position = 0;
        Assert.AreEqual(-128, parser.ReadValue<int>(stream, "root.values[2]"));
        stream.Position = 0;
        parser.Update(stream, "root.values[1]", 129);
        stream.Position = 0;
        Assert.AreEqual(129, parser.ReadValue<int>(stream, "root.values[1]"));
        byte[] before = stream.ToArray();
        stream.Position = 0;

        // Replacing a two-byte integer with a one-byte encoding must explain the unchanged-extent rule.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => parser.Update(stream, "root.values[1]", 1));
        StringAssert.Contains(failure.Message, "updates must preserve the existing encoded byte length");
        CollectionAssert.AreEqual(before, stream.ToArray());
    }

    /// <summary>Padding is permitted only inside the declared width and terminal bits must agree with its sign.</summary>
    [TestMethod]
    public void WidthLimitsAndPadding_AreValidated()
    {
        var parser = new CStruct("struct root { uleb128_32 value; };", aligned: false);
        using var padded = new MemoryStream(new byte[] { 0x83, 0x80, 0x80, 0x80, 0 });
        Assert.AreEqual(3U, parser.ReadValue<uint>(padded, "root.value"));
        foreach (byte[] bytes in new byte[][] { [0x80], [0xff, 0xff, 0xff, 0xff, 0x10], [0x80, 0x80, 0x80, 0x80, 0x80, 0] })
        {
            Assert.Throws<CStructReadException>(() => parser.Parse(new MemoryStream(bytes), "root"));
        }

        foreach (long value in new[] { long.MinValue, -1L, 0L, 63L, 64L, long.MaxValue })
        {
            var signed = new CStruct("struct root { sleb128_64 value; };", aligned: false);
            byte[] bytes = signed.Serialize("root", new Dictionary<string, object?> { ["value"] = value });
            Assert.IsTrue(bytes.Length <= 10);
            Assert.AreEqual(value, signed.ReadValue<long>(new MemoryStream(bytes), "root.value"));
        }

        var unsigned = new CStruct("struct root { uleb128_64 value; };", aligned: false);
        byte[] maximum = unsigned.Serialize("root", new Dictionary<string, object?> { ["value"] = ulong.MaxValue });
        Assert.AreEqual(10, maximum.Length);
        Assert.AreEqual(ulong.MaxValue, unsigned.ReadValue<ulong>(new MemoryStream(maximum), "root.value"));
    }
}
