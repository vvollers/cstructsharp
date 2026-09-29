namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Verifies that aligned padding at or past the end of the input reads the same from every source. A struct's
///     storage includes its alignment padding, so input that ends inside padding - between fields or after the last
///     field - is truncated, and every source reports it as a span does: <see cref="ReadFailures.OutsideRegion"/> at the
///     same offset and path when the padding itself runs past the end, and the short read of the next field when the
///     input ends right after the padding. Input that ends exactly after tail padding reads completely everywhere.
/// </summary>
[TestClass]
public class AlignedPaddingAtEndTests
{
    /// <summary>The execution paths that must agree.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.GeneralOnly];

    /// <summary>The stream forms compared with the span.</summary>
    private static readonly EngineInput[] Streams = EngineStreams.All.Where(input => input != EngineInput.ExposedStream).ToArray();

    /// <summary>
    ///     Gets the cases: a name, the definition (root <c>rec</c>, aligned), the input, and the start of the span's
    ///     failure message, or <see langword="null"/> when the read succeeds.
    /// </summary>
    public static IEnumerable<object[]> Cases =>
    [
        ["padding between fields runs past the end", "struct rec { uint8 a; uint16 b; };", new byte[] { 0x01, }, ReadFailures.OutsideRegion[..^1]],
        ["the input ends right after padding between fields", "struct rec { uint8 a; uint16 b; };", new byte[] { 0x01, 0x00, }, ReadFailures.ShortReadPrefix],
        ["tail padding runs past the end", "struct rec { uint16 a; uint8 b; };", new byte[] { 0x01, 0x00, 0x02, }, ReadFailures.OutsideRegion[..^1]],
        ["a nested struct's tail padding runs past the end", "struct inner { uint16 a; uint8 b; }; struct rec { inner x; };", new byte[] { 0x01, 0x00, 0x02, }, ReadFailures.OutsideRegion[..^1]],
        ["a runtime-sized struct's tail padding runs past the end", "struct rec { uint16 n; uint8 d[n]; };", new byte[] { 0x01, 0x00, 0x07, }, ReadFailures.OutsideRegion[..^1]],
        ["tail padding ends exactly at the end", "struct rec { uint16 a; uint8 b; };", new byte[] { 0x01, 0x00, 0x02, 0x00, }, null!],
    ];

    /// <summary>
    ///     Parse, ParseWithDebug, ParseAsync and ReadValue of the root give the span's outcome - value or failure
    ///     (type, message, path, offset, inner failure) - from a byte array, memory, a multi-segment sequence, a
    ///     hidden-buffer memory stream, 1-, 3- and 7-byte chunked streams and a file; a stream that reads completely
    ///     ends after the tail padding.
    /// </summary>
    /// <param name="name">The case name.</param>
    /// <param name="definition">The layout source.</param>
    /// <param name="data">The input.</param>
    /// <param name="failure">The start of the span's failure message, or <see langword="null"/>.</param>
    [TestMethod]
    [DynamicData(nameof(Cases))]
    public void EverySource_ReadsLikeTheSpan(string name, string definition, byte[] data, string? failure)
    {
        var layout = new CStruct(definition, aligned: true);
        bool fails = failure is not null;
        int size = definition.Contains("d[n]", StringComparison.Ordinal) ? -1 : layout.GetStructSizeInBytes("rec");
        var differences = new List<string>();
        foreach (ExecutionPath path in Paths)
        {
            var read = new ReadOptions { ExecutionPath = path, };
            OperationOutcome span = OperationOutcome.Of(() => layout.Parse(data.AsSpan(), "rec", null, read));
            Assert.AreEqual(fails, span.Failure is not null, name + " (" + path + "): " + span.Failure?.Message);
            if (fails)
            {
                Assert.IsInstanceOfType<CStructReadException>(span.Failure);
                StringAssert.StartsWith(span.Failure!.Message, failure, name);
            }

            var sources = new List<(string Source, OperationOutcome Outcome)>
            {
                ("byte[]", OperationOutcome.Of(() => layout.Parse(data, "rec", null, read))),
                ("memory", OperationOutcome.Of(() => layout.Parse(data.AsMemory(), "rec", null, read))),
                ("sequence", OperationOutcome.Of(() => layout.Parse(ChunkedSequence.Of(data), "rec", null, read))),
                ("debug span", OperationOutcome.Of(() => layout.ParseWithDebug(data.AsSpan(), "rec", null, read).Value)),
                ("ReadValue span", OperationOutcome.Of(() => layout.ReadValue(data.AsSpan(), "rec", null, read))),
            };
            foreach (EngineInput input in Streams)
            {
                sources.Add((input.ToString(), OperationOutcome.Of(() => ParseStream(layout, data, input, read, fails ? -1 : size))));
                sources.Add((input + " debug", OperationOutcome.Of(() => ParseDebugStream(layout, data, input, read))));
                sources.Add((input + " ReadValue", OperationOutcome.Of(() => ReadValueStream(layout, data, input, read))));
            }

            sources.Add(("async", OperationOutcome.Of(() => ParseAsync(layout, data, read))));
            foreach ((string source, OperationOutcome outcome) in sources)
            {
                try
                {
                    OperationOutcome.AssertSame(span, outcome, name + ", " + source + " (" + path + ")");
                }
                catch (AssertFailedException exception)
                {
                    differences.Add(exception.Message);
                }
            }
        }

        Assert.AreEqual(0, differences.Count, string.Join("\n", differences));
    }

