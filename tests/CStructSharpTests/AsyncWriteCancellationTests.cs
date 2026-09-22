namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;

/// <summary>Checks cancellation before async output acquisition and between staged composite-array elements.</summary>
[TestClass]
public class AsyncWriteCancellationTests
{
    /// <summary>A direct operation token reaches the synchronous staging writer before a second record is encoded.</summary>
    /// <param name="update">Whether to replace existing bytes rather than write a newly serialized value.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CancellationDuringStaging_StopsBeforeTheNextElement(bool update)
    {
        using var cancellation = new CancellationTokenSource();
        var codec = new CancellingCodec(cancellation);
        var layout = new CStruct("struct item { cancel_byte value; }; struct root { item items[2]; };", compilationOptions: new CStructCompilationOptions { Codecs = [codec,], });
        object[] items = [new Dictionary<string, object?> { ["value"] = (byte)1, }, new Dictionary<string, object?> { ["value"] = (byte)2, },];
        byte[] original = [0xAA, 0xBB, 7, 8,];
        using var destination = new MemoryStream(original.ToArray());
        destination.Position = 2;

        // Cancelling from the first custom field makes the next composite-array boundary observable.
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            if (update)
            {
                await layout.UpdateAsync(destination, "root.items", items, cancellationToken: cancellation.Token);
            }
            else
            {
                await layout.WriteAsync(destination, "root", new Dictionary<string, object?> { ["items"] = items, }, cancellationToken: cancellation.Token);
            }
        });

        Assert.AreEqual(1, codec.Writes, "Cancellation must stop staging, not merely prevent final output.");
        Assert.AreEqual(2L, destination.Position);
        CollectionAssert.AreEqual(original, destination.ToArray());
    }

    /// <summary>A pre-cancelled write rejects the operation before inspecting a deliberately invalid value path.</summary>
    [TestMethod]
    public async Task PreCancelledWrite_PrecedesPathValidation()
    {
        var layout = new CStruct("struct root { uint8 value; };");
        using var destination = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // The async boundary must report cancellation before Serialize validates the null path.
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.WriteAsync(destination, null!, (byte)1, cancellationToken: cancellation.Token));
        Assert.AreEqual(0L, destination.Length);
    }

    /// <summary>A pre-cancelled update does not probe the caller's stream position before failing.</summary>
    [TestMethod]
    public async Task PreCancelledUpdate_DoesNotReadTheOrigin()
    {
        var layout = new CStruct("struct root { uint8 value; };");
        using var destination = new PositionObservedStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Position probes are observable even when no bytes would be acquired or written.
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await layout.UpdateAsync(destination, "root.value", (byte)1, cancellationToken: cancellation.Token));
        Assert.AreEqual(0, destination.PositionReads);
    }

    /// <summary>Cancels after encoding the first byte, allowing the enclosing writer to enforce its own boundaries.</summary>
    /// <param name="cancellation">The direct operation's token source.</param>
    private sealed class CancellingCodec(CancellationTokenSource cancellation) : ICustomCodec
    {
        public string Name => "cancel_byte";

        public int? FixedSize => 1;

        public int Alignment => 1;

        public int Writes { get; private set; }

        /// <summary>Reads a baseline byte without cancellation or retained input ownership.</summary>
        /// <param name="source">Borrowed baseline bytes.</param>
        /// <param name="value">The first byte, or null if unavailable.</param>
        /// <param name="bytesConsumed">One on success, otherwise zero.</param>
        /// <returns>Done or NeedMoreData.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            value = source.IsEmpty ? null : source[0];
            bytesConsumed = source.IsEmpty ? 0 : 1;
            return source.IsEmpty ? OperationStatus.NeedMoreData : OperationStatus.Done;
        }

        /// <summary>Encodes a byte and then requests cancellation without throwing from the codec itself.</summary>
        /// <param name="destination">Borrowed staging output.</param>
        /// <param name="value">The byte to encode.</param>
        /// <param name="bytesWritten">One on success, otherwise zero.</param>
        /// <returns>Done after cancellation is requested, or DestinationTooSmall.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            bytesWritten = 0;
            if (destination.IsEmpty)
            {
                return OperationStatus.DestinationTooSmall;
            }

            destination[0] = (byte)value;
            bytesWritten = 1;
            this.Writes++;
            cancellation.Cancel();
            return OperationStatus.Done;
        }
    }

    /// <summary>Counts origin reads while retaining ordinary writable, readable and seekable capabilities.</summary>
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
