namespace CStructSharpTests;

using System.Buffers;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Pins <c>ParseMany</c> and <c>ParseManyAsync</c>: one root after another until the input ends, parsed lazily,
///     a fixed-size root by its stride and a runtime-sized root by the previous record's end; trailing bytes shorter
///     than one record fail on the step that meets them with the partial-element text and the record's index in the
///     path; the limits apply per record; the pointer rule of each form; the stream position after each record; the
///     awaitable form over a memory stream, a file, a forward-only stream, and a window smaller than the input.
/// </summary>
[TestClass]
public class RecordSequenceTests
{
    private const string Layout = "struct fixed { uint32 a; uint8 b; }; struct sized { uint8 n; uint8 d[n]; uint16 *p; uint16 blob; }; union choice { uint8 a; uint16 b; }; typedef uint32 word; struct empty { };";
    private static readonly byte[] Fixed = [1, 0, 0, 0, 9, 2, 0, 0, 0, 8, 3, 0, 0, 0, 7,];

    // Record 0: n = 1, d = [7], p -> 6 (the blob, 0xBBAA); record 1: n = 2, d = [7, 8], p -> 7 (0xDDCC).
    private static readonly byte[] Sized = [1, 7, 6, 0, 0, 0, 0xAA, 0xBB, 2, 7, 8, 7, 0, 0, 0, 0xCC, 0xDD,];