    /// <summary>
    ///     A record sequence whose last record's tail padding runs past the end fails at that record from memory, a
    ///     sequence and every stream alike; one that ends exactly after a record's padding reads every record.
    /// </summary>
    [TestMethod]
    public void RecordSequences_ReadLikeMemory()
    {
        var layout = new CStruct("struct rec { uint16 a; uint8 b; };", aligned: true);
        byte[][] inputs = [[1, 0, 2, 0, 3, 0, 4, 0], [1, 0, 2, 0, 3, 0, 4]];
        var differences = new List<string>();
        foreach (byte[] data in inputs)
        {
            foreach (ExecutionPath path in Paths)
            {
                var read = new ReadOptions { ExecutionPath = path, };
                OperationOutcome memory = OperationOutcome.Of(() => Records(layout.ParseMany(data.AsMemory(), "rec", null, read)));
                var sources = new List<(string Source, OperationOutcome Outcome)>
                {
                    ("sequence", OperationOutcome.Of(() => Records(layout.ParseMany(ChunkedSequence.Of(data), "rec", null, read)))),
                };
                foreach (EngineInput input in Streams)
                {
                    sources.Add((input.ToString(), OperationOutcome.Of(() =>
                    {
                        using Stream stream = EngineStreams.Open(input, data);
                        return Records(layout.ParseMany(stream, "rec", null, read));
                    })));
                }

                foreach ((string source, OperationOutcome outcome) in sources)
                {
                    try
                    {
                        OperationOutcome.AssertSame(memory, outcome, data.Length + " bytes, " + source + " (" + path + ")");
                    }
                    catch (AssertFailedException exception)
                    {
                        differences.Add(exception.Message);
                    }
                }
            }
        }

        Assert.AreEqual(0, differences.Count, string.Join("\n", differences));
    }

