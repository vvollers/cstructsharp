namespace CStructSharp.Tests;

using System.Runtime.CompilerServices;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Values;

/// <summary>
///     Regression tests for issue #52: the buffered input forms (a multi-segment sequence, the async stream forms, the
///     buffered stream reader generated code uses, and the windows of a record sequence) first copy the read budget plus
///     one byte, but padding, pointer targets and <c>T v[EOF]</c> counts move past bytes without charging them. The forms
///     read on and run again until the value is decided, so they return exactly what the span form returns.
/// </summary>
[TestClass]
public class BufferedBudgetEquivalenceTests
{
    /// <summary>An aligned terminated array between two bytes: the padding after <c>tag</c> is not charged.</summary>
    private const string AlignedArray = "struct rec { uint8 tag; uint16 values[]; uint8 tail; };";

    /// <summary>The bytes of <see cref="AlignedArray"/> (tag, padding, 1, 2, terminator, tail), then unused bytes.</summary>
    private static readonly byte[] AlignedArrayBytes = [7, 0xEE, 1, 0, 2, 0, 0, 0, 9, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC];

    /// <summary>
    ///     The issue's layout at its smallest span budget succeeds from every buffered form with the span's values, and
    ///     one byte less fails with the read-limit failure everywhere, not with a short read.
    /// </summary>
    /// <returns>A task that completes after every form is checked.</returns>
    [TestMethod]
    public async Task AlignedTerminatedArray_EveryFormMatchesTheSpanAtTheMinimumBudget()
    {
        var layout = new CStruct(AlignedArray, aligned: true);
        int minimum = SmallestBudget(budget => layout.Parse(AlignedArrayBytes, "rec", options: Budget(budget)));
        Assert.AreEqual(8, minimum, "tag, two elements, the terminator and tail; the padding is not charged");

        StructValue span = layout.Parse(AlignedArrayBytes, "rec", options: Budget(minimum));
        StructValue sequence = layout.Parse(ChunkedSequence.Of(AlignedArrayBytes), "rec", options: Budget(minimum));
        StructValue forward = await layout.ParseAsync(new AsyncStreamBufferTests.NonSeekableStream(AlignedArrayBytes), "rec", options: Budget(minimum));
        StructValue hidden = await layout.ParseAsync(new MemoryStream(AlignedArrayBytes), "rec", options: Budget(minimum));
        foreach (StructValue value in new[] { sequence, forward, hidden, })
        {
            Assert.AreEqual(span.Get<byte>("tail"), value.Get<byte>("tail"));
            CollectionAssert.AreEqual(span.Get<ushort[]>("values"), value.Get<ushort[]>("values"));
        }

        ReadOptions tooSmall = Budget(minimum - 1);
        CStructReadLimitException expected = Assert.Throws<CStructReadLimitException>(() => layout.Parse(AlignedArrayBytes, "rec", options: tooSmall));
        CStructReadLimitException fromSequence = Assert.Throws<CStructReadLimitException>(() => layout.Parse(ChunkedSequence.Of(AlignedArrayBytes), "rec", options: tooSmall));
        CStructReadLimitException fromStream = await Assert.ThrowsAsync<CStructReadLimitException>(async () => await layout.ParseAsync(new AsyncStreamBufferTests.NonSeekableStream(AlignedArrayBytes), "rec", options: tooSmall));
        Assert.AreEqual(expected.Message, fromSequence.Message, "the same failure, at the same offset");
        Assert.AreEqual(expected.Message, fromStream.Message);
    }

    /// <summary>
    ///     A read-to-end array is counted to the end of the whole input, never to the end of the first copy: before the
    ///     fix the copy of an aligned layout returned one element where the span returns three.
    /// </summary>
    /// <returns>A task that completes after every form is checked.</returns>
    [TestMethod]
    public async Task ReadToEndArray_IsNeverCountedToTheEndOfTheCopy()
    {
        var layout = new CStruct("struct tail { uint8 a; uint32 v[EOF]; };", aligned: true);
        byte[] bytes = [1, 0xEE, 0xEE, 0xEE, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0];
        ReadOptions options = Budget(13);
        uint[] expected = [1, 2, 3];

        CollectionAssert.AreEqual(expected, layout.Parse(bytes, "tail", options: options).Get<uint[]>("v"));
        CollectionAssert.AreEqual(expected, layout.Parse(ChunkedSequence.Of(bytes), "tail", options: options).Get<uint[]>("v"));
        CollectionAssert.AreEqual(expected, (await layout.ParseAsync(new AsyncStreamBufferTests.NonSeekableStream(bytes), "tail", options: options)).Get<uint[]>("v"));
        Assert.AreEqual(3, layout.GetArrayLength(ChunkedSequence.Of(bytes), "tail.v", options: options));
        Assert.AreEqual(3, await layout.GetArrayLengthAsync(new AsyncStreamBufferTests.NonSeekableStream(bytes), "tail.v", options: options));
    }