    /// <summary>Three fixed-size records and two runtime-sized ones read the same from memory, a segmented sequence, a stream, and the awaitable form.</summary>
    [TestMethod]
    public async Task Records_ReadTheSameFromEveryInput()
    {
        var layout = new CStruct(Layout, pointerSize: 4);
        foreach ((string kind, Func<byte[], IEnumerable<StructValue>> parseMany) in Inputs(layout, "fixed"))
        {
            List<StructValue> records = parseMany(Fixed).ToList();
            Assert.HasCount(3, records, kind);
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3, }, records.Select(record => record.Get<uint>("a")).ToArray(), kind);
            CollectionAssert.AreEqual(new byte[] { 9, 8, 7, }, records.Select(record => record.Get<byte>("b")).ToArray(), kind);
        }

        foreach ((string kind, Func<byte[], IEnumerable<StructValue>> parseMany) in Inputs(layout, "sized"))
        {
            List<StructValue> records = parseMany(Sized).ToList();
            Assert.HasCount(2, records, kind);
            CollectionAssert.AreEqual(new byte[] { 1, 2, }, records.Select(record => record.Get<byte>("n")).ToArray(), kind);
            CollectionAssert.AreEqual(new byte[] { 7, 8, }, records[1].Get<byte[]>("d"), kind);
            if (kind == "stream")
            {
                // The stream reader counts a stored address from the stream's first byte, as Parse(Stream) does.
                Assert.AreEqual((ushort)0xBBAA, records[0].Get<ushort>("p.value"), kind);
                Assert.AreEqual((ushort)0x02BB, records[1].Get<ushort>("p.value"), kind);
            }
            else
            {
                // Every other form treats a record as its own region: the address counts from the record's start.
                Assert.AreEqual((ushort)0xBBAA, records[0].Get<ushort>("p.value"), kind);
                Assert.AreEqual((ushort)0xDDCC, records[1].Get<ushort>("p.value"), kind);
            }
        }

        Assert.IsEmpty(layout.ParseMany(ReadOnlyMemory<byte>.Empty, "fixed"));
        Assert.IsEmpty(layout.ParseMany(new MemoryStream(), "sized"));
        Assert.IsEmpty(await Collect(layout.ParseManyAsync(new MemoryStream(), "fixed")));
        Assert.IsEmpty(await Collect(layout.ParseManyAsync(new MemoryStream(), "sized")));

        // The default root, and a record type named through a typedef.
        Assert.HasCount(3, layout.ParseMany(Fixed).ToList());
        var aliased = new CStruct("typedef struct _rec { uint8 v; } rec;");
        CollectionAssert.AreEqual(new byte[] { 4, 5, }, aliased.ParseMany(new byte[] { 4, 5, }, "rec").Select(record => record.Get<byte>("v")).ToArray());
    }

    /// <summary>Records are parsed on the step that reaches them, each as its own read: the limits apply per record and a failure names the record's index.</summary>
    [TestMethod]
    public void Records_AreParsedLazilyAndFailByIndex()
    {
        var layout = new CStruct(Layout, pointerSize: 4);
        byte[] bad = (byte[])Sized.Clone();
        bad[8] = 200; // record 1 claims 200 payload bytes
        using IEnumerator<StructValue> lazy = layout.ParseMany(bad, "sized").GetEnumerator();
        Assert.IsTrue(lazy.MoveNext());
        Assert.AreEqual((byte)1, lazy.Current.Get<byte>("n"));
        CStructReadException failure = Assert.Throws<CStructReadException>(() => lazy.MoveNext());
        StringAssert.Contains(failure.Message, "in '[1].sized'");
        Assert.AreEqual("[1].sized", failure.Path);
        Assert.AreEqual("d", failure.Member);
        Assert.AreEqual(Sized.Length, failure.Offset, "the input's coordinate (the record's start plus where its reader stopped), not the record's");

        // Twelve payload bytes per record would exceed a five-byte budget; the budget is charged per record, so the
        // first record passes and the second fails alone, while MaxArrayElements counts elements inside a record only.
        byte[] two = [1, 7, 6, 0, 0, 0, 0xAA, 0xBB, 12, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 17, 0, 0, 0, 0xCC, 0xDD,];
        var budget = new ReadOptions { MaxTotalBytesRead = 12, MaxArrayElements = 1, };
        using IEnumerator<StructValue> limited = layout.ParseMany(two, "sized", options: budget).GetEnumerator();
        Assert.IsTrue(limited.MoveNext());
        CStructReadLimitException overBudget = Assert.Throws<CStructReadLimitException>(() => limited.MoveNext());
        StringAssert.Contains(overBudget.Message, "[1].sized");
        Assert.HasCount(3, layout.ParseMany(Fixed, "fixed", options: new ReadOptions { MaxArrayElements = 1, }).ToList(), "three records are not an array of three");

        // A root that reads no bytes cannot advance.
        CStructReadException none = Assert.Throws<CStructReadException>(() => layout.ParseMany(new byte[] { 1, }, "empty").ToList());
        StringAssert.Contains(none.Message, "occupies no bytes");
    }

    /// <summary>Trailing bytes shorter than one record fail on the step that meets them: a fixed root with the partial-element text, a runtime-sized root with its short read.</summary>
    [TestMethod]
    public async Task TrailingBytes_FailOnTheStepThatMeetsThem()
    {
        var layout = new CStruct(Layout, pointerSize: 4);
        byte[] trailing = Fixed[..12];
        foreach ((string kind, Func<byte[], IEnumerable<StructValue>> parseMany) in Inputs(layout, "fixed"))
        {
            using IEnumerator<StructValue> records = parseMany(trailing).GetEnumerator();
            Assert.IsTrue(records.MoveNext(), kind);
            Assert.IsTrue(records.MoveNext(), kind);
            CStructReadException failure = Assert.Throws<CStructReadException>(() => records.MoveNext(), kind);
            Assert.AreEqual("The remaining 2 bytes are not a whole number of 5-byte elements: fixed (path '[2].fixed', offset 10).", failure.Message, kind);
        }

        CStructReadException viaAsync = await Assert.ThrowsAsync<CStructReadException>(async () => await Collect(layout.ParseManyAsync(new MemoryStream(trailing), "fixed")));
        Assert.AreEqual("The remaining 2 bytes are not a whole number of 5-byte elements: fixed (path '[2].fixed', offset 10).", viaAsync.Message);

        byte[] cut = Sized[..12];
        foreach ((string kind, Func<byte[], IEnumerable<StructValue>> parseMany) in Inputs(layout, "sized"))
        {
            using IEnumerator<StructValue> records = parseMany(cut).GetEnumerator();
            Assert.IsTrue(records.MoveNext(), kind);
            CStructReadException failure = Assert.Throws<CStructReadException>(() => records.MoveNext(), kind);
            StringAssert.Contains(failure.Message, "Not enough bytes", kind);
            StringAssert.Contains(failure.Message, "in '[1].sized'", kind);
        }

        CStructReadException sizedAsync = await Assert.ThrowsAsync<CStructReadException>(async () => await Collect(layout.ParseManyAsync(new MemoryStream(cut), "sized")));
        StringAssert.Contains(sizedAsync.Message, "in '[1].sized'");
    }

    /// <summary>The root must name a struct declaration, checked before the first record; the stream forms check the stream.</summary>
    [TestMethod]
    public void Roots_AreCheckedBeforeTheFirstRecord()
    {
        var layout = new CStruct(Layout, pointerSize: 4);
        CStructPathException union = Assert.Throws<CStructPathException>(() => layout.ParseMany(Fixed, "choice"));
        Assert.AreEqual(Assert.Throws<CStructPathException>(() => layout.Parse(Fixed, "choice")).Message, union.Message);
        CStructPathException scalar = Assert.Throws<CStructPathException>(() => layout.ParseMany(Fixed, "word"));
        Assert.AreEqual(Assert.Throws<CStructPathException>(() => layout.Parse(Fixed, "word")).Message, scalar.Message);
        StringAssert.Contains(Assert.Throws<CStructPathException>(() => layout.ParseMany(Fixed, "fixed.a")).Message, "selects a member");
        StringAssert.Contains(Assert.Throws<CStructPathException>(() => layout.ParseMany(new MemoryStream(Fixed), "nope")).Message, "Unknown struct declaration");
        Assert.Throws<CStructPathException>(() => layout.ParseManyAsync(new MemoryStream(Fixed), "choice"));

        Assert.Throws<ArgumentNullException>(() => layout.ParseMany((Stream)null!, "fixed"));
        Assert.Throws<ArgumentNullException>(() => layout.ParseManyAsync(null!, "fixed"));
        StringAssert.Contains(Assert.Throws<ArgumentException>(() => layout.ParseMany(new AsyncStreamBufferTests.NonSeekableStream(Fixed), "fixed")).Message, "seekable");
        StringAssert.Contains(Assert.Throws<ArgumentException>(() => layout.ParseManyAsync(new AsyncStreamBufferTests.NonSeekableStream(Sized), "sized")).Message, "seekable");
        Assert.Throws<ArgumentException>(() => layout.ParseMany(new WriteOnly(), "fixed"));
    }

    /// <summary>The synchronous stream form leaves the stream after each record, or where a failed read stopped; the awaitable form leaves a seekable stream at the record's end.</summary>
    [TestMethod]
    public async Task StreamForms_LeaveThePositionAtTheRecordsEnd()
    {
        var layout = new CStruct(Layout, pointerSize: 4);
        using var stream = new MemoryStream([0xEE, .. Sized,]) { Position = 1, };
        var ends = new List<long>();
        foreach (StructValue record in layout.ParseMany(stream, "sized"))
        {
            ends.Add(stream.Position);
        }

        CollectionAssert.AreEqual(new long[] { 9, 18, }, ends);

        stream.Position = 1;
        ends.Clear();
        await foreach (StructValue record in layout.ParseManyAsync(stream, "sized"))
        {
            ends.Add(stream.Position);
        }

        CollectionAssert.AreEqual(new long[] { 9, 18, }, ends);

        using var fixedStream = new MemoryStream(Fixed);
        ends.Clear();
        await foreach (StructValue record in layout.ParseManyAsync(fixedStream, "fixed"))
        {
            ends.Add(fixedStream.Position);
        }

        CollectionAssert.AreEqual(new long[] { 5, 10, 15, }, ends);
    }

    /// <summary>The awaitable form: a fixed root byte-exact from any stream (a file, a forward-only source), a runtime-sized root through a window that refills; cancellation between records.</summary>
    [TestMethod]
    public async Task ParseManyAsync_ReadsFilesForwardOnlyStreamsAndWindows()
    {
        var layout = new CStruct(Layout, pointerSize: 4);
        string path = Path.Combine(Path.GetTempPath(), $"cstructsharp-records-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, Fixed);
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            List<StructValue> records = await Collect(layout.ParseManyAsync(file, "fixed"));
            CollectionAssert.AreEqual(new uint[] { 1, 2, 3, }, records.Select(record => record.Get<uint>("a")).ToArray());
        }
        finally
        {
            File.Delete(path);
        }

        using var forward = new AsyncStreamBufferTests.NonSeekableStream(Fixed);
        List<StructValue> forwardRecords = await Collect(layout.ParseManyAsync(forward, "fixed"));
        Assert.HasCount(3, forwardRecords);
        Assert.AreEqual(Fixed.Length, forward.BytesRead, "byte-exact: nothing beyond the records is read");

        // A window of the budget plus one: the second record does not fit the first window and is read from a refill.
        byte[] three = [.. Sized, 3, 1, 2, 3, 8, 0, 0, 0, 0xEE, 0xFF,];
        List<StructValue> windowed = await Collect(layout.ParseManyAsync(new MemoryStream(three, 0, three.Length, writable: false, publiclyVisible: false), "sized", options: new ReadOptions { MaxTotalBytesRead = 12, }));
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, }, windowed.Select(record => record.Get<byte>("n")).ToArray());
        Assert.AreEqual((ushort)0xFFEE, windowed[2].Get<ushort>("p.value"));

        // A record larger than the budget fails with the budget text even though the window could have grown.
        CStructReadLimitException over = await Assert.ThrowsAsync<CStructReadLimitException>(async () => await Collect(layout.ParseManyAsync(new MemoryStream(three), "sized", options: new ReadOptions { MaxTotalBytesRead = 6, })));
        StringAssert.Contains(over.Message, "[0].sized");

        using var cancelled = new CancellationTokenSource();
        var seen = new List<byte>();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (StructValue record in layout.ParseManyAsync(new MemoryStream(Fixed), "fixed", cancellationToken: cancelled.Token))
            {
                seen.Add(record.Get<byte>("b"));
                cancelled.Cancel();
            }
        });
        CollectionAssert.AreEqual(new byte[] { 9, }, seen, "the record before the cancellation was delivered; the next step throws");
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await Collect(layout.ParseManyAsync(new MemoryStream(Sized), "sized", options: new ReadOptions { CancellationToken = new CancellationToken(canceled: true), })));
    }

    private static IEnumerable<(string Kind, Func<byte[], IEnumerable<StructValue>> ParseMany)> Inputs(CStruct layout, string root)
    {
        yield return ("memory", bytes => layout.ParseMany(bytes, root));
        yield return ("sequence", bytes => layout.ParseMany(new ReadOnlySequence<byte>(bytes), root));
        yield return ("segmented", bytes => layout.ParseMany(SequenceInputTests.Segmented(bytes, 3, 4), root));
        yield return ("stream", bytes => layout.ParseMany(new MemoryStream(bytes), root));
    }

    private static async Task<List<StructValue>> Collect(IAsyncEnumerable<StructValue> records)
    {
        var list = new List<StructValue>();
        await foreach (StructValue record in records)
        {
            list.Add(record);
        }

        return list;
    }

    private sealed class WriteOnly : MemoryStream
    {
        public override bool CanRead => false;
    }
}
