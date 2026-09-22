namespace CStructSharp.Tests.Generated;

using System.Buffers;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Exercises the generated reader's sequence boundary contracts independently of a particular generated layout.</summary>
[TestClass]
public class RecordSequenceBoundaryTests
{
    /// <summary>Invalid delegates and streams fail when the sequence is requested, not during later enumeration.</summary>
    [TestMethod]
    public void EntryPoints_RejectNullArgumentsImmediately()
    {
        using var stream = new MemoryStream();
        Assert.AreEqual("read", Assert.Throws<ArgumentNullException>(() => RecordSequence.FromMemory<byte>(default, 2, "pair", null, null!)).ParamName);
        Assert.AreEqual("read", Assert.Throws<ArgumentNullException>(() => RecordSequence.FromSequence<byte>(default, 2, "pair", null, null!)).ParamName);
        Assert.AreEqual("stream", Assert.Throws<ArgumentNullException>(() => RecordSequence.FromStream(null!, 2, "pair", null, ReadPair)).ParamName);
        Assert.AreEqual("read", Assert.Throws<ArgumentNullException>(() => RecordSequence.FromStream<byte>(stream, 2, "pair", null, null!)).ParamName);
        Assert.AreEqual("stream", Assert.Throws<ArgumentNullException>(() => RecordSequence.FromStreamAsync(null!, 2, "pair", null, ReadPair)).ParamName);
        Assert.AreEqual("read", Assert.Throws<ArgumentNullException>(() => RecordSequence.FromStreamAsync<byte>(stream, 2, "pair", null, null!)).ParamName);
        Assert.AreEqual("failure", Assert.Throws<ArgumentNullException>(() => RecordSequence.Complete(null!, 0, 0)).ParamName);
    }

    /// <summary>A one-segment sequence borrows its memory; edits made before the next lazy read remain visible.</summary>
    [TestMethod]
    public void SingleSegment_IsReadInPlace()
    {
        byte[] bytes = [1, 0, 2, 0,];
        using var records = RecordSequence.FromSequence(new ReadOnlySequence<byte>(bytes), 2, "pair", null, ReadPair).GetEnumerator();
        Assert.IsTrue(records.MoveNext());
        Assert.AreEqual((byte)1, records.Current.Value);
        bytes[2] = 7;
        Assert.IsTrue(records.MoveNext());
        Assert.AreEqual((byte)7, records.Current.Value);
        Assert.IsFalse(records.MoveNext());
    }

    /// <summary>A zero-byte second record reports its index, root name and original input offset.</summary>
    [TestMethod]
    public void EmptyRecord_IdentifiesItsInputLocation()
    {
        using var records = RecordSequence.FromMemory(new byte[] { 1, 0, 2, }, null, "pair", null, ReadThenStop).GetEnumerator();
        Assert.IsTrue(records.MoveNext());
        CStructReadException failure = Assert.Throws<CStructReadException>(() => records.MoveNext());
        Assert.AreEqual("[1].pair", failure.Path);
        Assert.AreEqual(2L, failure.Offset);
        StringAssert.Contains(failure.Message, "Record 1 of 'pair' occupies no bytes; a sequence of records needs a root that reads at least one byte");
    }

    /// <summary>Short stream reads are filled exactly to a record, and a trailing byte is diagnosed in stream coordinates.</summary>
    [TestMethod]
    public void FixedStream_FillsRecordsAndReportsTrailingBytes()
    {
        using var stream = new ShortReadStream([99, 1, 0, 2, 0, 7,]) { Position = 1, };
        using var records = RecordSequence.FromStream(stream, 2, "pair", null, ReadPair).GetEnumerator();
        Assert.IsTrue(records.MoveNext());
        Assert.AreEqual(((byte)1, 0, 1L), records.Current);
        Assert.AreEqual(3L, stream.Position);
        Assert.IsTrue(records.MoveNext());
        Assert.AreEqual(((byte)2, 1, 3L), records.Current);
        Assert.AreEqual(5L, stream.Position);
        CStructReadException failure = Assert.Throws<CStructReadException>(() => records.MoveNext());
        Assert.AreEqual("[2].pair", failure.Path);
        Assert.AreEqual(5L, failure.Offset);
        StringAssert.Contains(failure.Message, "remaining 1 bytes");
    }

