namespace CStructSharpTests;

using CStructSharp;

/// <summary>Verifies three-byte integer storage independently of alignment.</summary>
[TestClass]
public class Int24Tests
{
    /// <summary>Decoded three-byte counts and pointer targets participate in ordinary layout operations.</summary>
    [TestMethod]
    public void CountsAndPointerTargets_UseNumericValues()
    {
        var parser = new CStruct("struct cell { int24 value; }; struct root { uint24 count; cell items[count]; cell *target; };", pointerSize: 1, aligned: false);
        byte[] bytes = [1, 0, 0, 0xff, 0xff, 0x7f, 7, 0xff, 0xff, 0xff];
        using var stream = new MemoryStream(bytes);
        (List<DebugData> debug, dynamic parsed) = parser.ParseStreamWithDebug(stream, "root");
        Assert.AreEqual(8388607, (int)parsed.root.items[0].value);
        Assert.AreEqual(-1, (int)parsed.root.target.Value.value);
        DebugData entry = debug.Single(item => item.DebugStackString == "root.target.value");
        Assert.AreEqual(7L, entry.CurPos);
        Assert.AreEqual(10L, entry.EndPos);
        stream.Position = 0;
        Assert.AreEqual(1, parser.GetDynamicArrayLength(stream, "root.items"));
        stream.Position = 0;
        Assert.AreEqual(-1, parser.ReadValue<int>(stream, "root.target.value.value"));
    }

    /// <summary>Signed extrema, byte order, array stride and selected updates share one storage contract.</summary>
    [TestMethod]
    public void StorageAndSelectedOperations_RoundTrip()
    {
        var parser = new CStruct("typedef uint24 triple; struct root { uint8 prefix; int24> low; triple values[2]; uint8 tail; };", aligned: true);
        byte[] bytes = [42, 0x80, 0, 0, 0xff, 0xff, 0xff, 0x56, 0x34, 0x12, 99];
        using var stream = new MemoryStream(bytes);
        (List<DebugData> debug, dynamic parsed) = parser.ParseStreamWithDebug(stream, "root");
        Assert.AreEqual(-8388608, (int)parsed.root.low);
        Assert.AreEqual(0xffffffU, (uint)parsed.root.values[0]);
        Assert.AreEqual(0x123456U, (uint)parsed.root.values[1]);
        Assert.AreEqual(11, parser.GetStructSizeInBytes("root"));
        CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed.root));
        using var written = new MemoryStream();
        parser.WriteStream(written, "root", parsed.root);
        CollectionAssert.AreEqual(bytes, written.ToArray());
        DebugData entry = debug.Single(item => item.DebugStackString == "root.low");
        Assert.AreEqual(1L, entry.CurPos);
        Assert.AreEqual(4L, entry.EndPos);
        stream.Position = 0;
        Assert.AreEqual(7L, parser.ResolveAddress(stream, "root.values[1]"));
        stream.Position = 0;
        parser.UpdateStream(stream, "root.values[1]", 0xabcdefU);
        stream.Position = 0;
        Assert.AreEqual(0xabcdefU, parser.ReadValue<uint>(stream, "root.values[1]"));
        byte[] before = stream.ToArray();
        stream.Position = 0;
        Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.low", 8388608));
        CollectionAssert.AreEqual(before, stream.ToArray());
    }

    /// <summary>Every spelling uses three bytes and preserves its signed domain.</summary>
    [TestMethod]
    public void EndianAndRangeBoundaries_AreExact()
    {
        foreach (string suffix in new[] { string.Empty, "<", ">" })
        {
            var parser = new CStruct($"struct root {{ int24{suffix} value; }};", aligned: false);
            foreach (int value in new[] { -8388608, -1, 0, 1, 8388607 })
            {
                byte[] bytes = parser.Serialize("root", new { value });
                Assert.AreEqual(3, bytes.Length);
                using var stream = new MemoryStream(bytes);
                Assert.AreEqual(value, parser.ReadValue<int>(stream, "root.value"));
            }

            Assert.Throws<CStructWriteException>(() => parser.Serialize("root", new { value = -8388609 }));
            Assert.Throws<CStructReadException>(() => parser.ParseStream(new MemoryStream(new byte[2]), "root"));
        }

        var unsigned = new CStruct("struct root { uint24 value; };");
        Assert.Throws<CStructWriteException>(() => unsigned.Serialize("root", new { value = 0x1000000U }));
        Assert.Throws<CStructLayoutException>(() => new CStruct("enum kind : uint24 { A = 1 };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint24 value : 3; };"));
    }
}
