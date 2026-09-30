namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Values;

/// <summary>
///     Pins how public operations reach the compiled engine and how a test recording counts them
///     (<see cref="EngineDiagnostics"/>): every operation that is not taken by a direct fixed-root path runs on the engine
///     once, of its own kind - a failing one included - the recording is local to its flow, and an invalid path fails
///     before anything is read or written.
/// </summary>
[TestClass]
public class EngineDiagnosticsTests
{
    /// <summary>A layout with a data-sized array, so no direct fixed-root path can take its whole-root operations.</summary>
    private const string SizedLayout = "struct inner { uint8 a; }; struct rec { uint8 n; uint16 items[n]; inner last; uint8 tail; };";

    /// <summary>
    ///     A root spelled at run time whose count names a caller variable no expression of the layout uses, so the variable
    ///     has no slot (<see cref="UnslottedVariables"/>); the engine reads it through the caller's value all the same.
    /// </summary>
    private const string UnslottedRoot = "uint8[M]";

    /// <summary>The caller variables <see cref="UnslottedRoot"/> is read with.</summary>
    private static readonly Dictionary<string, int> UnslottedVariables = new() { ["M"] = 2, };

    /// <summary>Input for <see cref="SizedLayout"/>: two items, then <c>last.a</c> and <c>tail</c>.</summary>
    private static readonly byte[] SizedData = [2, 1, 0, 2, 0, 5, 9];

