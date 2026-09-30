namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>
///     Verifies where aligned placement is measured from: a struct's members are aligned relative to the struct's own
///     first byte, as C places them. So a record read from a stream that starts at any position - odd included - reads
///     exactly the values a span of the same bytes reads, and the positions it reports (addresses, debug ranges, the
///     final stream position) are the span's shifted by the start. A pointer target placed at an unaligned address is
///     read the same way.
/// </summary>
[TestClass]
public class AlignmentOriginTests
{
    /// <summary>The stream positions the record starts at.</summary>
    private static readonly int[] Starts = [1, 2, 3, 5];

    /// <summary>The execution paths that must agree.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.NoFastPaths];

    /// <summary>Gets the aligned layouts: a name, the definition (root <c>rec</c>), a field path, and a value to write.</summary>
    public static IEnumerable<object[]> Layouts =>
    [
        ["byte then word", "struct rec { uint8 a; uint32 b; };", "rec.b", new Dictionary<string, object?> { ["a"] = (byte)1, ["b"] = 0x11223344u, }],
        [
            "nested struct and bitfields",
            "struct inner { uint16 x; uint8 y; }; struct rec { uint8 a; inner i; uint32 lo : 4; uint32 hi : 4; uint8 t; };",
            "rec.t",
            new Dictionary<string, object?> { ["a"] = (byte)1, ["i"] = new Dictionary<string, object?> { ["x"] = (ushort)2, ["y"] = (byte)3, }, ["lo"] = 4u, ["hi"] = 5u, ["t"] = (byte)6, }
        ],
        [
            "runtime-sized array",
            "struct rec { uint8 n; uint16 v[n]; uint8 t; uint32 w; };",
            "rec.w",
            new Dictionary<string, object?> { ["n"] = (byte)2, ["v"] = new ushort[] { 7, 8, }, ["t"] = (byte)9, ["w"] = 0xAABBCCDDu, }
        ],
    ];

