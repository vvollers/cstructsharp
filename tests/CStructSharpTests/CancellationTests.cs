namespace CStructSharpTests;

using System.Buffers;
using System.Globalization;
using CStructSharp;
using CStructSharp.Codecs;
using CStructSharp.Values;

/// <summary>
///     Pins the cancellation contract: the token on the option records is observed at composite, pointer, block,
///     element, and chunk boundaries, cancellation is an <see cref="OperationCanceledException"/> (never a read or
///     write failure, never swallowed by a <c>Try</c> form), and a cancelled operation leaves the caller's stream
///     position and destination as an expected failure would.
/// </summary>
[TestClass]
public class CancellationTests
{
    private const string Layout = """
                                  struct item { tag t; uint8 pad; };
                                  struct root { uint8 count; item items[count]; uint16 words[512]; cstring name; };
                                  """;

    /// <summary>A token cancelled before the call ends every operation before it touches the input or the destination.</summary>
    [TestMethod]
    public void CancelledBeforeTheCall_NothingIsReadOrWritten()
    {
        var layout = new CStruct("struct root { uint8 a; uint8 b; };");
        using var source = new CancellationTokenSource();
        source.Cancel();
        var read = new ReadOptions { CancellationToken = source.Token, };
        var write = new WriteOptions { CancellationToken = source.Token, };
        var update = new UpdateOptions { CancellationToken = source.Token, };
        byte[] bytes = [1, 2,];
        var value = new Dictionary<string, object?> { ["a"] = (byte)5, ["b"] = (byte)6, };

        using (var stream = new MemoryStream(bytes))
        {
            stream.Position = 0;
            Assert.Throws<OperationCanceledException>(() => layout.Parse(stream, "root", options: read));
            Assert.AreEqual(0L, stream.Position, "the position is untouched");
            Assert.Throws<OperationCanceledException>(() => layout.TryReadValue<StructValue>(stream, "root", out _, options: read), "a Try form does not swallow cancellation");
            Assert.AreEqual(0L, stream.Position);
        }

        Assert.Throws<OperationCanceledException>(() => layout.Parse(bytes, "root", options: read));
        Assert.Throws<OperationCanceledException>(() => layout.ReadValue<byte>(bytes, "root.b", options: read));
        Assert.Throws<OperationCanceledException>(() => layout.Serialize("root", value, options: write));
        using (var destination = new MemoryStream())
        {
            Assert.Throws<OperationCanceledException>(() => layout.Write(destination, "root", value, options: write));
            Assert.AreEqual(0L, destination.Length, "nothing is written");
        }

        using (var destination = new MemoryStream([1, 2,]))
        {
            Assert.Throws<OperationCanceledException>(() => layout.Update(destination, "root.b", (byte)9, options: update));
            CollectionAssert.AreEqual(new byte[] { 1, 2, }, destination.ToArray(), "an update commits nothing");
            Assert.AreEqual(0L, destination.Position);
        }

        // The default token is the none token: no check fires.
        Assert.AreEqual((byte)2, layout.Parse(bytes, "root").Get<byte>("b"));
    }

    /// <summary>A token cancelled during the read (by a custom codec, the one hook a test has inside an operation) stops the read at the next boundary; a Try form restores the stream position and rethrows.</summary>
    [TestMethod]
    public void CancelledMidRead_StopsAtTheNextBoundary_AndTryFormsRestoreThePosition()
    {
        byte[] bytes = Bytes();

        // Each attempt gets its own token source: the codec cancels it on its first call.
        (CStruct Layout, CancellingTag Codec, ReadOptions Options) Arrange()
        {
            var source = new CancellationTokenSource();
            var codec = new CancellingTag(source);
            var layout = new CStruct(Layout, compilationOptions: new CStructCompilationOptions { Codecs = [codec,], });
            return (layout, codec, new ReadOptions { CancellationToken = source.Token, });
        }

        (CStruct layout, CancellingTag codec, ReadOptions options) = Arrange();
        using (var stream = new MemoryStream(bytes))
        {
            Assert.Throws<OperationCanceledException>(() => layout.Parse(stream, "root", options: options));
            Assert.AreEqual(1, codec.Reads, "the first item's tag was decoded, then the next element boundary observed the token");
        }

        (layout, codec, options) = Arrange();
        using (var stream = new MemoryStream(bytes))
        {
            Assert.Throws<OperationCanceledException>(() => layout.TryReadValue<StructValue>(stream, "root", out _, options: options));
            Assert.AreEqual(1, codec.Reads);
            Assert.AreEqual(0L, stream.Position, "TryReadValue restores the origin before rethrowing");
        }

        (layout, codec, options) = Arrange();
        Assert.Throws<OperationCanceledException>(() => layout.Parse(bytes, "root", options: options));
        Assert.AreEqual(1, codec.Reads);

        // The same layout parses when nothing cancels.
        (layout, codec, _) = Arrange();
        StructValue parsed = layout.Parse(bytes, "root");
        Assert.AreEqual(3, codec.Reads);
        Assert.AreEqual(3, parsed.Get<StructValue[]>("items").Length);
        Assert.AreEqual("ok", parsed.Get<string>("name"));
    }

