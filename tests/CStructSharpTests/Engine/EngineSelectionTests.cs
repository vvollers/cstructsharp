namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Engine;
using CStructSharp.Values;

/// <summary>
///     Pins the engine selection option and its diagnostics: the selector decides once per operation that reaches the
///     general path, counts the decision in the recording open on the calling flow, runs the engine for whole-root reads
///     of eligible roots and declines every other operation, and fails an operation that requires the engine with the
///     decline reason before reading or writing anything.
/// </summary>
[TestClass]
public class EngineSelectionTests
{
    /// <summary>A layout with a data-sized array, so no direct fixed-root path can take its whole-root operations.</summary>
    private const string SizedLayout = "struct inner { uint8 a; }; struct rec { uint8 n; uint16 items[n]; inner last; uint8 tail; };";

    /// <summary>A layout the engine cannot read yet: its root holds a pointer (stage 5); compiled with one-byte pointers.</summary>
    private const string PointerLayout = "struct rec { uint8 n; uint8 *value; uint8 tail; };";

    /// <summary>Input for <see cref="SizedLayout"/>: two items, then <c>last.a</c> and <c>tail</c>.</summary>
    private static readonly byte[] SizedData = [2, 1, 0, 2, 0, 5, 9];

    /// <summary>Options default to automatic selection, and a copy made with <c>with</c> keeps the selection.</summary>
    [TestMethod]
    public void Options_DefaultToAutomatic_AndCopiesKeepTheSelection()
    {
        Assert.AreEqual(EngineSelection.Automatic, new ReadOptions().EngineSelection);
        Assert.AreEqual(EngineSelection.Automatic, new WriteOptions().EngineSelection);
        Assert.AreEqual(EngineSelection.Automatic, new UpdateOptions().EngineSelection);

        ReadOptions read = EngineSelections.EngineRequired() with { MaxArrayElements = 3 };
        Assert.AreEqual(EngineSelection.EngineRequired, read.EngineSelection);
        UpdateOptions update = EngineSelections.With(EngineSelection.InterpreterOnly, new UpdateOptions()) with { MaxTraversalBytesRead = 10 };
        Assert.AreEqual(EngineSelection.InterpreterOnly, update.EngineSelection);
        WriteOptions write = EngineSelections.InterpreterOnly(new WriteOptions()) with { MaxNestingDepth = 4 };
        Assert.AreEqual(EngineSelection.InterpreterOnly, write.EngineSelection);
    }