    /// <summary>
    ///     A record at an unaligned stream position - in a hidden-buffer, exposed-buffer and 3-byte chunked memory stream
    ///     and a file - reads the span's value, debug ranges and field address shifted by the start, and ends right
    ///     after the record; writing and updating it there writes the span's bytes.
    /// </summary>
    /// <param name="name">The case name.</param>
    /// <param name="definition">The layout source.</param>
    /// <param name="field">A field path to select, address and update.</param>
    /// <param name="value">The record's value.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void RecordAtAnyStreamPosition_ReadsLikeTheSpan(string name, string definition, string field, Dictionary<string, object?> value)
    {
        var layout = new CStruct(definition, aligned: true);
        byte[] record = layout.Serialize("rec", value);
        var differences = new List<string>();
        foreach (ExecutionPath path in Paths)
        {
            var read = new ReadOptions { ExecutionPath = path, };
            string spanValue = OperationOutcome.Render(layout.Parse(record.AsSpan(), "rec", null, read));
            ParseResult spanDebug = layout.ParseWithDebug(record.AsSpan(), "rec", null, read);
            string spanField = OperationOutcome.Render(layout.ReadValue(record.AsSpan(), field, null, read));
            long spanAddress = layout.ResolveAddress(record.AsSpan(), field, null, read);
            foreach (int start in Starts)
            {
                foreach (string kind in (string[])["hidden", "exposed", "chunked", "file"])
                {
                    string label = name + ", " + kind + " stream at " + start + " (" + path + ")";

                    // Each check reads its own stream positioned at the record.
                    void Check(string what, Func<Stream, string> actual, string expected)
                    {
                        using Stream stream = Open(kind, record, start);
                        try
                        {
                            string result = actual(stream);
                            if (result != expected)
                            {
                                differences.Add(label + ", " + what + ": expected " + expected + ", got " + result);
                            }
                        }
                        catch (Exception exception) when (exception is not AssertFailedException)
                        {
                            differences.Add(label + ", " + what + ": " + exception.Message);
                        }
                    }

                    Check("Parse", stream => OperationOutcome.Render(layout.Parse(stream, "rec", null, read)) + " @" + stream.Position, spanValue + " @" + (start + record.Length));
                    Check(
                        "ParseWithDebug",
                        stream => string.Join(",", layout.ParseWithDebug(stream, "rec", null, read).Debug.Select(entry => entry.Path + ":" + (entry.Start - start) + "-" + (entry.End - start))),
                        string.Join(",", spanDebug.Debug.Select(entry => entry.Path + ":" + entry.Start + "-" + entry.End)));
                    Check("ReadValue", stream => OperationOutcome.Render(layout.ReadValue(stream, field, null, read)), spanField);
                    Check("ResolveAddress", stream => layout.ResolveAddress(stream, field, null, read).ToString(System.Globalization.CultureInfo.InvariantCulture), (start + spanAddress).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                // A write at the position lays out the span's bytes, and an update there changes the span's field bytes.
                var write = new WriteOptions { ExecutionPath = path, };
                using (var destination = new MemoryStream())
                {
                    destination.Write(new byte[start]);
                    layout.Write(destination, "rec", value, null, write);
                    CollectionAssert.AreEqual(record, destination.ToArray()[start..], name + ", Write at " + start + " (" + path + ")");
                }

                byte[] spanUpdated = (byte[])record.Clone();
                layout.Update(spanUpdated.AsSpan(), field, (byte)0x42, null, new UpdateOptions { ExecutionPath = path, });
                using (var target = new MemoryStream())
                {
                    target.Write(Filler(start));
                    target.Write(record);
                    target.Position = start;
                    layout.Update(target, field, (byte)0x42, null, new UpdateOptions { ExecutionPath = path, });
                    CollectionAssert.AreEqual(spanUpdated, target.ToArray()[start..], name + ", Update at " + start + " (" + path + ")");
                }
            }
        }

        Assert.AreEqual(0, differences.Count, string.Join("\n", differences));
    }

    /// <summary>
    ///     A record sequence read from a stream that starts at an unaligned position reads the records a sequence of the
    ///     same bytes in memory reads.
    /// </summary>
    [TestMethod]
    public void RecordSequenceAtAnyStreamPosition_ReadsLikeMemory()
    {
        var layout = new CStruct("struct rec { uint8 a; uint32 b; };", aligned: true);
        byte[] records = [.. layout.Serialize("rec", new Dictionary<string, object?> { ["a"] = (byte)1, ["b"] = 2u, }), .. layout.Serialize("rec", new Dictionary<string, object?> { ["a"] = (byte)3, ["b"] = 4u, })];
        string expected = string.Join(";", layout.ParseMany(records.AsMemory(), "rec").Select(OperationOutcome.Render));
        foreach (int start in Starts)
        {
            using var stream = new MemoryStream([.. Filler(start), .. records]) { Position = start, };
            Assert.AreEqual(expected, string.Join(";", layout.ParseMany(stream, "rec").Select(OperationOutcome.Render)), "start " + start);
        }
    }

    /// <summary>
    ///     A pointer target at an unaligned address places its members from the target's own first byte:
    ///     <c>t.b</c> of a target at 3 is at 3 + 4 = 7, on every path, through a parse and a selected read.
    /// </summary>
    [TestMethod]
    public void PointerTargetAtAnUnalignedAddress_AlignsFromItsOwnStart()
    {
        var layout = new CStruct("struct t { uint8 a; uint32 b; }; struct rec { uint8 tag; t *p; };", pointerSize: 1, aligned: true);
        byte[] data = [0x09, 0x03, 0xEE, 0x11, 0xEE, 0xEE, 0xEE, 0x44, 0x33, 0x22, 0x11];
        foreach (ExecutionPath path in Paths)
        {
            var read = new ReadOptions { ExecutionPath = path, };
            StructValue value = layout.Parse(data.AsSpan(), "rec", null, read);
            var target = (StructValue)((Pointer)value["p"]!).Value!;
            Assert.AreEqual((byte)0x11, target["a"], path.ToString());
            Assert.AreEqual(0x11223344u, target["b"], path.ToString());
            Assert.AreEqual(0x11223344u, layout.ReadValue(data.AsSpan(), "rec.p.value.b", null, read), path.ToString());
            Assert.AreEqual(7L, layout.ResolveAddress(data.AsSpan(), "rec.p.value.b", null, read), path.ToString());
        }
    }

    /// <summary>
    ///     The example of <c>docs/language/layout-alignment-and-padding.md</c>: <c>sample</c> after three other bytes
    ///     reads the span's values, <c>b</c> resolves to 7, and the stream ends after the record's tail padding.
    /// </summary>
    [TestMethod]
    public void DocumentedExample_MatchesTheImplementation()
    {
        var layout = new CStruct("struct sample { uint8 a; uint32 b; uint16 c; };", aligned: true);
        byte[] bytes = [0xEE, 0xEE, 0xEE, 0x11, 0x00, 0x00, 0x00, 0x55, 0x44, 0x33, 0x22, 0x77, 0x66, 0x00, 0x00];
        using var stream = new MemoryStream(bytes) { Position = 3, };
        Assert.AreEqual(7L, layout.ResolveAddress(stream, "sample.b"));
        Assert.AreEqual(3L, stream.Position);
        StructValue value = layout.Parse(stream, "sample");
        Assert.AreEqual((byte)0x11, value["a"]);
        Assert.AreEqual(0x22334455u, value["b"]);
        Assert.AreEqual((ushort)0x6677, value["c"]);
        Assert.AreEqual(15L, stream.Position);
    }

    /// <summary>Opens a stream holding filler and then the record, positioned at the record.</summary>
    /// <param name="kind">The stream kind.</param>
    /// <param name="record">The record bytes.</param>
    /// <param name="start">The record's stream position.</param>
    /// <returns>The stream.</returns>
    private static Stream Open(string kind, byte[] record, int start)
    {
        byte[] bytes = [.. Filler(start), .. record];
        Stream stream = kind switch
        {
            "hidden" => new MemoryStream(bytes, writable: false),
            "exposed" => new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true),
            "chunked" => new ChunkedMemoryStream(bytes, 3, writable: false),
            _ => OpenFile(bytes),
        };
        stream.Position = start;
        return stream;
    }

    /// <summary>Writes the bytes to a temporary file that is deleted when the stream closes.</summary>
    /// <param name="bytes">The file content.</param>
    /// <returns>The open file.</returns>
    private static FileStream OpenFile(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), "cstructsharp-origin-" + Guid.NewGuid().ToString("N") + ".bin");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
        file.Write(bytes);
        return file;
    }

    /// <summary>Bytes before the record that a misplaced read would decode: <c>0xEE</c>.</summary>
    /// <param name="count">The number of bytes.</param>
    /// <returns>The filler.</returns>
    private static byte[] Filler(int count) => Enumerable.Repeat((byte)0xEE, count).ToArray();
}
