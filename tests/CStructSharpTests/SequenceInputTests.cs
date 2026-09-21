namespace CStructSharpTests;

using System.Buffers;
using CStructSharp;
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
    [TestMethod]
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

    private sealed class Segment : ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            this.Memory = memory;
            this.RunningIndex = runningIndex;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, this.RunningIndex + this.Memory.Length);
            this.Next = next;
            return next;
        }
    }
}
