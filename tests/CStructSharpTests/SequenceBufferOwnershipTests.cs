namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks that every segmented-input entrypoint returns its copy buffer after success and failure.</summary>
[TestClass]
public class SequenceBufferOwnershipTests
{
    /// <summary>Single-segment address and length queries do not rent an intermediate sequence-copy buffer.</summary>
    /// <param name="addressQuery">Whether to resolve an element address rather than query its array length.</param>
    [TestMethod]
    [DoNotParallelize]
    [DataRow(true)]
    [DataRow(false)]
    public void SingleSegmentQueries_DoNotRentACopy(bool addressQuery)
    {
        var layout = new CStruct("struct root { uint8 values[3]; };");
        var source = new ReadOnlySequence<byte>(new byte[] { 11, 22, 33, });
        using var returns = new PoolReturnListener();
        Assert.AreEqual(0, returns.LastRental, "The observer starts with no operation rental.");
        if (addressQuery)
        {
            Assert.AreEqual(2L, layout.ResolveAddress(source, "root.values[2]"));
        }
        else
        {
            Assert.AreEqual(3, layout.GetArrayLength(source, "root.values"));
        }

        Assert.AreEqual(0, returns.LastRental, "A fixed-shape query over one segment must not rent a copy buffer.");
    }

    /// <summary>Async record iteration rejects an unreadable stream before returning an iterator.</summary>
    [TestMethod]
    public void AsyncRecords_RejectUnreadableInputImmediately()
    {
        var layout = new CStruct("struct root { uint8 value; };");
        var source = new MemoryStream();
        source.Dispose();

        // The argument contract is checked by the call itself, not delayed until MoveNextAsync.
        ArgumentException failure = Assert.Throws<ArgumentException>(() => layout.ParseManyAsync(source, "root"));
        Assert.AreEqual("stream", failure.ParamName);
        StringAssert.StartsWith(failure.Message, "Reading requires a readable stream.");
    }

    /// <summary>Each synchronous sequence operation owns its temporary copy only until the operation returns or throws.</summary>
    /// <param name="operation">The sequence entrypoint to exercise.</param>
    /// <param name="invalidPath">Whether the operation must fail after copying its input.</param>
    [TestMethod]
    [DoNotParallelize]
    [DataRow("parse", false)]
    [DataRow("debug", false)]
    [DataRow("value", false)]
    [DataRow("typed", false)]
    [DataRow("value-debug", false)]
    [DataRow("address", false)]
    [DataRow("length", false)]
    [DataRow("parse", true)]
    [DataRow("debug", true)]
    [DataRow("value", true)]
    [DataRow("typed", true)]
    [DataRow("value-debug", true)]
    [DataRow("address", true)]
    [DataRow("length", true)]
    public void SegmentedOperations_ReturnTheirExactCopy(string operation, bool invalidPath)
    {
        var layout = new CStruct("struct root { uint8 count; uint8 data[count]; };");
        using var returns = new PoolReturnListener();
        using var observed = new ObservedMemory(returns);
        var first = new Segment(observed.Descriptor);
        Segment last = first.Append(new byte[] { 11, 22, });
        var source = new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);

        if (invalidPath)
        {
            // Every entrypoint copies the input before checking the requested layout path.
            Assert.Throws<CStructException>(() => Invoke(layout, source, operation, "missing"));
        }
        else
        {
            Invoke(layout, source, operation, "root");
        }

        Assert.IsTrue(observed.Watched, "The source must identify the rental while the copy is in progress.");
        Assert.IsTrue(returns.Returned, "The exact sequence-copy array must be returned, including on a path failure.");
    }

    /// <summary>Calls one public sequence overload and checks a small, concrete result.</summary>
    /// <param name="layout">The compiled count-and-data layout.</param>
    /// <param name="source">The segmented bytes for one record.</param>
    /// <param name="operation">The selected overload family.</param>
    /// <param name="root">The root name, or an intentionally missing name.</param>
    private static void Invoke(CStruct layout, ReadOnlySequence<byte> source, string operation, string root)
    {
        switch (operation)
        {
        case "parse":
            Assert.AreEqual((byte)2, layout.Parse(source, root).Get<byte>("count"));
            break;
        case "debug":
            Assert.AreEqual((byte)2, layout.ParseWithDebug(source, root).Value.Get<byte>("count"));
            break;
        case "value":
            Assert.AreEqual((byte)2, layout.ReadValue(source, root + ".count"));
            break;
        case "typed":
            Assert.AreEqual((byte)2, layout.ReadValue<byte>(source, root + ".count"));
            break;
        case "value-debug":
            Assert.AreEqual((byte)2, ((StructValue)layout.ReadValueWithDebug(source, root).Value!).Get<byte>("count"));
            break;
        case "address":
            Assert.AreEqual(1L, layout.ResolveAddress(source, root + ".data"));
            break;
        case "length":
            Assert.AreEqual(2, layout.GetArrayLength(source, root + ".data"));
            break;
        default:
            Assert.Fail("Unknown test entrypoint.");
            break;
        }
    }

    /// <summary>Identifies the outstanding copy rental at the moment the source bytes are requested.</summary>
    /// <param name="returns">The current-thread pool observer.</param>
    private sealed class ObservedMemory(PoolReturnListener returns) : MemoryManager<byte>
    {
        private readonly byte[] bytes = [2,];

        public Memory<byte> Descriptor => this.CreateMemory(1);

        public bool Watched { get; private set; }

        /// <summary>Watches the latest rental before returning the one-byte source count.</summary>
        /// <returns>The borrowed count byte.</returns>
        public override Span<byte> GetSpan()
        {
            returns.Watch(returns.LastRental);
            this.Watched = true;
            return this.bytes;
        }

        /// <summary>Rejects pinning because managed sequence copying does not need it.</summary>
        /// <param name="elementIndex">Unused requested index.</param>
        /// <returns>No handle is returned.</returns>
        public override MemoryHandle Pin(int elementIndex = 0) => throw new NotSupportedException();

        /// <summary>Releases no pin because this fixture never creates one.</summary>
        public override void Unpin()
        {
        }

        /// <summary>Releases no external resource; the source array is managed.</summary>
        /// <param name="disposing">Whether disposal is explicit.</param>
        protected override void Dispose(bool disposing)
        {
        }
    }

    /// <summary>Links two borrowed memory regions without accessing their spans during construction.</summary>
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        /// <summary>Stores the first borrowed region.</summary>
        /// <param name="memory">Bytes in this region.</param>
        public Segment(ReadOnlyMemory<byte> memory) => this.Memory = memory;

        /// <summary>Appends a region at the next logical byte offset.</summary>
        /// <param name="memory">The next borrowed region.</param>
        /// <returns>The appended segment.</returns>
        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory) { RunningIndex = this.RunningIndex + this.Memory.Length, };
            this.Next = next;
            return next;
        }
    }
}