    /// <summary>
    ///     A seekable input's length is known, so counting a read-to-end array over a long input reads no more than the
    ///     first copy: the count, which charges nothing, needs the end's position, not the bytes before it.
    /// </summary>
    /// <returns>A task that completes after the count is checked.</returns>
    [TestMethod]
    public async Task ReadToEndCount_OverASeekableInput_ReadsOnlyTheFirstCopy()
    {
        var layout = new CStruct("struct tail { uint8 a; uint32 v[EOF]; };", aligned: true);
        byte[] bytes = new byte[4 + (4 * 1000)];
        ReadOptions options = Budget(8);
        using var stream = new CountingStream(bytes);

        Assert.AreEqual(layout.GetArrayLength(bytes, "tail.v", options: options), await layout.GetArrayLengthAsync(stream, "tail.v", options: options));
        Assert.AreEqual(9, stream.BytesRead, "the budget plus one byte");
        Assert.AreEqual(0L, stream.Position, "a length query leaves the stream at its origin");
    }

    /// <summary>A pointer whose target lies far past the first copy is followed as the span form follows it.</summary>
    /// <returns>A task that completes after every form is checked.</returns>
    [TestMethod]
    public async Task FarPointerTarget_IsReadFromEveryForm()
    {
        var layout = new CStruct("struct far { uint8 a; uint8* p; };", pointerSize: 8);
        byte[] bytes = [1, 40, 0, 0, 0, 0, 0, 0, 0, .. new byte[31], 0x2A, 0xCC, 0xCC, 0xCC];
        ReadOptions options = Budget(10);

        Assert.AreEqual((byte)0x2A, layout.ReadValue<byte>(bytes, "far.p.value", options: options));
        Assert.AreEqual((byte)0x2A, layout.ReadValue<byte>(ChunkedSequence.Of(bytes), "far.p.value", options: options));
        Assert.AreEqual((byte)0x2A, await layout.ReadValueAsync<byte>(new AsyncStreamBufferTests.NonSeekableStream(bytes), "far.p.value", options: options));
        Assert.AreEqual(40L, await layout.ResolveAddressAsync(new AsyncStreamBufferTests.NonSeekableStream(bytes), "far.p.value", options: options));
    }

    /// <summary>
    ///     A stream that cannot seek is consumed past the budget plus one byte only when the value addresses bytes past
    ///     it, and then only as far as needed: never to its end here.
    /// </summary>
    /// <returns>A task that completes after both reads are checked.</returns>
    [TestMethod]
    public async Task ForwardOnlyStream_IsConsumedOnlyAsFarAsTheValueAddresses()
    {
        var near = new CStruct("struct near { uint8 a; uint8 b; };");
        byte[] bytes = [1, 2, .. new byte[100]];
        var inside = new AsyncStreamBufferTests.NonSeekableStream(bytes);
        await near.ParseAsync(inside, "near", options: Budget(2));
        Assert.AreEqual(3, inside.BytesRead, "a value inside the first copy reads the budget plus one byte");

        var far = new CStruct("struct far { uint8 a; uint8* p; };", pointerSize: 8);
        byte[] pointed = [1, 40, 0, 0, 0, 0, 0, 0, 0, .. new byte[31], 0x2A, .. new byte[100]];
        var past = new AsyncStreamBufferTests.NonSeekableStream(pointed);
        await far.ParseAsync(past, "far", options: Budget(10));
        Assert.IsTrue(past.BytesRead > 11, "the target lies past the first copy");
        Assert.IsTrue(past.BytesRead < pointed.Length, "the stream is read only as far as the value addresses");
    }