    /// <summary>
    ///     Each public operation that reaches the general path records exactly one decision of its own kind: a run for a
    ///     whole-root read of an eligible root, over every source, and a decline with its reason for everything else.
    /// </summary>
    [TestMethod]
    public void EveryOperation_RecordsOneDecisionOfItsKind()
    {
        var layout = new CStruct(SizedLayout);
        var value = new Dictionary<string, object?> { ["n"] = (byte)1, ["items"] = new ushort[] { 4, }, ["last"] = new Dictionary<string, object?> { ["a"] = (byte)5, }, ["tail"] = (byte)9, };
        var cases = new (string Name, EngineOperation Kind, Action<ReadOptions, WriteOptions, UpdateOptions> Run)[]
        {
            ("Parse(Span)", EngineOperation.RootRead, (read, _, _) => layout.Parse(SizedData.AsSpan(), "rec", options: read)),
            ("Parse(Stream)", EngineOperation.RootRead, (read, _, _) => layout.Parse(new MemoryStream(SizedData), "rec", options: read)),
            ("Parse(Sequence)", EngineOperation.RootRead, (read, _, _) => layout.Parse(ChunkedSequence.Of(SizedData), "rec", options: read)),
            ("Parse(chunked stream)", EngineOperation.RootRead, (read, _, _) => layout.Parse(EngineStreams.Open(EngineInput.ChunkedStream3, SizedData), "rec", options: read)),
            ("ParseMany(Stream)", EngineOperation.RootRead, (read, _, _) => layout.ParseMany(new MemoryStream(SizedData), "rec", options: read).ToList()),
            ("ParseAsync", EngineOperation.RootRead, (read, _, _) => layout.ParseAsync(new MemoryStream(SizedData), "rec", options: read).AsTask().GetAwaiter().GetResult()),
            ("Parse(path)", EngineOperation.PathRead, (read, _, _) => layout.Parse(SizedData, "rec.last", options: read)),
            ("ReadValue(root)", EngineOperation.RootRead, (read, _, _) => layout.ReadValue(SizedData, "rec", options: read)),
            ("ReadValue<T>(root)", EngineOperation.RootRead, (read, _, _) => layout.ReadValue<StructValue>(SizedData, "rec", options: read)),
            ("ReadValue(path)", EngineOperation.PathRead, (read, _, _) => layout.ReadValue(SizedData, "rec.items[1]", options: read)),
            ("ReadValue<T>(path)", EngineOperation.PathRead, (read, _, _) => layout.ReadValue<int>(SizedData, "rec.tail", options: read)),
            ("ParseWithDebug", EngineOperation.DebugRead, (read, _, _) => layout.ParseWithDebug(SizedData, "rec", options: read)),
            ("ReadValueWithDebug(path)", EngineOperation.DebugRead, (read, _, _) => layout.ReadValueWithDebug(SizedData, "rec.last", options: read)),
            ("ResolveAddress", EngineOperation.AddressResolution, (read, _, _) => layout.ResolveAddress(SizedData, "rec.tail", options: read)),
            ("ResolveAddressAsync", EngineOperation.AddressResolution, (read, _, _) => layout.ResolveAddressAsync(new MemoryStream(SizedData), "rec.tail", options: read).AsTask().GetAwaiter().GetResult()),
            ("GetArrayLength", EngineOperation.LengthQuery, (read, _, _) => layout.GetArrayLength(SizedData, "rec.items", options: read)),
            ("Serialize(byte[])", EngineOperation.Write, (_, write, _) => layout.Serialize("rec", value, options: write)),
            ("Serialize(Span)", EngineOperation.Write, (_, write, _) => layout.Serialize(new byte[16].AsSpan(), "rec", value, options: write)),
            ("Serialize(IBufferWriter)", EngineOperation.Write, (_, write, _) => layout.Serialize(new ArrayBufferWriter<byte>(), "rec", value, options: write)),
            ("Write", EngineOperation.Write, (_, write, _) => layout.Write(new MemoryStream(), "rec", value, options: write)),
            ("WriteAsync", EngineOperation.Write, (_, write, _) => layout.WriteAsync(new MemoryStream(), "rec", value, options: write).AsTask().GetAwaiter().GetResult()),
            ("Update(Span)", EngineOperation.Update, (_, _, update) => layout.Update((byte[])SizedData.Clone(), "rec.tail", (byte)1, options: update)),
            ("Update(Stream)", EngineOperation.Update, (_, _, update) => layout.Update(new MemoryStream((byte[])SizedData.Clone()), "rec.tail", (byte)1, options: update)),
            ("UpdateAsync", EngineOperation.Update, (_, _, update) => layout.UpdateAsync(new MemoryStream((byte[])SizedData.Clone()), "rec.tail", (byte)1, options: update).AsTask().GetAwaiter().GetResult()),
        };

        foreach ((string name, EngineOperation kind, Action<ReadOptions, WriteOptions, UpdateOptions> run) in cases)
        {
            EngineDiagnostics diagnostics;
            using (EngineRecording recording = EngineDiagnostics.Record())
            {
                diagnostics = recording.Diagnostics;
                run(EngineSelections.With(EngineSelection.Automatic), EngineSelections.With(EngineSelection.Automatic, new WriteOptions()), EngineSelections.With(EngineSelection.Automatic, new UpdateOptions()));
            }

            Assert.AreEqual(0, diagnostics.InterpreterSelections, name);
            Assert.AreEqual(kind, diagnostics.LastOperation, name);
            if (kind == EngineOperation.RootRead)
            {
                Assert.AreEqual(1, diagnostics.EngineRuns, name);
                Assert.AreEqual(0, diagnostics.Declines, name);
            }
            else
            {
                Assert.AreEqual(1, diagnostics.Declines, name);
                Assert.AreEqual(0, diagnostics.EngineRuns, name);
                Assert.AreEqual(new EngineDecline(kind, EngineSelector.OperationNotSupported), diagnostics.LastDecline, name);
            }

            EngineDiagnostics forced;
            using (EngineRecording recording = EngineDiagnostics.Record())
            {
                forced = recording.Diagnostics;
                run(EngineSelections.InterpreterOnly(), EngineSelections.InterpreterOnly(new WriteOptions()), EngineSelections.With(EngineSelection.InterpreterOnly, new UpdateOptions()));
            }

            Assert.AreEqual(1, forced.InterpreterSelections, name + " (interpreter only)");
            Assert.AreEqual(0, forced.Declines + forced.EngineRuns, name + " (interpreter only)");
            Assert.AreEqual(kind, forced.LastOperation, name + " (interpreter only)");
        }
    }