    /// <summary>
    ///     Each public operation that is not taken by a direct fixed-root path runs on the engine once, and the recording
    ///     counts it under its own kind: a whole-root read over every source, a read, debug parse, address or length of a
    ///     path (<c>Parse</c>, <c>ReadValue</c>, <c>ParseWithDebug</c>, <c>ReadValueWithDebug</c>, <c>ResolveAddress</c>,
    ///     <c>GetArrayLength</c>, and asynchronously), a write of a root or a nested path to every destination
    ///     (<c>Serialize</c> to an array, a span or a buffer writer, <c>Write</c> to a stream, and <c>WriteAsync</c>, which
    ///     serializes first), with plain or update options, and an update. An operation that fails - an unknown root, a path
    ///     that selects nothing - is counted as well.
    /// </summary>
    [TestMethod]
    public void EveryOperation_RunsOnceOnTheEngine_UnderItsKind()
    {
        var layout = new CStruct(SizedLayout);
        var value = new Dictionary<string, object?> { ["n"] = (byte)1, ["items"] = new ushort[] { 4, }, ["last"] = new Dictionary<string, object?> { ["a"] = (byte)5, }, ["tail"] = (byte)9, };
        var cases = new (string Name, EngineOperation Kind, Action Run)[]
        {
            ("Parse(Span)", EngineOperation.RootRead, () => layout.Parse(SizedData.AsSpan(), "rec")),
            ("Parse(Stream)", EngineOperation.RootRead, () => layout.Parse(new MemoryStream(SizedData), "rec")),
            ("Parse(Sequence)", EngineOperation.RootRead, () => layout.Parse(ChunkedSequence.Of(SizedData), "rec")),
            ("Parse(chunked stream)", EngineOperation.RootRead, () => layout.Parse(EngineStreams.Open(EngineInput.ChunkedStream3, SizedData), "rec")),
            ("ParseMany(Stream)", EngineOperation.RootRead, () => layout.ParseMany(new MemoryStream(SizedData), "rec").ToList()),
            ("ParseAsync", EngineOperation.RootRead, () => layout.ParseAsync(new MemoryStream(SizedData), "rec").AsTask().GetAwaiter().GetResult()),
            ("Parse(unknown root)", EngineOperation.RootRead, () => Assert.Throws<CStructPathException>(() => layout.Parse(SizedData, "nosuch"))),
            ("Parse(path)", EngineOperation.PathRead, () => layout.Parse(SizedData, "rec.last")),
            ("ReadValue(root)", EngineOperation.RootRead, () => layout.ReadValue(SizedData, "rec")),
            ("ReadValue<T>(root)", EngineOperation.RootRead, () => layout.ReadValue<StructValue>(SizedData, "rec")),
            ("ReadValue(path)", EngineOperation.PathRead, () => layout.ReadValue(SizedData, "rec.items[1]")),
            ("ReadValue<T>(path)", EngineOperation.PathRead, () => layout.ReadValue<int>(SizedData, "rec.tail")),
            ("ReadValue(unknown root)", EngineOperation.PathRead, () => Assert.Throws<CStructPathException>(() => layout.ReadValue(SizedData, "nosuch.x"))),
            ("ParseWithDebug", EngineOperation.DebugRead, () => layout.ParseWithDebug(SizedData, "rec")),
            ("ParseWithDebug(Stream)", EngineOperation.DebugRead, () => layout.ParseWithDebug(new MemoryStream(SizedData), "rec")),
            ("ParseWithDebugAsync", EngineOperation.DebugRead, () => layout.ParseWithDebugAsync(new MemoryStream(SizedData), "rec").AsTask().GetAwaiter().GetResult()),
            ("ReadValueWithDebug(root)", EngineOperation.DebugRead, () => layout.ReadValueWithDebug(SizedData, "rec")),
            ("ReadValueWithDebugAsync(root)", EngineOperation.DebugRead, () => layout.ReadValueWithDebugAsync(new MemoryStream(SizedData), "rec").AsTask().GetAwaiter().GetResult()),
            ("ParseWithDebug(path)", EngineOperation.DebugRead, () => layout.ParseWithDebug(SizedData, "rec.last")),
            ("ReadValueWithDebug(path)", EngineOperation.DebugRead, () => layout.ReadValueWithDebug(SizedData, "rec.last")),
            ("ResolveAddress", EngineOperation.AddressResolution, () => layout.ResolveAddress(SizedData, "rec.tail")),
            ("ResolveAddressAsync", EngineOperation.AddressResolution, () => layout.ResolveAddressAsync(new MemoryStream(SizedData), "rec.tail").AsTask().GetAwaiter().GetResult()),
            ("GetArrayLength", EngineOperation.LengthQuery, () => layout.GetArrayLength(SizedData, "rec.items")),
            ("Serialize(byte[])", EngineOperation.Write, () => layout.Serialize("rec", value)),
            ("Serialize(Span)", EngineOperation.Write, () => layout.Serialize(new byte[16].AsSpan(), "rec", value)),
            ("Serialize(IBufferWriter)", EngineOperation.Write, () => layout.Serialize(new ArrayBufferWriter<byte>(), "rec", value)),
            ("Write", EngineOperation.Write, () => layout.Write(new MemoryStream(), "rec", value)),
            ("Write(path)", EngineOperation.Write, () => layout.Write(new MemoryStream(), "rec.last", value["last"]!)),
            ("Serialize(path)", EngineOperation.Write, () => layout.Serialize("rec.last", value["last"]!)),
            ("Serialize(unknown member)", EngineOperation.Write, () => Assert.Throws<CStructPathException>(() => layout.Serialize("rec.nosuch", (byte)1))),
            ("Write(UpdateOptions)", EngineOperation.Write, () => layout.Write(new MemoryStream(), "rec", value, options: new UpdateOptions())),
            ("WriteAsync", EngineOperation.Write, () => layout.WriteAsync(new MemoryStream(), "rec", value).AsTask().GetAwaiter().GetResult()),
            ("Update(Span)", EngineOperation.Update, () => layout.Update((byte[])SizedData.Clone(), "rec.tail", (byte)1)),
            ("Update(Stream)", EngineOperation.Update, () => layout.Update(new MemoryStream((byte[])SizedData.Clone()), "rec.tail", (byte)1)),
            ("UpdateAsync", EngineOperation.Update, () => layout.UpdateAsync(new MemoryStream((byte[])SizedData.Clone()), "rec.tail", (byte)1).AsTask().GetAwaiter().GetResult()),
            ("Update(unknown member)", EngineOperation.Update, () => Assert.Throws<CStructPathException>(() => layout.Update((byte[])SizedData.Clone(), "rec.nosuch", (byte)1))),
        };

        foreach ((string name, EngineOperation kind, Action run) in cases)
        {
            using EngineRecording recording = EngineDiagnostics.Record();
            run();
            Assert.AreEqual(1, recording.Diagnostics.Runs, name);
            Assert.AreEqual(kind, recording.Diagnostics.LastOperation, name);
        }
    }