    /// <summary>
    ///     A value inside the first copy runs the span reader once; a value that needs bytes past it runs it again over a
    ///     larger copy, and only then.
    /// </summary>
    [TestMethod]
    public void BufferedReader_RunsAgainOnlyWhenTheValueOutgrowsTheCopy()
    {
        var layout = new CStruct(AlignedArray, aligned: true);
        var runs = new StrongBox<int>();

        _ = ReadCursor.ReadStream<RootParser, StructValue>(new AsyncStreamBufferTests.NonSeekableStream(AlignedArrayBytes), new RootParser(layout, "rec", runs), Budget(64));
        Assert.AreEqual(1, runs.Value, "a large budget copies the whole input once");

        runs.Value = 0;
        _ = ReadCursor.ReadStream<RootParser, StructValue>(new AsyncStreamBufferTests.NonSeekableStream([1, 0, 2, 0, 0, 0,]), new RootParser(new CStruct("struct w { uint16 v[]; };"), "w", runs), Budget(6));
        Assert.AreEqual(1, runs.Value, "a value that charges every byte it reaches fits the first copy");

        runs.Value = 0;
        byte[] padded = [1, 0xEE, 0xEE, 0xEE, 2, 0, 0, 0, 3, 0xEE, 0xEE, 0xEE, 0xCC, 0xCC, 0xCC, 0xCC];
        _ = ReadCursor.ReadSequence<RootParser, StructValue>(ChunkedSequence.Of(padded), new RootParser(new CStruct("struct p { uint8 a; uint32 b; uint8 c; };", aligned: true), "p", runs), Budget(6));
        Assert.AreEqual(2, runs.Value, "b and c lie past the copy of the budget plus one byte, so the read runs once more");
    }