    /// <summary>
    ///     A record sequence decides once per record, so a recording sees one engine run for each record read, and one
    ///     decline, naming the first unsupported member, for each record of a root the engine cannot read.
    /// </summary>
    [TestMethod]
    public void RecordSequence_DecidesPerRecord()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 items[n]; };");
        using (EngineRecording recording = EngineDiagnostics.Record())
        {
            List<StructValue> records = layout.ParseMany(new byte[] { 1, 7, 0, 2, 8, 9, }.AsMemory(), "rec").ToList();
            Assert.HasCount(3, records);
            Assert.AreEqual(3, recording.Diagnostics.EngineRuns);
            Assert.AreEqual(0, recording.Diagnostics.Declines);
        }

        var pointers = new CStruct(PointerLayout, 1);
        using (EngineRecording recording = EngineDiagnostics.Record())
        {
            Assert.HasCount(2, pointers.ParseMany(new byte[] { 1, 0, 3, 4, 0, 7, }.AsMemory(), "rec").ToList());
            CollectionAssert.AreEqual(
                Enumerable.Repeat(new EngineDecline(EngineOperation.RootRead, "rec.value: pointers are not supported yet (stage 5)"), 2).ToArray(),
                recording.Diagnostics.RecentDeclines.ToArray());
        }
    }

    /// <summary>
    ///     A recording counts the decisions of its own flow and the tasks that flow starts, never those of a concurrent
    ///     flow, and nothing is counted after it is disposed. A nested recording counts into its own recorder until it
    ///     is disposed, and the outer one resumes.
    /// </summary>
    [TestMethod]
    public void Recordings_AreLocalToTheirFlow_AndNest()
    {
        var layout = new CStruct(SizedLayout);
        using var bothStarted = new Barrier(2);

        // Two concurrent flows each record their own reads; the barriers keep both recordings open at the same time.
        int CountOwnReads(int reads)
        {
            using EngineRecording recording = EngineDiagnostics.Record();
            bothStarted.SignalAndWait();
            for (int index = 0; index < reads; index++)
            {
                layout.Parse(SizedData, "rec");
            }

            bothStarted.SignalAndWait();
            return recording.Diagnostics.Decisions;
        }

        Task<int> first = Task.Run(() => CountOwnReads(3));
        Task<int> second = Task.Run(() => CountOwnReads(5));
        Assert.AreEqual(3, first.GetAwaiter().GetResult());
        Assert.AreEqual(5, second.GetAwaiter().GetResult());

        using EngineRecording outer = EngineDiagnostics.Record();
        layout.Parse(SizedData, "rec");
        Task.Run(() => layout.Parse(SizedData, "rec")).GetAwaiter().GetResult();
        using (EngineRecording inner = EngineDiagnostics.Record())
        {
            layout.ResolveAddress(SizedData, "rec.tail");
            Assert.AreEqual(1, inner.Diagnostics.Decisions);
            Assert.AreSame(inner.Diagnostics, EngineDiagnostics.Current);
        }

        layout.GetArrayLength(SizedData, "rec.items");
        Assert.AreEqual(3, outer.Diagnostics.Decisions, "the flow's read, the task's read, then the length query after the inner recording");
        Assert.AreEqual(2, outer.Diagnostics.EngineRuns, "the two whole-root reads");
        Assert.AreEqual(EngineOperation.LengthQuery, outer.Diagnostics.LastOperation);
        Assert.AreSame(outer.Diagnostics, EngineDiagnostics.Current);

        outer.Dispose();
        layout.Parse(SizedData, "rec");
        Assert.AreEqual(3, outer.Diagnostics.Decisions, "nothing is counted after disposal");
        Assert.IsNull(EngineDiagnostics.Current, "no recording is current on this flow after disposal");
    }

    /// <summary>
    ///     Requiring the engine fails every operation the engine declines with <see cref="InvalidOperationException"/>
    ///     naming the operation and the decline reason, before any byte is read or written or a stream moves; a whole-root
    ///     read of an eligible root runs.
    /// </summary>
    [TestMethod]
    public void EngineRequired_ThrowsWithTheDeclineReason_BeforeTouchingData()
    {
        var layout = new CStruct(SizedLayout);
        var value = new Dictionary<string, object?> { ["n"] = (byte)1, ["items"] = new ushort[] { 4, }, ["last"] = new Dictionary<string, object?> { ["a"] = (byte)5, }, ["tail"] = (byte)9, };
        ReadOptions read = EngineSelections.EngineRequired();
        WriteOptions write = EngineSelections.EngineRequired(new WriteOptions());
        UpdateOptions update = EngineSelections.EngineRequired(new UpdateOptions());

        Assert.AreEqual((byte)9, layout.Parse(SizedData.AsSpan(), "rec", options: read)["tail"]);
        Assert.AreEqual((byte)9, ((StructValue)layout.ReadValue(new MemoryStream(SizedData), "rec", options: read)!)["tail"]);

        var pointers = new CStruct(PointerLayout, 1);
        using var pointerSource = new MemoryStream([1, 2, 3, 4]);
        AssertRequired(EngineOperation.RootRead, () => pointers.Parse(pointerSource, "rec", options: read), "rec.value: pointers are not supported yet (stage 5)");
        Assert.AreEqual(0, pointerSource.Position, "the stream does not move");
        AssertRequired(EngineOperation.PathRead, () => layout.ReadValue(SizedData, "rec.items[0]", options: read));
        AssertRequired(EngineOperation.DebugRead, () => layout.ParseWithDebug(SizedData, "rec", options: read));
        AssertRequired(EngineOperation.AddressResolution, () => layout.ResolveAddress(SizedData, "rec.tail", options: read));
        AssertRequired(EngineOperation.LengthQuery, () => layout.GetArrayLength(SizedData, "rec.items", options: read));
        AssertRequired(EngineOperation.Write, () => layout.Serialize("rec", value, options: write));

        byte[] destination = [0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC];
        AssertRequired(EngineOperation.Write, () => layout.Serialize(destination.AsSpan(), "rec", value, options: write));
        CollectionAssert.AreEqual(new byte[] { 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, }, destination, "nothing is written");

        using var stream = new MemoryStream((byte[])SizedData.Clone());
        stream.Position = 1;
        AssertRequired(EngineOperation.Update, () => layout.Update(stream, "rec.tail", (byte)1, options: update));
        Assert.AreEqual(1, stream.Position, "the stream does not move");
        CollectionAssert.AreEqual(SizedData, stream.ToArray(), "nothing is changed");

        // The asynchronous forms copy the options with a linked token; the selection survives the copy.
        using var cancellation = new CancellationTokenSource();
        AssertRequired(EngineOperation.RootRead, () => pointers.ParseAsync(new MemoryStream([1, 2, 3, 4]), "rec", options: read, cancellationToken: cancellation.Token).AsTask().GetAwaiter().GetResult(), "rec.value: pointers are not supported yet (stage 5)");
        Assert.AreEqual((byte)9, layout.ParseAsync(new MemoryStream(SizedData), "rec", options: read, cancellationToken: cancellation.Token).AsTask().GetAwaiter().GetResult()["tail"]);
        AssertRequired(EngineOperation.Write, () => layout.WriteAsync(new MemoryStream(), "rec", value, options: write, cancellationToken: cancellation.Token).AsTask().GetAwaiter().GetResult());
        AssertRequired(EngineOperation.Update, () => layout.UpdateAsync(new MemoryStream((byte[])SizedData.Clone()), "rec.tail", (byte)1, options: update, cancellationToken: cancellation.Token).AsTask().GetAwaiter().GetResult());
    }

    /// <summary>
    ///     The selector runs only where the general path would: a whole fixed root read from memory or written to memory by
    ///     the direct path asks nothing, so a recording sees no decision; the same root read from a stream reaches the
    ///     general path, where the engine runs it.
    /// </summary>
    [TestMethod]
    public void DirectFixedRootPaths_DoNotConsultTheSelector()
    {
        var layout = new CStruct("struct rec { uint16 id; uint8 flags; };");
        ReadOptions read = EngineSelections.EngineRequired();
        WriteOptions write = EngineSelections.EngineRequired(new WriteOptions());
        byte[] data = [7, 0, 1];
        using EngineRecording recording = EngineDiagnostics.Record();

        StructValue value = layout.Parse(data.AsSpan(), "rec", options: read);
        Assert.AreEqual((ushort)7, value["id"]);
        Assert.AreEqual((ushort)7, layout.ReadValue<StructValue>(data, "rec", options: read)["id"]);
        CollectionAssert.AreEqual(data, layout.Serialize("rec", value, options: write));
        Assert.AreEqual(3, layout.Serialize(new byte[4].AsSpan(), "rec", value, options: write));
        Assert.AreEqual(0, recording.Diagnostics.Decisions);

        // The same root through a stream reaches the general path, which the engine runs.
        Assert.AreEqual((ushort)7, layout.Parse(new MemoryStream(data), "rec", options: read)["id"]);
        Assert.AreEqual(1, recording.Diagnostics.EngineRuns);

        // Forcing the interpreter or leaving the choice automatic reads the same value.
        Assert.AreEqual((ushort)7, layout.Parse(new MemoryStream(data), "rec", options: EngineSelections.InterpreterOnly())["id"]);
        Assert.AreEqual((ushort)7, layout.Parse(new MemoryStream(data), "rec", options: EngineSelections.With(EngineSelection.Automatic))["id"]);
        Assert.AreEqual(1, recording.Diagnostics.InterpreterSelections);
        Assert.AreEqual(2, recording.Diagnostics.EngineRuns);
        Assert.AreEqual(0, recording.Diagnostics.Declines);
    }

    /// <summary>The recorder counts concurrent decisions exactly and keeps only the most recent declines.</summary>
    [TestMethod]
    public void Diagnostics_CountConcurrentDecisions_AndKeepRecentDeclines()
    {
        var diagnostics = new EngineDiagnostics();
        Assert.IsNull(diagnostics.LastDecline);
        Assert.IsNull(diagnostics.LastOperation);

        Parallel.For(0, 1000, index =>
        {
            diagnostics.RecordDecline(EngineOperation.PathRead, "reason " + index);
            diagnostics.RecordRun(EngineOperation.RootRead);
            diagnostics.RecordInterpreterSelection(EngineOperation.Write);
        });

        Assert.AreEqual(1000, diagnostics.Declines);
        Assert.AreEqual(1000, diagnostics.EngineRuns);
        Assert.AreEqual(1000, diagnostics.InterpreterSelections);
        Assert.AreEqual(3000, diagnostics.Decisions);
        Assert.HasCount(EngineDiagnostics.KeptDeclines, diagnostics.RecentDeclines);

        diagnostics.RecordDecline(EngineOperation.Update, "last");
        Assert.AreEqual(new EngineDecline(EngineOperation.Update, "last"), diagnostics.LastDecline);
        Assert.AreEqual(new EngineDecline(EngineOperation.Update, "last"), diagnostics.RecentDeclines[^1]);
        Assert.AreEqual(EngineOperation.Update, diagnostics.LastOperation);
    }

    /// <summary>Asserts that an operation fails because the required engine declined it.</summary>
    /// <param name="kind">The kind of operation the message must name.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="reason">The decline reason the message must give.</param>
    private static void AssertRequired(EngineOperation kind, Action operation, string reason = EngineSelector.OperationNotSupported)
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(operation);
        Assert.AreEqual($"The compiled engine is required but declined the {kind} operation: {reason}.", failure.Message);
    }
}
