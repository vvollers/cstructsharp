namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>
///     Arrays whose count comes from the data: <c>T values[EOF]</c> (every whole element to the end of the input)
///     and <c>T values[]</c> on a non-character type (elements until an all-zero element).
/// </summary>
[TestClass]
public class DataSizedArrayTests
{
    /// <summary><c>[EOF]</c> reads every whole element that remains, in memory and in a stream, and rejects a partial one.</summary>
    [TestMethod]
    public void ToEnd_ReadsEveryRemainingElement()
    {
        var layout = new CStruct("struct root { uint8 magic; uint16 values[EOF]; };");
        byte[] bytes = [7, 1, 0, 2, 0, 3, 0,];
        dynamic value = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual((byte)7, (byte)value.magic);
        CollectionAssert.AreEqual(new ushort[] { 1, 2, 3, }, ((IEnumerable<object?>)value.values).Select(item => (ushort)item!).ToArray());

        using var stream = new MemoryStream(bytes);
        dynamic streamed = layout.ParseStream(stream, "root");
        Assert.AreEqual(3, ((IEnumerable<object?>)streamed.values).Count());
        Assert.AreEqual(7, stream.Position);

        dynamic empty = layout.Parse(new byte[] { 7, }.AsSpan(), "root");
        Assert.IsEmpty((IEnumerable<object?>)empty.values);

        Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 7, 1, 0, 2, }.AsSpan(), "root"));
        Assert.Throws<CStructReadLimitException>(() => layout.Parse(bytes.AsSpan(), "root", options: new ReadOptions { MaxArrayElements = 2, }));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 magic; cstring values[EOF]; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 values[EOF][2]; };"));
    }

    /// <summary>A layout that defines <c>EOF</c> itself keeps the ordinary count meaning.</summary>
    [TestMethod]
    public void ToEnd_YieldsToAnExplicitDefine()
    {
        var layout = new CStruct("#define EOF 2\nstruct root { uint8 values[EOF]; uint8 tail; };");
        Assert.AreEqual(3, layout.GetStructSizeInBytes("root"));
    }

    /// <summary>Structs and byte payloads read to the end, through every operation.</summary>
    [TestMethod]
    public void ToEnd_RoundTripsThroughEveryOperation()
    {
        var layout = new CStruct("struct entry { uint8 kind; uint8 size; }; struct root { uint16 header; entry entries[EOF]; };");
        byte[] bytes = [0x34, 0x12, 1, 10, 2, 20,];
        using var stream = new MemoryStream((byte[])bytes.Clone());

        List<DebugData> debug = layout.ParseStreamWithDebug(stream, "root").DebugData;
        stream.Position = 0;
        dynamic parsed = layout.ParseStream(stream, "root");
        stream.Position = 0;
        Assert.AreEqual((byte)20, (byte)parsed.entries[1].size);
        Assert.IsTrue(debug.Any(item => item.DebugStackString == "root.entries[1].size" && item.CurPos == 5));
        Assert.AreEqual(2, layout.GetDynamicArrayLength(stream, "root.entries"));
        Assert.AreEqual(4, layout.ResolveAddress(stream, "root.entries[1]"));
        Assert.Throws<CStructPathException>(() => layout.ResolveAddress(stream, "root.entries[2]"));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));

        using var written = new MemoryStream();
        layout.WriteStream(
            written,
            "root",
            new Dictionary<string, object?>
            {
                ["header"] = (ushort)0x1234,
                ["entries"] = new object[]
                {
                    new Dictionary<string, object?> { ["kind"] = (byte)1, ["size"] = (byte)10, },
                    new Dictionary<string, object?> { ["kind"] = (byte)2, ["size"] = (byte)20, },
                },
            });
        CollectionAssert.AreEqual(bytes, written.ToArray());

        layout.UpdateStream(stream, "root.entries[0].size", (byte)11);
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, 1, 11, 2, 20, }, stream.ToArray());
        Assert.AreEqual((byte)11, layout.ReadValue<byte>(stream.ToArray().AsSpan(), "root.entries[0].size"));

        Root typed = layout.ReadValue<Root>(stream.ToArray().AsSpan(), "root");
        Assert.AreEqual(2, typed.Entries.Length);
        Assert.AreEqual((byte)2, typed.Entries[1].Kind);
    }

    /// <summary><c>T values[]</c> on a non-character type reads until an all-zero element and consumes it.</summary>
    [TestMethod]
    public void Terminated_ReadsUntilAZeroElement()
    {
        var layout = new CStruct("struct root { uint16 values[]; uint8 tail; };");
        byte[] bytes = [5, 0, 6, 0, 0, 0, 9,];
        dynamic value = layout.Parse(bytes.AsSpan(), "root");
        CollectionAssert.AreEqual(new ushort[] { 5, 6, }, ((IEnumerable<object?>)value.values).Select(item => (ushort)item!).ToArray());
        Assert.AreEqual((byte)9, (byte)value.tail);

        dynamic empty = layout.Parse(new byte[] { 0, 0, 9, }.AsSpan(), "root");
        Assert.IsEmpty((IEnumerable<object?>)empty.values);
        Assert.AreEqual((byte)9, (byte)empty.tail);

        Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 5, 0, 6, 0, }.AsSpan(), "root"));
        Assert.Throws<CStructReadLimitException>(() => layout.Parse(bytes.AsSpan(), "root", options: new ReadOptions { MaxArrayElements = 1, }));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct e { uint8 n; uint8 v[n]; }; struct root { e items[]; };"));
    }

    /// <summary>A terminated struct array (the exFAT/APFS shape) through every operation, including the terminator on write.</summary>
    [TestMethod]
    public void Terminated_StructArrayRoundTrips()
    {
        var layout = new CStruct("struct entry { uint8 kind; uint8 size; }; struct root { entry entries[]; uint8 tail; };");
        byte[] bytes = [1, 10, 2, 20, 0, 0, 9,];
        using var stream = new MemoryStream((byte[])bytes.Clone());

        List<DebugData> debug = layout.ParseStreamWithDebug(stream, "root").DebugData;
        stream.Position = 0;
        dynamic parsed = layout.ParseStream(stream, "root");
        stream.Position = 0;
        Assert.AreEqual((byte)9, (byte)parsed.tail);
        Assert.IsTrue(debug.Any(item => item.DebugStackString == "root.tail" && item.CurPos == 6));
        Assert.AreEqual(2, layout.GetDynamicArrayLength(stream, "root.entries"));
        Assert.AreEqual(2, layout.ResolveAddress(stream, "root.entries[1]"));
        Assert.AreEqual(6, layout.ResolveAddress(stream, "root.tail"));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));

        byte[] written = layout.Serialize(
            "root",
            new Dictionary<string, object?>
            {
                ["entries"] = new object[] { new Dictionary<string, object?> { ["kind"] = (byte)3, ["size"] = (byte)30, }, },
                ["tail"] = (byte)9,
            });
        CollectionAssert.AreEqual(new byte[] { 3, 30, 0, 0, 9, }, written);

        layout.UpdateStream(stream, "root.entries[1].kind", (byte)7);
        CollectionAssert.AreEqual(new byte[] { 1, 10, 7, 20, 0, 0, 9, }, stream.ToArray());
        layout.UpdateStream(stream, "root.tail", (byte)8);
        CollectionAssert.AreEqual(new byte[] { 1, 10, 7, 20, 0, 0, 8, }, stream.ToArray());
    }

    /// <summary>A data-sized array has no fixed size, so the containing struct has none either.</summary>
    [TestMethod]
    public void DataSizedArrays_HaveNoFixedSize()
    {
        var layout = new CStruct("struct root { uint16 values[EOF]; }; struct other { uint32 values[]; };");
        Assert.Throws<CStructLayoutException>(() => layout.GetStructSizeInBytes("root"));
        Assert.Throws<CStructLayoutException>(() => layout.GetStructSizeInBytes("other"));
        Assert.AreEqual(2, layout.GetStructAlignmentInBytes("root"));
    }

    private sealed class Root
    {
        public ushort Header { get; set; }

        public Entry[] Entries { get; set; } = [];
    }

    private sealed class Entry
    {
        public byte Kind { get; set; }

        public byte Size { get; set; }
    }
}
