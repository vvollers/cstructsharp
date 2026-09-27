namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks that async wrappers preserve options and observe cancellation during the buffered decoder itself.</summary>
[TestClass]
public class AsyncOperationBoundaryTests
{
    /// <summary>A pre-cancelled operation stops before probing the caller's current input position.</summary>
    [TestMethod]
    public async Task PreCancelledRead_DoesNotInspectStreamPosition()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var source = new PositionObservedStream();
        var layout = new CStruct("struct root { uint8 value; };");

        // Once argument/capability checks pass, cancellation must precede input acquisition and origin lookup.
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.ParseAsync(source, "root", cancellationToken: cancellation.Token));
        Assert.AreEqual(0, source.PositionReads);
    }

    /// <summary>A separately supplied token reaches the decoder, and cancellation is never turned into a failed Try result.</summary>
    /// <param name="visible">Whether input is borrowed directly or acquired in a separate buffer.</param>
    /// <param name="tryForm">Whether to use the non-throwing value-read wrapper.</param>
    [TestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public async Task CancellationInsideCodec_StopsBufferedDecodeAndRestoresOrigin(bool visible, bool tryForm)
    {
        using var cancellation = new CancellationTokenSource();
        var codec = new CancellingByte(cancellation);
        var layout = new CStruct("struct item { cancel_byte value; }; struct root { item items[2]; };", compilationOptions: new CStructCompilationOptions { Codecs = [codec,], });
        byte[] bytes = [99, 98, 17, 29,];
        using var source = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: visible) { Position = 2, };

        // The first codec cancels after acquisition; the next array-element boundary must see the operation token.
        if (tryForm)
        {
            // A Try result must never swallow cancellation raised inside the decoder.
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.TryReadValueAsync<StructValue>(source, "root", cancellationToken: cancellation.Token));
        }
        else
        {
            // The ordinary parse wrapper propagates cancellation from the same decoding boundary.
            await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.ParseAsync(source, "root", cancellationToken: cancellation.Token));
        }

        Assert.AreEqual(1, codec.Reads);
        Assert.AreEqual(2L, source.Position);
        Assert.IsTrue(source.CanRead);
    }

    /// <summary>Library continuations do not dispatch back through the caller's synchronization context.</summary>
    /// <param name="tryForm">Whether to include the extra await inside the non-throwing wrapper.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PendingRead_DoesNotCaptureCallerContext(bool tryForm)
    {
        var layout = new CStruct("struct root { uint16 value; };");
        using var source = new AsyncReadTestSupport.GatedStream();
        var context = new AsyncReadTestSupport.RecordingContext();
        SynchronizationContext? previous = SynchronizationContext.Current;
        Task pending;
        try
        {
            SynchronizationContext.SetSynchronizationContext(context);
            pending = tryForm ? layout.TryReadValueAsync<StructValue>(source, "root").AsTask() : layout.ParseAsync(source, "root").AsTask();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        source.ReleaseRead();
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(0, context.Posts);
        Assert.AreEqual(2L, source.Position);
    }

    /// <summary>Query wrappers reject missing paths before input capability checks and name their public path parameter.</summary>
    /// <param name="lengthQuery">Whether to query array length instead of field address.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task NullQueryPath_FailsAtTheWrapper(bool lengthQuery)
    {
        var layout = new CStruct("struct root { uint8 values[2]; };");
        using var source = new UnreadableStream();

        // Missing path wins over the unreadable-stream error, whether the wrapper throws immediately or on await.
        ArgumentNullException failure = await Assert.ThrowsAsync<ArgumentNullException>(async () =>
        {
            if (lengthQuery)
            {
                await layout.GetArrayLengthAsync(source, null!);
            }
            else
            {
                await layout.ResolveAddressAsync(source, null!);
            }
        });
        Assert.AreEqual("path", failure.ParamName);
        Assert.AreEqual(0L, source.Position);
    }

    /// <summary>Adding an operation token preserves the caller's independent element-count limit.</summary>
    [TestMethod]
    public async Task OperationToken_PreservesOtherReadOptions()
    {
        using var cancellation = new CancellationTokenSource();
        var layout = new CStruct("struct root { uint8 values[2]; };");
        using var source = new MemoryStream(new byte[] { 99, 17, 29, }) { Position = 1, };

        // A live but uncancelled token still requires a copied options record, not a fresh record with default limits.
        await Assert.ThrowsAsync<CStructReadLimitException>(async () => await layout.ParseAsync(
            source, "root", options: new ReadOptions { MaxArrayElements = 1, }, cancellationToken: cancellation.Token));
        Assert.AreEqual(1L, source.Position);
    }

    /// <summary>Unreadable input identifies the invalid stream and explains the required capability.</summary>
    [TestMethod]
    public async Task UnreadableInput_ExplainsRequiredCapability()
    {
        var layout = new CStruct("struct root { uint8 value; };");
        using var source = new UnreadableStream();

        // Capability validation happens before buffering or invoking a field codec.
        ArgumentException failure = await Assert.ThrowsAsync<ArgumentException>(async () => await layout.ParseAsync(source, "root"));
        Assert.AreEqual("stream", failure.ParamName);
        StringAssert.StartsWith(failure.Message, "Reading requires a readable stream.");
    }

    /// <summary>Cancels after decoding exactly one byte, before the next field is reached.</summary>
    private sealed class CancellingByte : ICustomCodec
    {
        private readonly CancellationTokenSource cancellation;

        /// <summary>Retains the test-owned source to cancel when this codec reads its field.</summary>
        /// <param name="cancellation">Cancellation source owned and disposed by the test.</param>
        public CancellingByte(CancellationTokenSource cancellation)
        {
            this.cancellation = cancellation;
        }

        public string Name => "cancel_byte";

        public int? FixedSize => 1;

        public int Alignment => 1;

        public int Reads { get; private set; }

        /// <summary>Reads a byte and cancels the operation after supplying its value.</summary>
        /// <param name="source">Borrowed input window.</param>
        /// <param name="value">Decoded byte, or null when no byte is available.</param>
        /// <param name="bytesConsumed">One on success; zero for incomplete input.</param>
        /// <returns>Done for one byte, or NeedMoreData for an empty window.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            value = null;
            bytesConsumed = 0;
            if (source.IsEmpty)
            {
                return OperationStatus.NeedMoreData;
            }

            this.Reads++;
            value = source[0];
            bytesConsumed = 1;
            this.cancellation.Cancel();
            return OperationStatus.Done;
        }

        /// <summary>Rejects writes because this fixture exercises only reader cancellation.</summary>
        /// <param name="destination">Unused destination window.</param>
        /// <param name="value">Unused input value.</param>
        /// <param name="bytesWritten">Always zero.</param>
        /// <returns>InvalidData without modifying the destination.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            bytesWritten = 0;
            return OperationStatus.InvalidData;
        }
    }

    /// <summary>Reports that reading is unavailable even though the underlying memory stream exists.</summary>
    private sealed class UnreadableStream : MemoryStream
    {
        public override bool CanRead => false;
    }

    /// <summary>Counts position probes without changing ordinary memory-stream behavior.</summary>
    private sealed class PositionObservedStream : MemoryStream
    {
        public int PositionReads { get; private set; }

        public override long Position
        {
            get
            {
                this.PositionReads++;
                return base.Position;
            }

            set => base.Position = value;
        }
    }
}
