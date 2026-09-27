namespace CStructSharp.Tests;

using CStructSharp.Codecs;
using CStructSharp.Reading;
using CStructSharp.Streams;

/// <summary>Checks bulk primitive reads at block, cancellation and pooled-buffer ownership boundaries.</summary>
[TestClass]
[DoNotParallelize]
public class PrimitiveArrayBoundaryTests
{
    /// <summary>Both typed and boxed readers return their rental after success or cancellation between blocks.</summary>
    /// <param name="boxed">Whether to append boxed elements for a multidimensional array.</param>
    /// <param name="cancel">Whether the underlying first read requests cancellation before the next block.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public void BlockReads_ReturnRentalAndHonorCancellation(bool boxed, bool cancel)
    {
        using var listener = new PoolReturnListener();
        using var cancellation = new CancellationTokenSource();
        byte[] bytes = Enumerable.Repeat((byte)37, 65537).ToArray();
        using var source = new ObservedStream(bytes, listener, cancel ? cancellation : null);
        using var budget = new ReadBudgetStream(source, long.MaxValue, long.MaxValue, cancellation.Token);
        PrimitiveCodec codec = PrimitiveCodec.Resolve("uint8", true);
        if (cancel)
        {
            // The first block is valid; cancellation must prevent consuming the final byte in the next block.
            OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() => ReadArray(budget, codec, bytes.Length, boxed));
            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
            Assert.AreEqual(65536L, source.Position);
        }
        else
        {
            IList<object?> result = ReadArray(budget, codec, bytes.Length, boxed);
            Assert.AreEqual(bytes.Length, result.Count);
            Assert.AreEqual((byte)37, result[0]);
            Assert.AreEqual((byte)37, result[^1]);
            Assert.AreEqual((long)bytes.Length, source.Position);
        }

        Assert.IsTrue(source.ObservedRental, "The non-exposable stream must use the pooled block path.");
        Assert.IsTrue(listener.Returned, "The first rental must be returned, including after cancellation.");
    }

    /// <summary>Non-numeric codecs fail with a useful explanation in each bulk-reader entry point.</summary>
    [TestMethod]
    public void UnsupportedCodec_IdentifiesTheRequiredCategory()
    {
        PrimitiveCodec codec = PrimitiveCodec.Resolve("char", true);
        using var source = new MemoryStream(new byte[1]);
        using var budget = new ReadBudgetStream(source, long.MaxValue, long.MaxValue);

        // Character arrays use text handling instead of numeric bulk decoding.
        StringAssert.StartsWith(Assert.Throws<InvalidOperationException>(() => PrimitiveArrayReader.Read(budget, codec, 1)).Message, "Codec is not a fixed-width numeric primitive: Char");
        StringAssert.StartsWith(Assert.Throws<InvalidOperationException>(() => PrimitiveArrayReader.Decode(new byte[1], codec, 1)).Message, "Codec is not a fixed-width numeric primitive: Char");
        StringAssert.StartsWith(Assert.Throws<InvalidOperationException>(() => PrimitiveArrayReader.GetElementType(codec)).Message, "Codec is not a fixed-width numeric: Char");
    }

    /// <summary>Runs either bulk entry point and checks the boxed reader's last-value result.</summary>
    /// <param name="stream">The budgeted input at the first array byte.</param>
    /// <param name="codec">The fixed-width numeric codec.</param>
    /// <param name="count">The number of elements to read.</param>
    /// <param name="boxed">Whether to use the list-appending entry point.</param>
    /// <returns>The complete array values; propagates cancellation without another read.</returns>
    private static IList<object?> ReadArray(ReadBudgetStream stream, PrimitiveCodec codec, int count, bool boxed)
    {
        if (!boxed)
        {
            return PrimitiveArrayReader.Read(stream, codec, count);
        }

        var target = new List<object?>();
        object? last = PrimitiveArrayReader.ReadInto(stream, codec, count, target);
        Assert.AreEqual((byte)37, last);
        return target;
    }

    /// <summary>Observes the first input-block rental and can cancel after supplying that block.</summary>
    private sealed class ObservedStream : MemoryStream
    {
        private readonly PoolReturnListener listener;
        private readonly CancellationTokenSource? cancellation;

        /// <summary>Creates a non-exposable stream so the reader cannot bypass block reads with a direct span.</summary>
        /// <param name="bytes">The complete input bytes, retained by the stream.</param>
        /// <param name="listener">The current-thread pool observer.</param>
        /// <param name="cancellation">An optional cancellation source triggered after the first physical read.</param>
        public ObservedStream(byte[] bytes, PoolReturnListener listener, CancellationTokenSource? cancellation)
            : base(bytes)
        {
            this.listener = listener;
            this.cancellation = cancellation;
        }

        public bool ObservedRental { get; private set; }

        /// <summary>Watches the existing block rental before supplying bytes, then optionally requests cancellation.</summary>
        /// <param name="buffer">The caller's pooled block to fill.</param>
        /// <returns>The actual bytes supplied by the underlying memory stream.</returns>
        public override int Read(Span<byte> buffer)
        {
            if (!this.ObservedRental)
            {
                this.listener.Watch(this.listener.LastRental);
                this.ObservedRental = true;
            }

            int read = base.Read(buffer);
            this.cancellation?.Cancel();
            return read;
        }
    }
}