    /// <summary>
    ///     The example of <c>docs/language/layout-alignment-and-padding.md</c>: the aligned <c>sample</c> reads its 12
    ///     bytes, fails with <see cref="ReadFailures.OutsideRegion"/> without its last tail-padding byte and with only
    ///     its first byte, and fails at <c>b</c> with a short read when the input ends right after the padding before it.
    /// </summary>
    [TestMethod]
    public void DocumentedExample_MatchesTheImplementation()
    {
        var layout = new CStruct("struct sample { uint8 a; uint32 b; uint16 c; };", aligned: true);
        byte[] complete = [0x11, 0x00, 0x00, 0x00, 0x55, 0x44, 0x33, 0x22, 0x77, 0x66, 0x00, 0x00];
        Assert.AreEqual((ushort)0x6677, layout.Parse(complete.AsSpan(), "sample")["c"]);
        foreach (int length in (int[])[11, 1])
        {
            CStructReadException failure = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(complete.AsSpan(0, length), "sample"));
            StringAssert.StartsWith(failure.Message, ReadFailures.OutsideRegion[..^1]);
            using var stream = new MemoryStream(complete, 0, length, writable: false);
            Assert.AreEqual(failure.Message, Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(stream, "sample")).Message);
        }

        CStructReadException shortRead = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(complete.AsSpan(0, 4), "sample"));
        StringAssert.StartsWith(shortRead.Message, ReadFailures.ShortReadPrefix);
        StringAssert.Contains(shortRead.Message, "field 'b'");
    }

    /// <summary>
    ///     Writes and updates agree too: a new value includes its zeroed tail padding in every destination, and an
    ///     update of the last field of a record whose tail padding is missing changes that field from every source,
    ///     because an update reads only what locates and holds its target.
    /// </summary>
    [TestMethod]
    public void Writes_IncludeTailPaddingInEveryDestination()
    {
        var layout = new CStruct("struct rec { uint16 a; uint8 b; };", aligned: true);
        var value = new Dictionary<string, object?> { ["a"] = (ushort)1, ["b"] = (byte)2, };
        byte[] expected = [1, 0, 2, 0];
        foreach (ExecutionPath path in Paths)
        {
            var write = new WriteOptions { ExecutionPath = path, };
            CollectionAssert.AreEqual(expected, layout.Serialize("rec", value, null, write), path.ToString());
            byte[] span = new byte[6];
            Assert.AreEqual(4, layout.Serialize(span.AsSpan(), "rec", value, null, write), path.ToString());
            using var stream = new MemoryStream();
            layout.Write(stream, "rec", value, null, write);
            CollectionAssert.AreEqual(expected, stream.ToArray(), path.ToString());
            OperationOutcome tooSmall = OperationOutcome.Of(() => layout.Serialize(new byte[3].AsSpan(), "rec", value, null, write));
            Assert.IsInstanceOfType<CStructWriteException>(tooSmall.Failure, path.ToString());

            var update = new UpdateOptions { ExecutionPath = path, };
            OperationOutcome spanUpdate = OperationOutcome.Of(() =>
            {
                byte[] truncated = [1, 0, 2];
                layout.Update(truncated.AsSpan(), "rec.b", (byte)9, null, update);
                return truncated;
            });

            // An update needs only its target field's bytes, so the missing tail padding does not stop it.
            CollectionAssert.AreEqual(new byte[] { 1, 0, 9, }, (byte[])spanUpdate.Result!, path.ToString());
            foreach (EngineInput input in Streams)
            {
                OperationOutcome streamUpdate = OperationOutcome.Of(() =>
                {
                    using Stream target = EngineStreams.Open(input, [1, 0, 2]);
                    if (!target.CanWrite)
                    {
                        using var copy = new MemoryStream();
                        target.CopyTo(copy);
                        copy.Position = 0;
                        layout.Update(copy, "rec.b", (byte)9, null, update);
                        return copy.ToArray();
                    }

                    layout.Update(target, "rec.b", (byte)9, null, update);
                    target.Position = 0;
                    using var result = new MemoryStream();
                    target.CopyTo(result);
                    return result.ToArray();
                });
                OperationOutcome.AssertSame(spanUpdate, streamUpdate, "Update " + input + " (" + path + ")");
            }
        }
    }

    /// <summary>Parses from a stream form and checks where a complete read leaves the stream.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input.</param>
    /// <param name="input">The stream form.</param>
    /// <param name="read">The read options.</param>
    /// <param name="size">The struct size a complete read ends at, or -1 to skip the check.</param>
    /// <returns>The parsed value.</returns>
    private static StructValue ParseStream(CStruct layout, byte[] data, EngineInput input, ReadOptions read, int size)
    {
        using Stream stream = EngineStreams.Open(input, data);
        StructValue value = layout.Parse(stream, "rec", null, read);
        if (size >= 0)
        {
            Assert.AreEqual(size, stream.Position, input + ": final position");
        }

        return value;
    }

    /// <summary>Debug-parses from a stream form.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input.</param>
    /// <param name="input">The stream form.</param>
    /// <param name="read">The read options.</param>
    /// <returns>The parsed value.</returns>
    private static StructValue ParseDebugStream(CStruct layout, byte[] data, EngineInput input, ReadOptions read)
    {
        using Stream stream = EngineStreams.Open(input, data);
        return layout.ParseWithDebug(stream, "rec", null, read).Value;
    }

    /// <summary>Reads the root through <c>ReadValue</c> from a stream form.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input.</param>
    /// <param name="input">The stream form.</param>
    /// <param name="read">The read options.</param>
    /// <returns>The value.</returns>
    private static object? ReadValueStream(CStruct layout, byte[] data, EngineInput input, ReadOptions read)
    {
        using Stream stream = EngineStreams.Open(input, data);
        return layout.ReadValue(stream, "rec", null, read);
    }

    /// <summary>Parses asynchronously from a hidden-buffer memory stream.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input.</param>
    /// <param name="read">The read options.</param>
    /// <returns>The parsed value.</returns>
    private static StructValue ParseAsync(CStruct layout, byte[] data, ReadOptions read)
    {
        using var stream = new MemoryStream((byte[])data.Clone(), writable: false);
        return layout.ParseAsync(stream, "rec", null, read).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Materializes a record sequence so a failure part-way surfaces here.</summary>
    /// <param name="records">The records.</param>
    /// <returns>The records as a list.</returns>
    private static List<StructValue> Records(IEnumerable<StructValue> records) => records.ToList();
}