    /// <summary>The primitive-array block loop and the terminated-string chunk loop observe the token between blocks and chunks.</summary>
    [TestMethod]
    public void CancelledDuringABulkRead_StopsBetweenBlocksAndChunks()
    {
        var layout = new CStruct("struct root { uint32 values[65536]; };");
        byte[] bytes = new byte[65536 * 4];
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => layout.Parse(bytes, "root", options: new ReadOptions { CancellationToken = source.Token, }));

        var text = new CStruct("struct root { cstring name; };");
        byte[] longText = new byte[1024];
        longText.AsSpan(0, 1023).Fill((byte)'a');
        Assert.Throws<OperationCanceledException>(() => text.Parse(longText, "root", options: new ReadOptions { CancellationToken = source.Token, }));
        Assert.AreEqual(1023, text.Parse(longText, "root").Get<string>("name").Length);
    }

    /// <summary>A token cancelled during a write stops at the next composite boundary; the serialized array is never produced and a stream write may have written a prefix, as any late failure may.</summary>
    [TestMethod]
    public void CancelledMidWrite_StopsAtTheNextCompositeBoundary()
    {
        using var source = new CancellationTokenSource();
        var codec = new CancellingTag(source);
        var layout = new CStruct("struct item { tag t; uint8 pad; }; struct root { uint8 count; item items[count]; };", compilationOptions: new CStructCompilationOptions { Codecs = [codec,], });
        var value = new Dictionary<string, object?>
        {
            ["count"] = (byte)2,
            ["items"] = new object[]
            {
                new Dictionary<string, object?> { ["t"] = 7UL, ["pad"] = (byte)0, },
                new Dictionary<string, object?> { ["t"] = 8UL, ["pad"] = (byte)0, },
            },
        };
        Assert.Throws<OperationCanceledException>(() => layout.Serialize("root", value, options: new WriteOptions { CancellationToken = source.Token, }));
        Assert.AreEqual(1, codec.Writes, "the first item's tag was encoded, then the second element's boundary observed the token");

        using var updateSource = new CancellationTokenSource();
        var updateCodec = new CancellingTag(updateSource);
        var updateLayout = new CStruct("struct item { tag t; uint8 pad; }; struct root { uint8 count; item items[count]; };", compilationOptions: new CStructCompilationOptions { Codecs = [updateCodec,], });
        using var destination = new MemoryStream([2, 1, 0, 2, 0,]);
        Assert.Throws<OperationCanceledException>(() => updateLayout.Update(destination, "root", value, options: new UpdateOptions { CancellationToken = updateSource.Token, }));
        Assert.AreEqual(1, updateCodec.Writes);
        CollectionAssert.AreEqual(new byte[] { 2, 1, 0, 2, 0, }, destination.ToArray(), "an update stages first, so a cancelled update commits nothing");
        Assert.AreEqual(0L, destination.Position);
    }

    /// <summary>The token rides along in the read settings of an update's traversal and in the compiled options' equality.</summary>
    [TestMethod]
    public void Token_IsPartOfTheOptionsRecord()
    {
        using var source = new CancellationTokenSource();
        var options = new ReadOptions { CancellationToken = source.Token, };
        Assert.AreEqual(source.Token, options.CancellationToken);
        Assert.AreEqual(options, options with { });
        Assert.AreNotEqual(options, new ReadOptions(), "a different token is a different policy");
        Assert.AreEqual(CancellationToken.None, new WriteOptions().CancellationToken);
        Assert.AreEqual(CancellationToken.None, new UpdateOptions().CancellationToken);
    }

    private static byte[] Bytes()
    {
        var bytes = new List<byte> { 3, };
        for (int index = 0; index < 3; index++)
        {
            bytes.Add((byte)(index + 1));
            bytes.Add(0);
        }

        bytes.AddRange(new byte[1024]);
        bytes.AddRange("ok\0"u8.ToArray());
        return [.. bytes,];
    }

    /// <summary>A one-byte tag codec that cancels the shared token the first time it is called.</summary>
    private sealed class CancellingTag(CancellationTokenSource source) : ICustomCodec
    {
        public string Name => "tag";

        public int? FixedSize => 1;

        public int Alignment => 1;

        public int Reads { get; set; }

        public int Writes { get; set; }

        public OperationStatus Read(ReadOnlySpan<byte> source1, out object? value, out int bytesConsumed)
        {
            this.Reads++;
            source.Cancel();
            value = (ulong)source1[0];
            bytesConsumed = 1;
            return OperationStatus.Done;
        }

        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            this.Writes++;
            source.Cancel();
            destination[0] = (byte)Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            bytesWritten = 1;
            return OperationStatus.Done;
        }
    }
}