    /// <summary>
    ///     A record sequence of runtime-sized aligned records read through windows: a record that starts its window and
    ///     needs bytes past it makes the window grow, where it used to fail for real.
    /// </summary>
    /// <returns>A task that completes after the records are checked.</returns>
    [TestMethod]
    public async Task RecordWindows_GrowForARecordThatStartsThem()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint32 values[]; uint8 tail; };", aligned: true);
        byte[] record = [5, 0xEE, 0xEE, 0xEE, 1, 0, 0, 0, 0, 0, 0, 0, 9, 0xEE, 0xEE, 0xEE];
        byte[] bytes = [.. record, .. record, .. record];
        ReadOptions options = Budget(SmallestBudget(budget => layout.Parse(record, "rec", options: Budget(budget))));
        Assert.AreEqual(10L, options.MaxTotalBytesRead, "tag, one element, the terminator and tail");

        var tails = new List<byte>();
        await foreach (StructValue value in layout.ParseManyAsync(new MemoryStream(bytes), "rec", options: options))
        {
            tails.Add(value.Get<byte>("tail"));
        }

        CollectionAssert.AreEqual(new byte[] { 9, 9, 9 }, tails);
        Assert.AreEqual(3, layout.ParseMany(new MemoryStream(bytes), "rec", options: options).Count());
    }

    /// <summary>
    ///     A <c>T v[EOF]</c> count over a stream that cannot seek needs the stream's end, which only reading finds: the
    ///     buffer doubles while the stream gives bytes, so a short stream never costs an array of the largest size.
    /// </summary>
    [TestMethod]
    public void ReadToEndCount_OverAForwardOnlyStream_GrowsByDoubling()
    {
        var layout = new CStruct("struct tail { uint8 a; uint32 v[EOF]; };", aligned: true);
        byte[] bytes = [1, 0xEE, 0xEE, 0xEE, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0];
        ReadOptions options = Budget(13);
        var reader = new RootParser(layout, "tail", new StrongBox<int>());

        _ = ReadCursor.ReadStream<RootParser, StructValue>(new AsyncStreamBufferTests.NonSeekableStream(bytes), reader, options);
        long before = GC.GetAllocatedBytesForCurrentThread();
        StructValue value = ReadCursor.ReadStream<RootParser, StructValue>(new AsyncStreamBufferTests.NonSeekableStream(bytes), reader, options);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        CollectionAssert.AreEqual(new uint[] { 1, 2, 3 }, value.Get<uint[]>("v"));
        CollectionAssert.AreEqual(layout.Parse(bytes, "tail", options: options).Get<uint[]>("v"), value.Get<uint[]>("v"), "the span result");
        Assert.IsTrue(allocated < 1 << 20, "a 16-byte stream allocated " + allocated + " bytes");
    }

    /// <summary>
    ///     A pointer to the largest address, read from a record window that does not reach the stream's end, fails as
    ///     from memory: the address plus one byte overflows, and the window must grow, not take the overflow for success.
    /// </summary>
    /// <returns>A task that completes after the failure is checked.</returns>
    [TestMethod]
    public async Task RecordWindows_FailForAPointerToTheLargestAddress()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint8* p; uint8 values[]; };", pointerSize: 8);
        byte[] record = [1, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 0];
        byte[] bytes = [.. record, .. record, .. record];
        ReadOptions options = Budget(10);
        CStructReadException expected = Assert.Throws<CStructReadException>(() => layout.ParseMany(bytes.AsMemory(), "rec", options: options).ToList());

        int delivered = 0;
        CStructReadException actual = await Assert.ThrowsAsync<CStructReadException>(async () =>
        {
            // At most a few steps, so a window that mistook the overflow for success ends the test instead of looping.
            await foreach (StructValue value in layout.ParseManyAsync(new MemoryStream(bytes), "rec", options: options))
            {
                if (++delivered > 3)
                {
                    break;
                }
            }
        });
        Assert.AreEqual(0, delivered, "the first record fails");
        Assert.AreEqual(expected.Message, actual.Message);
    }

    /// <summary>
    ///     A seekable stream that ends before the length it reports: a window whose fill came up short holds the whole
    ///     input, so a record that needs more fails as from memory instead of growing the window again and again.
    /// </summary>
    /// <returns>A task that completes after the failure is checked.</returns>
    [TestMethod]
    public async Task RecordWindows_StopGrowingWhenTheStreamEndsEarly()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint32 values[]; uint8 tail; };", aligned: true);
        byte[] bytes = [5, 0xEE, 0xEE, 0xEE, 1, 0, 0, 0, 0, 0, 0, 0];
        ReadOptions options = Budget(10);
        CStructReadException expected = Assert.Throws<CStructReadException>(() => layout.ParseMany(bytes.AsMemory(), "rec", options: options).ToList());

        CStructReadException actual = await Assert.ThrowsAsync<CStructReadException>(async () =>
        {
            // Enumerates every record; the first one fails.
            await foreach (StructValue value in layout.ParseManyAsync(new OverstatedLengthStream(bytes, 40), "rec", options: options))
            {
                _ = value;
            }
        });
        Assert.AreEqual(expected.Message, actual.Message);
    }

    /// <summary>
    ///     A custom codec is promised the whole remaining input. A codec that takes every byte it is given must see the
    ///     whole input from a buffered form too, not only the bytes the first copy holds (the padding before it is not
    ///     charged, so the copy ends inside the value).
    /// </summary>
    /// <returns>A task that completes after every form is checked.</returns>
    [TestMethod]
    public async Task CustomCodecThatTakesTheRest_SeesTheWholeInput()
    {
        var layout = new CStruct("struct t { uint8 a; rest b; };", aligned: true, compilationOptions: new CStructCompilationOptions { Codecs = [RestCodec.Instance,], });
        byte[] bytes = [1, 0xEE, 0xEE, 0xEE, 0xEE, 0xEE, 0xEE, 0xEE, 10, 20, 30, 40];
        ReadOptions options = Budget(8);

        Assert.AreEqual(4, layout.ReadValue<int>(bytes, "t.b", options: options));
        Assert.AreEqual(4, layout.ReadValue<int>(ChunkedSequence.Of(bytes), "t.b", options: options));
        Assert.AreEqual(4, await layout.ReadValueAsync<int>(new AsyncStreamBufferTests.NonSeekableStream(bytes), "t.b", options: options));
    }

    /// <summary>
    ///     A record that starts its window knows the stream's remaining length, so a pointer past the stream's end fails
    ///     as from memory without the window growing over the rest of the stream.
    /// </summary>
    /// <returns>A task that completes after the failure and the bytes read are checked.</returns>
    [TestMethod]
    public async Task RecordWindows_FailForAPointerPastTheEndWithoutReadingTheRest()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint8* p; uint8 values[]; };", pointerSize: 8);
        byte[] bytes = new byte[4096];
        bytes[0] = 1;
        BitConverter.TryWriteBytes(bytes.AsSpan(1, 8), 1_000_000L);
        ReadOptions options = Budget(10);
        CStructReadException expected = Assert.Throws<CStructReadException>(() => layout.ParseMany(bytes.AsMemory(), "rec", options: options).ToList());

        using var stream = new CountingStream(bytes);
        CStructReadException actual = await Assert.ThrowsAsync<CStructReadException>(async () =>
        {
            // The first record fails, so the loop body never runs.
            await foreach (StructValue value in layout.ParseManyAsync(stream, "rec", options: options))
            {
            }
        });

        Assert.AreEqual(expected.Message, actual.Message);
        Assert.AreEqual(11, stream.BytesRead, "the first window of the budget plus one byte, not the whole stream");
    }

    /// <summary>Read options with a total read budget of <paramref name="budget"/> bytes.</summary>
    /// <param name="budget">The budget.</param>
    /// <returns>The options.</returns>
    private static ReadOptions Budget(long budget) => new() { MaxTotalBytesRead = budget, };

    /// <summary>The smallest budget a span read succeeds with; every smaller one must fail with the read-limit failure.</summary>
    /// <param name="read">The read at a budget.</param>
    /// <returns>The smallest successful budget.</returns>
    private static int SmallestBudget(Action<long> read)
    {
        for (int budget = 0; ; budget++)
        {
            try
            {
                read(budget);
                return budget;
            }
            catch (CStructReadLimitException)
            {
                // Too small: try the next budget.
            }
        }
    }

    /// <summary>A span reader that parses one root struct, counting its runs and reporting no end position.</summary>
    private readonly struct RootParser : IBufferedReader<StructValue>
    {
        private readonly CStruct layout;
        private readonly string root;
        private readonly StrongBox<int> runs;

        /// <summary>Creates the reader.</summary>
        /// <param name="layout">The layout that parses.</param>
        /// <param name="root">The root struct's name.</param>
        /// <param name="runs">Counts the runs.</param>
        public RootParser(CStruct layout, string root, StrongBox<int> runs)
        {
            this.layout = layout;
            this.root = root;
            this.runs = runs;
        }

        /// <inheritdoc/>
        public StructValue Read(ReadOnlySpan<byte> source, ReadOptions? options, out long consumed)
        {
            this.runs.Value++;
            consumed = 0;
            return this.layout.Parse(source, this.root, options: options);
        }
    }

    /// <summary>A custom type, aligned to eight bytes, whose value is every remaining byte; it decodes to their count.</summary>
    private sealed class RestCodec : ICustomCodec
    {
        /// <summary>The one instance layouts register.</summary>
        public static readonly RestCodec Instance = new();

        /// <summary>Gets the layout type name.</summary>
        public string Name => "rest";

        /// <summary>Gets nothing: the value's size is the rest of the input.</summary>
        public int? FixedSize => null;

        /// <summary>Gets the alignment, which puts padding before the value in an aligned layout.</summary>
        public int Alignment => 8;

        /// <summary>Takes every byte of <paramref name="source"/>.</summary>
        /// <param name="source">The remaining input.</param>
        /// <param name="value">Receives the byte count, as an <see cref="int"/>.</param>
        /// <param name="bytesConsumed">Receives the byte count.</param>
        /// <returns>Always <see cref="System.Buffers.OperationStatus.Done"/>.</returns>
        public System.Buffers.OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            value = source.Length;
            bytesConsumed = source.Length;
            return System.Buffers.OperationStatus.Done;
        }

        /// <summary>Writes nothing; the tests only read.</summary>
        /// <param name="destination">The destination.</param>
        /// <param name="value">The value.</param>
        /// <param name="bytesWritten">Receives zero.</param>
        /// <returns>Always <see cref="System.Buffers.OperationStatus.Done"/>.</returns>
        public System.Buffers.OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            bytesWritten = 0;
            return System.Buffers.OperationStatus.Done;
        }
    }

    /// <summary>A seekable stream that hides its buffer and reports a length larger than the bytes it holds, like a file truncated while it is read.</summary>
    /// <param name="bytes">The stream's bytes.</param>
    /// <param name="reportedLength">The length the stream reports.</param>
    private sealed class OverstatedLengthStream(byte[] bytes, long reportedLength) : MemoryStream(bytes, writable: false)
    {
        /// <summary>Gets the overstated length.</summary>
        public override long Length => reportedLength;
    }

    /// <summary>A seekable stream that hides its buffer, so the async forms buffer it, and counts the bytes it hands out.</summary>
    /// <param name="bytes">The stream's bytes.</param>
    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        /// <summary>Gets the number of bytes the stream has handed out.</summary>
        public int BytesRead { get; private set; }

        /// <summary>Reads and counts; a derived memory stream routes its span and async reads here.</summary>
        /// <param name="buffer">The destination.</param>
        /// <param name="offset">The first index to fill.</param>
        /// <param name="count">The most bytes to read.</param>
        /// <returns>The bytes read.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = base.Read(buffer, offset, count);
            this.BytesRead += read;
            return read;
        }

        /// <summary>Reads asynchronously through the counted synchronous read.</summary>
        /// <param name="buffer">The destination.</param>
        /// <param name="cancellationToken">The token.</param>
        /// <returns>The bytes read.</returns>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(this.Read(buffer.Span));
    }
}