    /// <summary>A forward-only source needs no position property, ends cleanly, and checks cancellation before another read.</summary>
    [TestMethod]
    public void FixedStream_HandlesEndAndCancellationWithoutSeeking()
    {
        using var stream = new ShortReadStream([1, 0, 2, 0,], seekable: false);
        CollectionAssert.AreEqual(
            new[] { ((byte)1, 0, 0L), ((byte)2, 1, 2L), },
            RecordSequence.FromStream(stream, 2, "pair", null, ReadPair).ToArray());

        using var cancelled = new CancellationTokenSource();
        using var another = new ShortReadStream([1, 0, 2, 0,], seekable: false);
        using var records = RecordSequence.FromStream(another, 2, "pair", new ReadOptions { CancellationToken = cancelled.Token, }, ReadPair).GetEnumerator();
        Assert.IsTrue(records.MoveNext());
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => records.MoveNext());
        Assert.AreEqual(2, another.BytesRead);
    }

    /// <summary>Async fixed records preserve a nonzero origin and propagate the linked token without dropping other options.</summary>
    [TestMethod]
    public async Task AsyncFixedStream_PreservesCoordinatesAndEffectiveOptions()
    {
        using var stream = new ShortReadStream([99, 1, 0, 2, 0,]) { Position = 1, };
        using var cancelled = new CancellationTokenSource();
        var options = new ReadOptions { MaxArrayElements = 17, };
        await using var records = RecordSequence.FromStreamAsync(stream, 2, "pair", options, ReadWithOptions, cancelled.Token).GetAsyncEnumerator();
        Assert.IsTrue(await records.MoveNextAsync());
        Assert.AreEqual(((byte)1, 0, 1L), records.Current);
        Assert.IsTrue(await records.MoveNextAsync());
        Assert.AreEqual(((byte)2, 1, 3L), records.Current);
        cancelled.Cancel();

        // This stream deliberately ignores cancellation so the iterator must check before it reads again.
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await records.MoveNextAsync());
        Assert.AreEqual(4, stream.BytesRead);
    }

    /// <summary>Records larger than the smallest pool bucket still receive enough storage on synchronous and async streams.</summary>
    [TestMethod]
    public async Task FixedStreams_ReadRecordsLargerThanTheMinimumPoolBucket()
    {
        byte[] bytes = Enumerable.Range(0, 64).Select(value => (byte)value).ToArray();
        using var sync = new ShortReadStream(bytes);
        CollectionAssert.AreEqual(new byte[] { 31, 63, }, RecordSequence.FromStream(sync, 32, "large", null, ReadLargeRecord).ToArray());
        Assert.AreEqual(0, sync.ZeroLengthReadRequests, "a completed record needs no additional zero-byte I/O request");
        using var asyncStream = new ShortReadStream(bytes);
        var results = new List<byte>();
        await foreach (byte value in RecordSequence.FromStreamAsync(asyncStream, 32, "large", null, ReadLargeRecord))
        {
            results.Add(value);
        }

        CollectionAssert.AreEqual(new byte[] { 31, 63, }, results);
    }

    /// <summary>Reads the final byte of a 32-byte record to exercise storage beyond the minimum pool bucket.</summary>
    /// <param name="source">Record storage supplied by the iterator.</param>
    /// <param name="offset">Record start within the storage.</param>
    /// <param name="index">Unused record index.</param>
    /// <param name="shift">Unused absolute origin.</param>
    /// <param name="options">Unused per-record settings.</param>
    /// <param name="consumed">The 32 consumed bytes.</param>
    /// <returns>The final record byte.</returns>
    private static byte ReadLargeRecord(ReadOnlyMemory<byte> source, int offset, int index, long shift, ReadOptions? options, out int consumed)
    {
        consumed = 32;
        return source.Span[offset + 31];
    }

    /// <summary>Reads a two-byte test record and exposes the index and absolute start passed by the iterator.</summary>
    /// <param name="source">Memory containing the record.</param>
    /// <param name="offset">Start within that memory, in bytes.</param>
    /// <param name="index">Zero-based record index.</param>
    /// <param name="shift">Origin of the memory in input coordinates.</param>
    /// <param name="options">Per-record settings; this minimal reader does not interpret them.</param>
    /// <param name="consumed">The two bytes consumed.</param>
    /// <returns>The first byte, record index and absolute byte offset.</returns>
    private static (byte Value, int Index, long Offset) ReadPair(ReadOnlyMemory<byte> source, int offset, int index, long shift, ReadOptions? options, out int consumed)
    {
        consumed = 2;
        return (source.Span[offset], index, shift + offset);
    }

    /// <summary>Reads one pair, then simulates a conditional record whose fields consume no bytes.</summary>
    /// <inheritdoc cref="ReadPair"/>
    private static (byte Value, int Index, long Offset) ReadThenStop(ReadOnlyMemory<byte> source, int offset, int index, long shift, ReadOptions? options, out int consumed)
    {
        var result = ReadPair(source, offset, index, shift, options, out consumed);
        if (index > 0)
        {
            consumed = 0;
        }

        return result;
    }

    /// <summary>Checks that asynchronous enumeration retains the caller's limit and supplies a cancellable token.</summary>
    /// <inheritdoc cref="ReadPair"/>
    private static (byte Value, int Index, long Offset) ReadWithOptions(ReadOnlyMemory<byte> source, int offset, int index, long shift, ReadOptions? options, out int consumed)
    {
        Assert.IsNotNull(options);
        Assert.AreEqual(17, options.MaxArrayElements);
        Assert.IsTrue(options.CancellationToken.CanBeCanceled);
        return ReadPair(source, offset, index, shift, options, out consumed);
    }

    /// <summary>A source that returns one byte per read and optionally forbids querying its position.</summary>
    private sealed class ShortReadStream : MemoryStream
    {
        private readonly bool seekable;

        /// <summary>Creates a short-read source over the supplied bytes.</summary>
        /// <param name="bytes">Input bytes owned by this test.</param>
        /// <param name="seekable">Whether position queries are permitted.</param>
        public ShortReadStream(byte[] bytes, bool seekable = true)
            : base(bytes)
        {
            this.seekable = seekable;
        }

        public int BytesRead { get; private set; }

        public int ZeroLengthReadRequests { get; private set; }

        public override bool CanSeek => this.seekable;

        public override long Position
        {
            get => this.seekable ? base.Position : throw new NotSupportedException();
            set => base.Position = value;
        }

        /// <summary>Returns at most one byte while tracking the total consumed from the source.</summary>
        /// <param name="buffer">Destination array.</param>
        /// <param name="offset">Destination start in bytes.</param>
        /// <param name="count">Maximum requested bytes.</param>
        /// <returns>The bytes copied, or zero at end of input.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0)
            {
                this.ZeroLengthReadRequests++;
            }

            ArgumentOutOfRangeException.ThrowIfGreaterThan(count, buffer.Length - offset);
            int read = base.Read(buffer, offset, Math.Min(count, 1));
            this.BytesRead += read;
            return read;
        }

        /// <summary>Returns at most one byte immediately, deliberately leaving cancellation enforcement to the iterator.</summary>
        /// <param name="buffer">Destination memory.</param>
        /// <param name="cancellationToken">Ignored to model a source that cannot cancel an individual read.</param>
        /// <returns>The bytes copied, or zero at end of input.</returns>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int value = buffer.IsEmpty ? -1 : this.ReadByte();
            if (value < 0)
            {
                return ValueTask.FromResult(0);
            }

            buffer.Span[0] = (byte)value;
            this.BytesRead++;
            return ValueTask.FromResult(1);
        }
    }
}