    /// <summary>A record sequence runs the engine once per record, so a recording counts one run for each record read.</summary>
    [TestMethod]
    public void RecordSequence_RunsTheEnginePerRecord()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 items[n]; };");
        using EngineRecording recording = EngineDiagnostics.Record();
        List<StructValue> records = layout.ParseMany(new byte[] { 1, 7, 0, 2, 8, 9, }.AsMemory(), "rec").ToList();
        Assert.HasCount(3, records);
        Assert.AreEqual(3, recording.Diagnostics.Runs);
    }

    /// <summary>
    ///     A recording counts the operations of its own flow and the tasks that flow starts, never those of a concurrent
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
            return recording.Diagnostics.Runs;
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
            Assert.AreEqual(1, inner.Diagnostics.Runs);
            Assert.AreSame(inner.Diagnostics, EngineDiagnostics.Current);
        }

        layout.GetArrayLength(SizedData, "rec.items");
        Assert.AreEqual(3, outer.Diagnostics.Runs, "the flow's read, the task's read, then the length query after the inner recording");
        Assert.AreEqual(EngineOperation.LengthQuery, outer.Diagnostics.LastOperation);
        Assert.AreSame(outer.Diagnostics, EngineDiagnostics.Current);

        outer.Dispose();
        layout.Parse(SizedData, "rec");
        Assert.AreEqual(3, outer.Diagnostics.Runs, "nothing is counted after disposal");
        Assert.IsNull(EngineDiagnostics.Current, "no recording is current on this flow after disposal");
    }

    /// <summary>
    ///     Every operation runs on the engine and gives its result; a path whose root the layout does not declare, or that
    ///     selects no member, fails with a path failure before any byte is read or written or a stream moves.
    /// </summary>
    [TestMethod]
    public void Operations_RunOnTheEngine_AndInvalidPathsFailBeforeTouchingData()
    {
        var layout = new CStruct(SizedLayout);
        var value = new Dictionary<string, object?> { ["n"] = (byte)1, ["items"] = new ushort[] { 4, }, ["last"] = new Dictionary<string, object?> { ["a"] = (byte)5, }, ["tail"] = (byte)9, };

        Assert.AreEqual((byte)9, layout.Parse(SizedData.AsSpan(), "rec")["tail"]);
        Assert.AreEqual((byte)9, ((StructValue)layout.ReadValue(new MemoryStream(SizedData), "rec")!)["tail"]);

        using var unslottedSource = new MemoryStream([1, 2, 3, 4]);
        object? spelled = layout.ReadValue(unslottedSource, UnslottedRoot, UnslottedVariables);
        Assert.AreEqual(2, ((System.Collections.IEnumerable)spelled!).Cast<object?>().Count(), "the caller's M counts the elements");
        Assert.AreEqual(2, unslottedSource.Position);

        using var unknownSource = new MemoryStream(SizedData);
        CStructPathException unknown = Assert.Throws<CStructPathException>(() => layout.ReadValue(unknownSource, "nosuch.x"));
        StringAssert.StartsWith(unknown.Message, "Unknown root 'nosuch'");
        Assert.AreEqual("nosuch.x", unknown.Path);
        Assert.AreEqual(0L, unknown.Offset);
        Assert.AreEqual(0, unknownSource.Position, "nothing is read");

        Assert.AreEqual((ushort)1, layout.ReadValue(SizedData, "rec.items[0]"));
        Assert.AreEqual((byte)9, layout.ParseWithDebug(SizedData, "rec").Value["tail"]);
        Assert.AreEqual((byte)5, layout.ParseWithDebug(SizedData, "rec.last").Value["a"]);
        Assert.AreEqual(6L, layout.ResolveAddress(SizedData, "rec.tail"));
        Assert.AreEqual(2, layout.GetArrayLength(SizedData, "rec.items"));
        CollectionAssert.AreEqual(new byte[] { 1, 4, 0, 5, 9, }, layout.Serialize("rec", value));
        var appended = new ArrayBufferWriter<byte>();
        Assert.AreEqual(5L, layout.Serialize(appended, "rec", value));
        CollectionAssert.AreEqual(new byte[] { 1, 4, 0, 5, 9, }, appended.WrittenSpan.ToArray());

        byte[] destination = [0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC];
        Assert.AreEqual(1, layout.Serialize(destination.AsSpan(), "rec.tail", (byte)1));
        CStructPathException member = Assert.Throws<CStructPathException>(() => layout.Serialize(destination.AsSpan(1), "rec.nosuch", (byte)1));
        StringAssert.StartsWith(member.Message, "Unknown field 'nosuch' in 'rec'");
        CollectionAssert.AreEqual(new byte[] { 1, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, }, destination, "nothing is written for the path that selects nothing");
        using var written = new MemoryStream();
        written.Write([7, 7]);
        written.Position = 1;
        layout.Write(written, "rec", value);
        Assert.AreEqual(6, written.Position, "the stream ends after the record");
        CollectionAssert.AreEqual(new byte[] { 7, 1, 4, 0, 5, 9, }, written.ToArray(), "the record overwrites and extends the stream");
        layout.Write(written, "rec.last", value["last"]!, options: new UpdateOptions());
        CollectionAssert.AreEqual(new byte[] { 7, 1, 4, 0, 5, 9, 5, }, written.ToArray(), "a member is written on its own, with update options too");

        using var stream = new MemoryStream((byte[])SizedData.Clone());
        stream.Position = 1;
        CStructPathException updated = Assert.Throws<CStructPathException>(() => layout.Update(stream, "rec.nosuch", (byte)1));
        StringAssert.StartsWith(updated.Message, "Unknown field 'nosuch' in 'rec'");
        Assert.AreEqual(1, stream.Position, "the stream does not move");
        CollectionAssert.AreEqual(SizedData, stream.ToArray(), "nothing is changed");
        stream.Position = 0;
        layout.Update(stream, "rec.tail", (byte)1);
        Assert.AreEqual(0, stream.Position, "the update restores the position");
        CollectionAssert.AreEqual(new byte[] { 2, 1, 0, 2, 0, 5, 1, }, stream.ToArray(), "only the tail changed");

        // The asynchronous forms copy the options with a linked token.
        using var cancellation = new CancellationTokenSource();
        Assert.AreEqual((byte)9, layout.ParseAsync(new MemoryStream(SizedData), "rec", cancellationToken: cancellation.Token).AsTask().GetAwaiter().GetResult()["tail"]);
        using var asyncWritten = new MemoryStream();
        layout.WriteAsync(asyncWritten, "rec", value, cancellationToken: cancellation.Token).AsTask().GetAwaiter().GetResult();
        CollectionAssert.AreEqual(new byte[] { 1, 4, 0, 5, 9, }, asyncWritten.ToArray(), "WriteAsync serializes through the engine");
        using var asyncUpdated = new MemoryStream((byte[])SizedData.Clone());
        layout.UpdateAsync(asyncUpdated, "rec.last.a", (byte)6, cancellationToken: cancellation.Token).AsTask().GetAwaiter().GetResult();
        CollectionAssert.AreEqual(new byte[] { 2, 1, 0, 2, 0, 6, 9, }, asyncUpdated.ToArray(), "UpdateAsync updates its buffer through the engine");
    }

    /// <summary>
    ///     A whole fixed root read from memory or written to memory by the direct path never reaches the engine, so a
    ///     recording counts nothing; the same root read from a stream reaches the engine.
    /// </summary>
    [TestMethod]
    public void DirectFixedRootPaths_DoNotReachTheEngine()
    {
        var layout = new CStruct("struct rec { uint16 id; uint8 flags; };");
        byte[] data = [7, 0, 1];
        using EngineRecording recording = EngineDiagnostics.Record();

        StructValue value = layout.Parse(data.AsSpan(), "rec");
        Assert.AreEqual((ushort)7, value["id"]);
        Assert.AreEqual((ushort)7, layout.ReadValue<StructValue>(data, "rec")["id"]);
        CollectionAssert.AreEqual(data, layout.Serialize("rec", value));
        Assert.AreEqual(3, layout.Serialize(new byte[4].AsSpan(), "rec", value));
        Assert.AreEqual(0, recording.Diagnostics.Runs);

        // The same root through a stream reaches the engine.
        Assert.AreEqual((ushort)7, layout.Parse(new MemoryStream(data), "rec")["id"]);
        Assert.AreEqual(1, recording.Diagnostics.Runs);
    }

    /// <summary>The recorder counts concurrent runs exactly and keeps the kind of the latest one.</summary>
    [TestMethod]
    public void Diagnostics_CountConcurrentRuns()
    {
        var diagnostics = new EngineDiagnostics();
        Assert.IsNull(diagnostics.LastOperation);

        Parallel.For(0, 1000, _ => diagnostics.RecordRun(EngineOperation.RootRead));

        Assert.AreEqual(1000, diagnostics.Runs);
        diagnostics.RecordRun(EngineOperation.Update);
        Assert.AreEqual(1001, diagnostics.Runs);
        Assert.AreEqual(EngineOperation.Update, diagnostics.LastOperation);
    }
}
