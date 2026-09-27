namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Memory;

/// <summary>Checks failure atomicity promises, custom codecs, and address boundary behavior.</summary>
[TestClass]
public class MemoryFailureTests
{
    /// <summary>A later write failure reports the confirmed prefix without concealing partial mutation.</summary>
    [TestMethod]
    public void Patch_ReportsPartialCommit()
    {
        var source = new FailingSource();
        var mapped = new MappedMemorySource("mapped", [new(100, new MemoryRegion(source, 0, 1)), new(101, new MemoryRegion(source, 2, 1)),]);
        MemoryPatch patch = MemoryPatch.Create(new MemoryRegion(mapped, 100, 2), new byte[] { 8, 9, });
        MemoryPatchCommitException failure = Assert.Throws<MemoryPatchCommitException>(() => patch.Commit());
        Assert.AreEqual(1L, failure.CompletedBytes);
        Assert.AreEqual(1, failure.FragmentIndex);
        Assert.IsInstanceOfType<IOException>(failure.InnerException);
        Assert.AreEqual(8, source.Bytes[0]);
    }

    /// <summary>Fixed custom types receive finite streams and participate in metadata creation, inspection, and patches.</summary>
    [TestMethod]
    public void Codec_ReadsAndWritesThroughFiniteRegion()
    {
        var codec = new LetterCodec();
        var options = new CStructCompilationOptions { Codecs = [codec,], };
        var layout = new CStruct("struct Message { letter text; };", compilationOptions: options);
        var session = new MemorySession(PortableMemorySchema.Create(layout, "Message"));
        byte[] bytes = session.Serialize("Message", new Dictionary<string, object?> { ["text"] = "A", });
        var source = new ByteArrayMemorySource("image", bytes);
        var mapped = new MappedMemorySource("high", [new(ulong.MaxValue, new MemoryRegion(source, 0, 1)),]);
        var region = new MemoryRegion(mapped, ulong.MaxValue, 1);
        MemoryInspection inspected = session.Inspect(region, "Message", "text");
        Assert.AreEqual("A", inspected.Value);
        Assert.AreEqual(ulong.MaxValue, inspected.Selection.Region.Address);
        Assert.AreEqual(0UL, inspected.BackingRegions[0].Address);
        session.PlanUpdate(region, "Message", "text", "B").Commit();
        Assert.AreEqual("B", session.Read(region, "Message", "text"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new MemoryRegion(mapped, ulong.MaxValue, 2));
    }

    /// <summary>Bad paths, finite-stream seeks, and canceled graph operations fail before unbounded work.</summary>
    [TestMethod]
    public void Boundaries_CancelAndRejectMalformedPaths()
    {
        var source = new ByteArrayMemorySource("image", new byte[8]);
        var region = new MemoryRegion(source, 0, 8);
        var session = new MemorySession(new MemorySchema([new("u", "u", MemoryTypeKind.Scalar, 8, scalarType: "uint64"),]));
        foreach (string path in new[] { ".", "x.", "[-1]", "[x]", "[0]x", "..", })
        {
            Assert.Throws<ArgumentException>(() => session.Resolve(region, "u", path));
        }

        using Stream stream = region.OpenRead();
        Assert.AreEqual(7L, stream.Seek(-1, SeekOrigin.End));
        Assert.AreEqual(0L, stream.Seek(-7, SeekOrigin.Current));
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(9, SeekOrigin.Begin));

        // Cancellation propagates through the shared callback budget.
        Assert.Throws<OperationCanceledException>(() => MemoryWalker.Tree(region, (_, _) => [], context: new MemoryAccessContext(cancellationToken: new CancellationToken(true))));
    }

    /// <summary>Independent writable adapter that fails only the second physical fragment.</summary>
    private sealed class FailingSource : IWritableMemorySource
    {
        public byte[] Bytes { get; } = new byte[4];

        public string Id => "failing";

        public long Generation => 0;

        /// <inheritdoc/>
        public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
        {
            context.Charge(this.Id, address, destination.Length);
            this.Bytes.AsSpan((int)address, destination.Length).CopyTo(destination);
            return destination.Length;
        }

        /// <inheritdoc/>
        public void Write(ulong address, ReadOnlySpan<byte> bytes, MemoryAccessContext context)
        {
            context.Charge(this.Id, address, bytes.Length);
            if (address == 2)
            {
                throw new IOException("Synthetic device failure.");
            }

            bytes.CopyTo(this.Bytes.AsSpan((int)address));
        }
    }

    /// <summary>A tiny independently authored one-byte codec whose value is a string.</summary>
    private sealed class LetterCodec : ICustomCodec
    {
        public string Name => "letter";

        public int? FixedSize => 1;

        public int Alignment => 1;

        /// <inheritdoc/>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            if (source.IsEmpty)
            {
                value = null;
                bytesConsumed = 0;
                return OperationStatus.NeedMoreData;
            }

            value = ((char)source[0]).ToString();
            bytesConsumed = 1;
            return OperationStatus.Done;
        }

        /// <inheritdoc/>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            destination[0] = checked((byte)((string)value)[0]);
            bytesWritten = 1;
            return OperationStatus.Done;
        }
    }
}
