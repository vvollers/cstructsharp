namespace CStructSharpTests;

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CStructSharp;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Pins the <see cref="ReadOnlySequence{T}"/> input forms: a single segment reads in place with the span path's
///     allocation, several segments read the same values, ranges, addresses, and lengths as the flat bytes, and a
///     sequence longer than the total read budget fails with the budget text.
/// </summary>
[TestClass]
public class SequenceInputTests
{
    private const string Layout = "struct item { uint16 id; uint8 flags; }; struct root { uint8 count; item items[count]; cstring name; uint16 *link; };";
    private static readonly byte[] Bytes = [2, 0x34, 0x12, 1, 0x78, 0x56, 2, (byte)'o', (byte)'k', 0, 12, 0, 0, 0, 0, 0, 0, 0, 0xEE, 0xFF,];

    /// <summary>Builds a sequence whose segments split <paramref name="bytes"/> at the given lengths.</summary>
    internal static ReadOnlySequence<byte> Segmented(byte[] bytes, params int[] lengths)
    {
        var first = new Segment(bytes.AsMemory(0, lengths[0]), 0);
        Segment last = first;
        int offset = lengths[0];
        for (int index = 1; index < lengths.Length; index++)
        {
            last = last.Append(bytes.AsMemory(offset, lengths[index]));
            offset += lengths[index];
        }

        if (offset < bytes.Length)
        {
            last = last.Append(bytes.AsMemory(offset));
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    /// <summary>A single-segment sequence reads in place: the same result as the span, with no allocation beyond the span path's.</summary>
    /// <remarks>Run alone so concurrent tests cannot perturb shared caches or buffer pools between the two measurements.</remarks>
    [TestMethod]
    [DoNotParallelize]
    public void SingleSegment_ReadsInPlace()
    {
        var layout = new CStruct(Layout, pointerSize: 8);
        var single = new ReadOnlySequence<byte>(Bytes);
        Assert.IsTrue(single.IsSingleSegment);

        StructValue viaSpan = layout.Parse(Bytes, "root");
        StructValue viaSequence = layout.Parse(single, "root");
        Assert.AreEqual(viaSpan.Get<string>("name"), viaSequence.Get<string>("name"));
        Assert.AreEqual(viaSpan.Get<ushort>("items[1].id"), viaSequence.Get<ushort>("items[1].id"));
        Assert.AreEqual(viaSpan.Get<ushort>("link.value"), viaSequence.Get<ushort>("link.value"));

        for (int warm = 0; warm < 50; warm++)
        {
            layout.Parse(single, "root");
            layout.Parse(Bytes.AsSpan(), "root");
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++)
        {
            layout.Parse(Bytes.AsSpan(), "root");
        }

        long span = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 100; index++)
        {
            layout.Parse(single, "root");
        }

        long sequence = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(span, sequence, "a single segment takes the span path without a copy");
    }

    /// <summary>A custom codec observes the original byte address for one segment, and copied storage for multiple segments.</summary>
    [TestMethod]
    public void SingleSegment_PassesTheOriginalStorageToTheCodec()
    {
        byte[] bytes = [7, 8];
        var codec = new StorageIdentityCodec(bytes);
        var layout = new CStruct("struct root { storage_identity probe; uint8 tail; };", compilationOptions: new CStructCompilationOptions { Codecs = [codec], });
        var single = new ReadOnlySequence<byte>(bytes);

        Assert.IsTrue(layout.Parse(bytes.AsSpan(), "root").Get<bool>("probe"));
        Assert.IsTrue(layout.Parse(single, "root").Get<bool>("probe"), "the codec must receive the caller's original storage");
        Assert.IsTrue(layout.ParseWithDebug(single, "root").Value.Get<bool>("probe"));
        Assert.AreEqual(true, layout.ReadValue(single, "root.probe"));
        Assert.IsTrue(layout.ReadValue<bool>(single, "root.probe"));
        Assert.IsTrue(((StructValue)layout.ReadValueWithDebug(single, "root").Value!).Get<bool>("probe"));
        Assert.IsTrue(layout.TryReadValue(single, "root.probe", out bool borrowed));
        Assert.IsTrue(borrowed);
        Assert.IsTrue(layout.ParseMany(single, "root").Single().Get<bool>("probe"));
        Assert.IsFalse(layout.Parse(Segmented(bytes, 1, 1), "root").Get<bool>("probe"), "the observer must detect the actual multi-segment copy");
    }

    /// <summary>Several segments read exactly as the flat bytes do: values, debug ranges, addresses, and array lengths; a segment boundary inside a primitive is invisible.</summary>
    [TestMethod]
    public void MultipleSegments_ReadLikeTheFlatBytes()
    {
        var layout = new CStruct(Layout, pointerSize: 8);
        ReadOnlySequence<byte> segmented = Segmented(Bytes, 2, 3, 4, 1);
        Assert.IsFalse(segmented.IsSingleSegment);
        Assert.AreEqual(Bytes.Length, segmented.Length);

        ParseResult flat = layout.ParseWithDebug(Bytes, "root");
        ParseResult split = layout.ParseWithDebug(segmented, "root");
        Assert.AreEqual(flat.Value.Get<string>("name"), split.Value.Get<string>("name"));
        Assert.AreEqual(flat.Value.Get<ushort>("items[0].id"), split.Value.Get<ushort>("items[0].id"));
        Assert.AreEqual(flat.Value.Get<ushort>("link.value"), split.Value.Get<ushort>("link.value"));
        Assert.AreEqual(flat.Debug.Count, split.Debug.Count);
        for (int index = 0; index < flat.Debug.Count; index++)
        {
            Assert.AreEqual(flat.Debug[index].Path, split.Debug[index].Path);
            Assert.AreEqual(flat.Debug[index].Start, split.Debug[index].Start);
            Assert.AreEqual(flat.Debug[index].End, split.Debug[index].End);
        }

        Assert.AreEqual(layout.ResolveAddress(Bytes, "root.items[1].flags"), layout.ResolveAddress(segmented, "root.items[1].flags"));
        Assert.AreEqual(layout.GetArrayLength(Bytes, "root.items"), layout.GetArrayLength(segmented, "root.items"));
        Assert.AreEqual(layout.ReadValue<ushort>(Bytes, "root.items[1].id"), layout.ReadValue<ushort>(segmented, "root.items[1].id"));
        Assert.AreEqual(layout.ReadValue(Bytes, "root.name"), layout.ReadValue(segmented, "root.name"));
        ReadResult flatRead = layout.ReadValueWithDebug(Bytes, "root.items[1]");
        ReadResult splitRead = layout.ReadValueWithDebug(segmented, "root.items[1]");
        Assert.AreEqual(flatRead.Debug.Count, splitRead.Debug.Count);
        Assert.AreEqual(((StructValue)flatRead.Value!).Get<byte>("flags"), ((StructValue)splitRead.Value!).Get<byte>("flags"));

        Assert.IsTrue(layout.TryReadValue(segmented, "root.items[1].id", out ushort id));
        Assert.AreEqual((ushort)0x5678, id);
    }

    /// <summary>A truncated sequence fails with the flat bytes' message; a Try form returns false; a sequence longer than the total read budget fails with the budget text, as a stream does.</summary>
    [TestMethod]
    public void Failures_MatchTheFlatBytesAndTheBudget()
    {
        var layout = new CStruct(Layout, pointerSize: 8);
        byte[] truncated = Bytes[..8];
        CStructReadException flat = Assert.Throws<CStructReadException>(() => layout.Parse(truncated, "root"));
        CStructReadException split = Assert.Throws<CStructReadException>(() => layout.Parse(Segmented(truncated, 3, 3), "root"));
        Assert.AreEqual(flat.Message, split.Message);
        Assert.AreEqual(flat.Offset, split.Offset);
        Assert.IsFalse(layout.TryReadValue<StructValue>(Segmented(truncated, 3, 3), "root", out _));

        var budget = new ReadOptions { MaxTotalBytesRead = 6, };
        CStructReadLimitException viaStream = Assert.Throws<CStructReadLimitException>(() => layout.Parse(new MemoryStream(Bytes), "root", options: budget));
        CStructReadLimitException viaSequence = Assert.Throws<CStructReadLimitException>(() => layout.Parse(Segmented(Bytes, 4, 4, 4), "root", options: budget));
        Assert.AreEqual(viaStream.Message, viaSequence.Message);

        // An empty sequence is a short read, not an argument error.
        Assert.Throws<CStructReadException>(() => layout.Parse(ReadOnlySequence<byte>.Empty, "root"));
        Assert.Throws<CStructReadException>(() => layout.Parse(Segmented(new byte[4], 2, 2), "root"));
    }

    /// <summary>Observes input storage identity without retaining a borrowed span or relying on allocation counters.</summary>
    /// <param name="expected">The caller-owned input whose first byte must be borrowed in the single-segment path.</param>
    private sealed class StorageIdentityCodec(byte[] expected) : ICustomCodec
    {
        public string Name => "storage_identity";

        public int? FixedSize => 1;

        public int Alignment => 1;

        /// <summary>Reports whether the supplied first byte is the original managed byte, consuming one byte.</summary>
        /// <param name="source">The borrowed input window.</param>
        /// <param name="value">True only when the window starts in the caller's original storage.</param>
        /// <param name="bytesConsumed">One on success, otherwise zero.</param>
        /// <returns>Done for a nonempty window; otherwise NeedMoreData.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            if (source.IsEmpty)
            {
                value = null;
                bytesConsumed = 0;
                return OperationStatus.NeedMoreData;
            }

            value = Unsafe.AreSame(ref expected[0], ref MemoryMarshal.GetReference(source));
            bytesConsumed = 1;
            return OperationStatus.Done;
        }

        /// <summary>Rejects writes because this test codec only observes input identity.</summary>
        /// <param name="destination">Unused output window.</param>
        /// <param name="value">Unused value.</param>
        /// <param name="bytesWritten">Not assigned because the method always throws.</param>
        /// <returns>No result; writing is unsupported.</returns>
        /// <exception cref="NotSupportedException">Always thrown.</exception>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten) => throw new NotSupportedException();
    }

    /// <summary>Links borrowed memory regions into a logical byte sequence without copying their contents.</summary>
    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        /// <summary>Creates one segment at its absolute byte index within the logical sequence.</summary>
        /// <param name="memory">The caller-owned bytes borrowed by this segment.</param>
        /// <param name="runningIndex">The number of bytes preceding this segment.</param>
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            this.Memory = memory;
            this.RunningIndex = runningIndex;
        }

        /// <summary>Links the next borrowed region immediately after this segment.</summary>
        /// <param name="memory">The next caller-owned byte region.</param>
        /// <returns>The appended segment, used to continue building the chain.</returns>
        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, this.RunningIndex + this.Memory.Length);
            this.Next = next;
            return next;
        }
    }
}
