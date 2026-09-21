namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Pins the awaitable write forms: <c>WriteAsync</c> produces the bytes <c>Write(Stream)</c> produces and writes
///     nothing on a validation failure; <c>UpdateAsync</c> changes the bytes <c>Update(Stream)</c> changes, writes back
///     only the ranges that differ, leaves the stream unchanged on a late failure, restores the origin, and refuses a
///     stream that cannot seek.
/// </summary>
[TestClass]
public class AsyncWriteTests
{
    private const string Layout = "struct item { uint16 id; uint8 flags; }; struct root { uint8 count; item items[2]; cstring name; uint16 *link; };";
    private static readonly byte[] Bytes = [2, 0x34, 0x12, 1, 0x78, 0x56, 2, (byte)'o', (byte)'k', 0, 18, 0, 0, 0, 0, 0, 0, 0, 0xEE, 0xFF,];

    /// <summary><c>WriteAsync</c> writes the bytes of <c>Write(Stream)</c> at the current position, and nothing when the value is invalid.</summary>
    [TestMethod]
    public async Task WriteAsync_WritesTheSyncBytes_AndNothingOnFailure()
    {
        var layout = new CStruct(Layout, pointerSize: 8);
        StructValue value = layout.Parse(Bytes, "root");

        using var sync = new MemoryStream();
        sync.Write([0xAA, 0xBB,]);
        layout.Write(sync, "root", value);
        using var async = new MemoryStream();
        async.Write([0xAA, 0xBB,]);
        await layout.WriteAsync(async, "root", value);
        CollectionAssert.AreEqual(sync.ToArray(), async.ToArray());
        Assert.AreEqual(sync.Position, async.Position);

        var invalid = new Dictionary<string, object?> { ["count"] = (byte)1, ["items"] = new object[] { new Dictionary<string, object?>(), }, ["name"] = "x", ["link"] = null, };
        using var untouched = new MemoryStream();
        CStructWriteException expected = Assert.Throws<CStructWriteException>(() => layout.Write(new MemoryStream(), "root", invalid));
        CStructWriteException actual = await Assert.ThrowsAsync<CStructWriteException>(async () => await layout.WriteAsync(untouched, "root", invalid));
        Assert.AreEqual(expected.Message, actual.Message);
        Assert.AreEqual(0L, untouched.Length, "a validation failure writes nothing");

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await layout.WriteAsync(null!, "root", value));
        await Assert.ThrowsAsync<ArgumentException>(async () => await layout.WriteAsync(new MemoryStream(new byte[4], writable: false), "root", value));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.WriteAsync(untouched, "root", value, cancellationToken: cancelled.Token));
        Assert.AreEqual(0L, untouched.Length);
    }

    /// <summary><c>UpdateAsync</c> changes exactly the bytes <c>Update(Stream)</c> changes, written back as the runs that differ, with the origin restored.</summary>
    [TestMethod]
    public async Task UpdateAsync_WritesBackOnlyTheChangedRuns()
    {
        var layout = new CStruct(Layout, pointerSize: 8);
        foreach ((string path, object replacement) in new (string, object)[]
                 {
                     ("root.items[1].id", (ushort)0xBEEF),
                     ("root.name", "no"),
                     ("root.items[0]", new Dictionary<string, object?> { ["id"] = (ushort)9, ["flags"] = (byte)9, }),
                 })
        {
            byte[] prefixed = [0xAA, 0xBB, .. Bytes,];
            using var sync = new MemoryStream((byte[])prefixed.Clone()) { Position = 2, };
            layout.Update(sync, path, replacement);
            using var async = new RecordingStream((byte[])prefixed.Clone()) { Position = 2, };
            await layout.UpdateAsync(async, path, replacement);
            CollectionAssert.AreEqual(sync.ToArray(), async.ToArray(), path);
            Assert.AreEqual(2L, async.Position, path);
            Assert.IsNotEmpty(async.Writes, path);
            foreach ((long position, int count) in async.Writes)
            {
                Assert.IsTrue(position >= 2 && position + count <= prefixed.Length, path);
                CollectionAssert.AreNotEqual(prefixed[(int)position..(int)(position + count)], async.ToArray()[(int)position..(int)(position + count)], $"{path}: every written run differs from the original");
            }
        }

        // A stored absolute address counts from the region's origin, as in the span form: the target is written at
        // origin + 18, where the synchronous stream form would write stream offset 18.
        byte[] pointerPrefixed = [0xAA, 0xBB, .. Bytes,];
        using var pointer = new RecordingStream((byte[])pointerPrefixed.Clone()) { Position = 2, };
        await layout.UpdateAsync(pointer, "root.link.value", (ushort)0x1234);
        byte[] spanForm = (byte[])Bytes.Clone();
        layout.Update(spanForm.AsSpan(), "root.link.value", (ushort)0x1234);
        CollectionAssert.AreEqual(spanForm, pointer.ToArray()[2..]);
        Assert.AreEqual((20L, 2), pointer.Writes.Single());

        // One changed field is one run at its stream coordinate: 2 (prefix) + 1 (count) + 3 (item 0) = offset 6, two bytes.
        using var single = new RecordingStream([0xAA, 0xBB, .. Bytes,]) { Position = 2, };
        await layout.UpdateAsync(single, "root.items[1].id", (ushort)0xBEEF);
        Assert.HasCount(1, single.Writes);
        Assert.AreEqual((6L, 2), single.Writes[0]);

        // An unchanged replacement writes nothing.
        using var same = new RecordingStream((byte[])Bytes.Clone());
        await layout.UpdateAsync(same, "root.items[1].id", (ushort)0x5678);
        Assert.IsEmpty(same.Writes);
    }

    /// <summary>A late failure, cancellation, or a stream that cannot seek leaves the destination unchanged.</summary>
    [TestMethod]
    public async Task UpdateAsync_LeavesTheStreamUnchangedOnFailure()
    {
        var layout = new CStruct(Layout, pointerSize: 8);
        using var stream = new RecordingStream((byte[])Bytes.Clone());
        CStructWriteException expected = Assert.Throws<CStructWriteException>(() => layout.Update(new MemoryStream((byte[])Bytes.Clone()), "root.name", "toolong"));
        CStructWriteException actual = await Assert.ThrowsAsync<CStructWriteException>(async () => await layout.UpdateAsync(stream, "root.name", "toolong"));
        Assert.AreEqual(expected.Message, actual.Message);
        CollectionAssert.AreEqual(Bytes, stream.ToArray());
        Assert.IsEmpty(stream.Writes);
        Assert.AreEqual(0L, stream.Position);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.UpdateAsync(stream, "root.items[1].id", (ushort)1, cancellationToken: cancelled.Token));
        CollectionAssert.AreEqual(Bytes, stream.ToArray());
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.UpdateAsync(stream, "root.items[1].id", (ushort)1, options: new UpdateOptions { CancellationToken = cancelled.Token, }));

        ArgumentException forward = await Assert.ThrowsAsync<ArgumentException>(async () => await layout.UpdateAsync(new AsyncStreamBufferTests.NonSeekableStream(Bytes), "root.items[1].id", (ushort)1));
        StringAssert.Contains(forward.Message, "seekable");
        await Assert.ThrowsAsync<ArgumentException>(async () => await layout.UpdateAsync(new MemoryStream(Bytes, writable: false), "root.items[1].id", (ushort)1));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await layout.UpdateAsync(null!, "root.items[1].id", (ushort)1));
    }

    /// <summary>A memory stream that records the position and length of every write.</summary>
    private sealed class RecordingStream(byte[] bytes) : MemoryStream(bytes, writable: true)
    {
        public List<(long Position, int Count)> Writes { get; } = [];

        // Only the asynchronous write is recorded: the memory stream forwards it to a synchronous overload internally.
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.Writes.Add((this.Position, buffer.Length));
            return base.WriteAsync(buffer, cancellationToken);
        }
    }
}
