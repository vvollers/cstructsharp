namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Pins the awaitable read forms against the synchronous stream forms: the same values, debug ranges (as stream
///     coordinates), addresses, lengths, failure texts, and final positions over a memory stream that exposes its
///     buffer (read in place), one that does not (buffered), a file opened for asynchronous I/O, and a forward-only
///     stream (consumed up to the budget); the non-throwing form's outcome; cancellation.
/// </summary>
[TestClass]
public class AsyncReadTests
{
    private const string Layout = "struct item { uint16 id; uint8 flags; }; struct root { uint8 count; item items[count]; cstring name; uint16 *link; };";
    private const int Origin = 2;
    private const int ValueLength = 18;
    private static readonly byte[] Bytes = [0xAA, 0xBB, 2, 0x34, 0x12, 1, 0x78, 0x56, 2, (byte)'o', (byte)'k', 0, 14, 0, 0, 0, 0, 0, 0, 0, 0xEE, 0xFF, 0xCC,];

    /// <summary>Every awaitable read agrees with its synchronous stream form on every stream kind, and leaves a seekable stream just after the value (or at the origin for address and length queries).</summary>
    [TestMethod]
    public async Task AsyncReads_AgreeWithTheStreamForms()
    {
        var layout = new CStruct(Layout, pointerSize: 8);
        foreach ((string kind, Func<Stream> open) in Streams())
        {
            // Parse.
            using (Stream sync = open())
            using (Stream async = open())
            {
                StructValue expected = layout.Parse(sync, "root");
                StructValue actual = await layout.ParseAsync(async, "root");
                Assert.AreEqual(expected.Get<string>("name"), actual.Get<string>("name"), kind);
                Assert.AreEqual(expected.Get<ushort>("items[1].id"), actual.Get<ushort>("items[1].id"), kind);
                Assert.AreEqual(expected.Get<ushort>("link.value"), actual.Get<ushort>("link.value"), kind);
                if (async.CanSeek)
                {
                    Assert.AreEqual(sync.Position, async.Position, kind);
                    Assert.AreEqual((long)(Origin + ValueLength), async.Position, kind);
                }
            }

            // ParseWithDebug: ranges are stream coordinates for a seekable stream.
            using (Stream sync = open())
            using (Stream async = open())
            {
                ParseResult expected = layout.ParseWithDebug(sync, "root");
                ParseResult actual = await layout.ParseWithDebugAsync(async, "root");
                Assert.AreEqual(expected.Debug.Count, actual.Debug.Count, kind);
                for (int index = 0; index < expected.Debug.Count; index++)
                {
                    long shift = async.CanSeek ? 0 : Origin;
                    Assert.AreEqual(expected.Debug[index].Start - shift, actual.Debug[index].Start, $"{kind}: {expected.Debug[index].Path}");
                    Assert.AreEqual(expected.Debug[index].End - shift, actual.Debug[index].End, kind);
                }
            }

            // ReadValue, ReadValue<T>, ReadValueWithDebug, ResolveAddress, GetArrayLength.
            using (Stream sync = open())
            using (Stream async = open())
            {
                Assert.AreEqual(layout.ReadValue(sync, "root.name"), await layout.ReadValueAsync(async, "root.name"), kind);
                if (async.CanSeek)
                {
                    Assert.AreEqual(sync.Position, async.Position, kind);
                    sync.Position = Origin;
                    async.Position = Origin;
                    Assert.AreEqual(layout.ReadValue<ushort>(sync, "root.items[1].id"), await layout.ReadValueAsync<ushort>(async, "root.items[1].id"), kind);
                    sync.Position = Origin;
                    async.Position = Origin;
                    ReadResult expected = layout.ReadValueWithDebug(sync, "root.items[1]");
                    ReadResult actual = await layout.ReadValueWithDebugAsync(async, "root.items[1]");
                    Assert.AreEqual(((StructValue)expected.Value!).Get<byte>("flags"), ((StructValue)actual.Value!).Get<byte>("flags"), kind);
                    Assert.AreEqual(expected.Debug[0].Start, actual.Debug[0].Start, kind);
                    sync.Position = Origin;
                    async.Position = Origin;
                    Assert.AreEqual(layout.ResolveAddress(sync, "root.items[1].flags"), await layout.ResolveAddressAsync(async, "root.items[1].flags"), kind);
                    Assert.AreEqual((long)Origin, async.Position, $"{kind}: an address query ends at the origin");
                    Assert.AreEqual(layout.GetArrayLength(sync, "root.items"), await layout.GetArrayLengthAsync(async, "root.items"), kind);
                    Assert.AreEqual((long)Origin, async.Position, kind);
                }
            }
        }

        // A non-seekable stream: buffer offsets and consumption up to the budget.
        using var forward = new AsyncStreamBufferTests.NonSeekableStream(Bytes[Origin..]);
        long address = await layout.ResolveAddressAsync(forward, "root.items[1].flags", options: new ReadOptions { MaxTotalBytesRead = 64, });
        Assert.AreEqual(layout.ResolveAddress(Bytes.AsSpan(Origin), "root.items[1].flags"), address);
        Assert.AreEqual(Bytes.Length - Origin, forward.BytesRead, "consumed: everything up to the budget plus one");
    }

    /// <summary>A failure carries the stream form's text and leaves a seekable stream at its origin; the non-throwing form reports the same failure without throwing; cancellation throws and restores the origin.</summary>
    [TestMethod]
    public async Task AsyncFailures_MatchTheStreamFormsAndRestoreTheOrigin()
    {
        var layout = new CStruct(Layout, pointerSize: 8);
        byte[] truncated = Bytes[..9];
        foreach ((string kind, Func<Stream> open) in Streams(truncated))
        {
            using Stream sync = open();
            using Stream async = open();
            CStructReadException expected = Assert.Throws<CStructReadException>(() => layout.Parse(sync, "root"));
            CStructReadException actual = await Assert.ThrowsAsync<CStructReadException>(async () => await layout.ParseAsync(async, "root"));
            Assert.AreEqual(expected.Message, actual.Message, kind);
            if (async.CanSeek)
            {
                Assert.AreEqual((long)Origin, async.Position, $"{kind}: a failed async read restores the origin");
            }

            async.Position = Origin;
            ReadAttempt<StructValue> attempt = await layout.TryReadValueAsync<StructValue>(async, "root");
            Assert.IsFalse(attempt.Succeeded, kind);
            Assert.IsNull(attempt.Value, kind);
            Assert.AreEqual(expected.Message, attempt.Failure!.Message, kind);
            Assert.AreEqual((long)Origin, async.Position, kind);
        }

        using var stream = new MemoryStream(Bytes) { Position = Origin, };
        ReadAttempt<ushort> ok = await layout.TryReadValueAsync<ushort>(stream, "root.items[0].id");
        Assert.IsTrue(ok.Succeeded);
        Assert.AreEqual((ushort)0x1234, ok.Value);
        Assert.IsNull(ok.Failure);

        CStructPathException path = await Assert.ThrowsAsync<CStructPathException>(async () => await layout.ParseAsync(new MemoryStream(Bytes), "missing"));
        Assert.AreEqual(Assert.Throws<CStructPathException>(() => layout.Parse(new MemoryStream(Bytes), "missing")).Message, path.Message);
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await layout.ParseAsync(null!, "root"));
        await Assert.ThrowsAsync<ArgumentException>(async () => await layout.ParseAsync(new WriteOnlyStream(), "root"));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        stream.Position = Origin;
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.ParseAsync(stream, "root", cancellationToken: cancelled.Token));
        Assert.AreEqual((long)Origin, stream.Position, "cancellation before the read leaves the origin");
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.ParseAsync(stream, "root", options: new ReadOptions { CancellationToken = cancelled.Token, }));
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.TryReadValueAsync<StructValue>(stream, "root", cancellationToken: cancelled.Token));
    }

    private static IEnumerable<(string Kind, Func<Stream> Open)> Streams(byte[]? bytes = null)
    {
        bytes ??= Bytes;
        yield return ("memory (buffer exposed)", () => new MemoryStream(bytes) { Position = Origin, });
        yield return ("memory (buffer hidden)", () => new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false) { Position = Origin, });
        yield return ("file (async)", () => OpenFile(bytes));
    }

    private static FileStream OpenFile(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"cstructsharp-async-read-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, bytes);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.DeleteOnClose) { Position = Origin, };
    }

    private sealed class WriteOnlyStream : MemoryStream
    {
        public override bool CanRead => false;
    }
}
