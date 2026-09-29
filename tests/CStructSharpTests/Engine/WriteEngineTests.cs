namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Values;

/// <summary>
///     The compiled write engine's step semantics where the sweeps and corpora do not reach, each compared with the
///     interpreter through the differential harness and pinned to its expected outcome: every codec at every write budget
///     and span capacity, the prefix a failed write leaves in a span, the narrow text path that fails after writing earlier
///     characters, tail padding written and charged, the inactive conditional member check, captures of converted supplied
///     values, and the memory destination's own rules (gaps, read-back, budget, chunked zero fill).
/// </summary>
[TestClass]
public class WriteEngineTests
{
    /// <summary>A packed layout with one scalar or array of every codec the engine writes through a codec writer, plus a count that keeps the root off the direct path.</summary>
    private const string CodecLayout = """
        enum e16 : uint16 { A = 1, B = 4660 };
        struct rec {
          uint8 n; uint8 bytes[n];
          int48 a; uint48 b; int128 c; uint128 d; float16 h;
          fixed16_16 f1; ufixed16_16 f2; fixed2_30 f3; ufixed8_8 f4;
          uuid u; guid g;
          uleb128_32 l1; uleb128_64 l2; sleb128_32 l3; sleb128_64 l4;
          char ch; latin1 la; cp437 cp; utf8 u8; wchar w;
          e16 en; e16 ens[2]; int48 as[2]; wchar ws[2]; char cs[3]; utf8 text[4]; cstring s;
          uint8 tail;
        };
        """;

    /// <summary>The execution paths each case runs under.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.GeneralOnly];

    /// <summary>Gets valid input for <see cref="CodecLayout"/>, field by field.</summary>
    private static byte[] CodecData =>
    [
        0x02, 0xAA, 0xBB, // n, bytes
        0xFB, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // a = -5
        0x07, 0, 0, 0, 0, 0, // b = 7
        0xF7, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // c = -9
        0x0A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // d = 10
        0x00, 0x3E, // h = 1.5
        0x00, 0x80, 0x01, 0x00, // f1 = 1.5
        0x00, 0x40, 0x02, 0x00, // f2 = 2.25
        0x00, 0x00, 0x00, 0x20, // f3 = 0.5
        0x80, 0x03, // f4 = 3.5
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, // u
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, // g
        0xAC, 0x02, // l1 = 300
        0xF0, 0xA2, 0x04, // l2 = 70000
        0x7D, // l3 = -3
        0xD4, 0x7D, // l4 = -300
        0x41, 0xE9, 0x80, 0x7A, // ch, la, cp, u8
        0xA9, 0x03, // w = U+03A9
        0x34, 0x12, // en = B
        0x01, 0x00, 0x34, 0x12, // ens
        1, 0, 0, 0, 0, 0, 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // as = 1, -2
        0x41, 0x00, 0x42, 0x00, // ws = "AB"
        0x78, 0x79, 0x00, // cs
        0x68, 0x69, 0x00, 0x00, // text
        0x6F, 0x6B, 0x00, // s = "ok"
        0x09, // tail
    ];

    /// <summary>
    ///     The parsed value of every codec serializes back to its input through the engine, and every write budget from 1
    ///     to one past the output's length and every span capacity fail or succeed exactly as the interpreter does.
    /// </summary>
    [TestMethod]
    public void Codecs_WriteIdenticallyAtEveryBudgetAndCapacity()
    {
        var layout = new CStruct(CodecLayout);
        byte[] data = CodecData;
        StructValue value = layout.Parse(data.AsSpan(), "rec", options: EngineSelections.InterpreterOnly());
        CollectionAssert.AreEqual(data, layout.Serialize("rec", value, options: EngineSelections.EngineRequired(new WriteOptions())));
        foreach (ExecutionPath path in Paths)
        {
            for (int limit = 1; limit <= data.Length + 1; limit++)
            {
                EngineDifferential.AssertSame(EngineOperations.Serialize(layout, "rec", value, options: new WriteOptions { MaxTotalBytesWritten = limit, }), true, path);
                EngineDifferential.AssertSame(EngineOperations.SerializeToSpan(layout, limit - 1, "rec", value), true, path);
                EngineDifferential.AssertSame(EngineOperations.Serialize(layout, "rec", value, options: new WriteOptions { MaxStringBytes = limit - 1, }), true, path);
            }
        }
    }

    /// <summary>
    ///     A value that cannot be encoded fails at its member after the members before it were written: a span keeps them
    ///     (and its unused capacity), the failure names the member and the destination position, and an array has no output.
    /// </summary>
    [TestMethod]
    public void FailedWrite_LeavesTheEarlierMembersInTheSpan()
    {
        var layout = new CStruct("struct rec { uint8 a; uint16 b; uint8 n; uint8 items[n]; uint8 c; };");
        var value = new Dictionary<string, object?> { ["a"] = (byte)1, ["b"] = (ushort)0x0302, ["n"] = (byte)1, ["items"] = new byte[] { 4, }, ["c"] = 300, };
        byte[] destination = Enumerable.Repeat(EngineOperations.Unwritten, 8).ToArray();
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize(destination.AsSpan(), "rec", value, options: EngineSelections.EngineRequired(new WriteOptions())));
        Assert.AreEqual("c", failure.Member);
        Assert.AreEqual(5L, failure.Offset);
        Assert.AreEqual("rec", failure.Path);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 1, 4, 0xCC, 0xCC, 0xCC, }, destination);
        Assert.Throws<CStructWriteException>(() => layout.Serialize("rec", value, options: EngineSelections.EngineRequired(new WriteOptions())));
        foreach (ExecutionPath path in Paths)
        {
            EngineDifferential.AssertSame(EngineOperations.SerializeToSpan(layout, 8, "rec", value), true, path);
            EngineDifferential.AssertSame(EngineOperations.Serialize(layout, "rec", value), true, path);
        }
    }

    /// <summary>
    ///     A narrow <c>char[N]</c> holding a character above 255 cannot take the block path, so it is written character by
    ///     character and fails at that character with the earlier characters written.
    /// </summary>
    [TestMethod]
    public void NarrowText_FailsAfterTheEarlierCharacters()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 items[n]; char name[4]; };");
        var value = new Dictionary<string, object?> { ["n"] = (byte)0, ["items"] = Array.Empty<byte>(), ["name"] = "abĀ", };
        byte[] destination = Enumerable.Repeat(EngineOperations.Unwritten, 6).ToArray();
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize(destination.AsSpan(), "rec", value, options: EngineSelections.EngineRequired(new WriteOptions())));
        Assert.AreEqual("name", failure.Member);
        CollectionAssert.AreEqual(new byte[] { 0, (byte)'a', (byte)'b', 0xCC, 0xCC, 0xCC, }, destination);
        foreach (ExecutionPath path in Paths)
        {
            EngineDifferential.AssertSame(EngineOperations.SerializeToSpan(layout, 6, "rec", value), true, path);
        }
    }

    /// <summary>
    ///     An aligned struct's tail padding is written as zeroes and charged, so the output's length is the struct's size
    ///     and a budget one byte short fails; padding between members is charged only as the extent it creates.
    /// </summary>
    [TestMethod]
    public void TailPadding_IsWrittenAndCharged()
    {
        var layout = new CStruct("struct rec { uint8 n; uint32 v[n]; uint8 t; };", aligned: true);
        var value = new Dictionary<string, object?> { ["n"] = (byte)1, ["v"] = new uint[] { 0x04030201, }, ["t"] = (byte)9, };
        WriteOptions required = EngineSelections.EngineRequired(new WriteOptions { MaxTotalBytesWritten = 12, });
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 1, 2, 3, 4, 9, 0, 0, 0, }, layout.Serialize("rec", value, options: required));
        CStructWriteLimitException failure = Assert.Throws<CStructWriteLimitException>(() => layout.Serialize("rec", value, options: required with { MaxTotalBytesWritten = 11, }));
        Assert.IsNull(failure.Member, "the tail belongs to no member");
        foreach (ExecutionPath path in Paths)
        {
            for (int limit = 1; limit <= 13; limit++)
            {
                EngineDifferential.AssertSame(EngineOperations.Serialize(layout, "rec", value, options: new WriteOptions { MaxTotalBytesWritten = limit, }), true, path);
                EngineDifferential.AssertSame(EngineOperations.SerializeToSpan(layout, limit, "rec", value), true, path);
            }
        }
    }

    /// <summary>A value supplied for a member of an unselected conditional arm is rejected, naming the member but not as the failing member.</summary>
    [TestMethod]
    public void InactiveConditionalMember_IsRejected()
    {
        var layout = new CStruct("struct rec { uint8 f; if (f == 1) { uint8 x; } else { uint16 y; } uint8 t; };");
        var value = new Dictionary<string, object?> { ["f"] = (byte)0, ["x"] = (byte)5, ["y"] = (ushort)6, ["t"] = (byte)1, };
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("rec", value, options: EngineSelections.EngineRequired(new WriteOptions())));
        StringAssert.StartsWith(failure.Message, WriteFailures.InactiveConditionalField("x"));
        Assert.IsNull(failure.Member);
        value.Remove("x");
        CollectionAssert.AreEqual(new byte[] { 0, 6, 0, 1, }, layout.Serialize("rec", value, options: EngineSelections.EngineRequired(new WriteOptions())));
        foreach (ExecutionPath path in Paths)
        {
            EngineDifferential.AssertSame(EngineOperations.Serialize(layout, "rec", value), true, path);
            EngineDifferential.AssertSame(EngineOperations.Serialize(layout, "rec", new Dictionary<string, object?>(value) { ["x"] = (byte)5, }), true, path);
        }
    }

    /// <summary>
    ///     A count is captured from the supplied value by the shared rule, converted as the codec converts it: numeric text
    ///     and a fraction rounded half to even name the same count the bytes hold.
    /// </summary>
    [TestMethod]
    public void Captures_ConvertTheSuppliedValue()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 items[n]; };");
        WriteOptions required = EngineSelections.EngineRequired(new WriteOptions());
        CollectionAssert.AreEqual(new byte[] { 2, 7, 8, }, layout.Serialize("rec", new Dictionary<string, object?> { ["n"] = "2", ["items"] = new byte[] { 7, 8, }, }, options: required));
        CollectionAssert.AreEqual(new byte[] { 2, 7, 8, }, layout.Serialize("rec", new Dictionary<string, object?> { ["n"] = 2.5, ["items"] = new byte[] { 7, 8, }, }, options: required));
        foreach (object count in new object[] { "2", 2.5, 1.5, "x", -1, 256, })
        {
            foreach (ExecutionPath path in Paths)
            {
                EngineDifferential.AssertSame(EngineOperations.Serialize(layout, "rec", new Dictionary<string, object?> { ["n"] = count, ["items"] = new byte[] { 7, 8, }, }), true, path);
            }
        }
    }

    /// <summary>
    ///     Mapped instances are bound where the interpreter binds them - at every struct the write enters, in a dictionary
    ///     root, in an array element and at the root itself - including under the reject policy, which checks each struct's
    ///     bound members; the root is not fixed, so the engine writes every case.
    /// </summary>
    [TestMethod]
    public void MappedInstances_AreBoundAtEveryStruct()
    {
        var layout = new CStruct("struct inner { uint8 a; }; struct rec { uint8 n; inner items[n]; inner nested; };");
        var mapped = new Dictionary<string, object?>
        {
            ["n"] = (byte)2,
            ["items"] = new object[] { new SharpEdgeOptionTests.InnerPoco { A = 6, }, new Dictionary<string, object?> { ["a"] = (byte)7, }, },
            ["nested"] = new SharpEdgeOptionTests.InnerPoco { A = 5, },
        };
        CollectionAssert.AreEqual(new byte[] { 2, 6, 7, 5, }, layout.Serialize("rec", mapped, options: EngineSelections.EngineRequired(new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject, })));
        var unknownInside = new Dictionary<string, object?>(mapped) { ["items"] = new object[] { new SharpEdgeOptionTests.InnerPoco { A = 6, }, new Dictionary<string, object?> { ["a"] = (byte)7, ["zz"] = 1, }, }, };
        foreach (ExecutionPath path in Paths)
        {
            foreach (WriteOptions options in new[] { new WriteOptions(), new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject, }, })
            {
                EngineDifferential.AssertSame(EngineOperations.Serialize(layout, "rec", mapped, options: options), true, path);
                EngineDifferential.AssertSame(EngineOperations.Serialize(layout, "rec", unknownInside, options: options), true, path);
                EngineDifferential.AssertSame(EngineOperations.Serialize(layout, "rec", new SharpEdgeOptionTests.InnerPoco { A = 1, }, options: options), true, path);
                EngineDifferential.AssertSame(EngineOperations.SerializeToSpan(layout, 3, "rec", mapped, options: options), true, path);
            }
        }
    }

    /// <summary>
    ///     An unsized wide-character array is written through its terminated view but placed by its declaration, so in an
    ///     aligned layout it lands where the reader reads it - at its element's alignment, even after an odd-length member -
    ///     whether its offset is known when the program is built or depends on the data, and an <c>@N</c> the layout
    ///     accepts holds for the write.
    /// </summary>
    [TestMethod]
    public void UnsizedWideText_IsPlacedAsTheReaderPlacesIt()
    {
        var known = new CStruct("struct rec { uint8 tag; wchar< wide[]; uint16 after; };", aligned: true);
        var knownValue = new Dictionary<string, object?> { ["tag"] = (byte)1, ["wide"] = "xy", ["after"] = (ushort)7, };
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0x78, 0, 0x79, 0, 0, 0, 7, 0, }, known.Serialize("rec", knownValue, options: EngineSelections.EngineRequired(new WriteOptions())));
        var dynamic = new CStruct("struct rec { uint8 tag; char name[]; wchar< wide[]; uint16 after; };", aligned: true);
        var dynamicValue = new Dictionary<string, object?> { ["tag"] = (byte)1, ["name"] = "a", ["wide"] = "xy", ["after"] = (ushort)7, };
        var asserted = new CStruct("struct rec { uint8 tag; wchar< wide[] @2; };", aligned: true);
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0x78, 0, 0, 0, }, asserted.Serialize("rec", new Dictionary<string, object?> { ["tag"] = (byte)1, ["wide"] = "x", }, options: EngineSelections.EngineRequired(new WriteOptions())));
        foreach (ExecutionPath path in Paths)
        {
            EngineDifferential.AssertSame(EngineOperations.Serialize(asserted, "rec", new Dictionary<string, object?> { ["tag"] = (byte)1, ["wide"] = "x", }), true, path);
            EngineDifferential.AssertSame(EngineOperations.Serialize(known, "rec", knownValue), true, path);
            EngineDifferential.AssertSame(EngineOperations.Serialize(dynamic, "rec", dynamicValue), true, path);
            EngineDifferential.AssertSame(EngineOperations.SerializeToSpan(dynamic, 9, "rec", dynamicValue), true, path);
        }
    }

    /// <summary>
    ///     The memory destination behaves as the interpreter's budget stream over a growing memory stream: a write past the
    ///     high-water mark zero-fills the gap, a read returns nothing past it, and the budget charges the larger of the bytes
    ///     written and the extent, before any byte moves.
    /// </summary>
    [TestMethod]
    public void MemoryBuffer_FillsGaps_AndChargesTheLargerOfTrafficAndExtent()
    {
        using var buffer = MemoryWriteBuffer.ForNewArray(new WriteOptions { MaxTotalBytesWritten = 6, });
        buffer.Position = 3;
        buffer.Write([7, 8]);
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, 7, 8, }, buffer.ToArray());
        buffer.Position = 0;
        byte[] read = new byte[8];
        Assert.AreEqual(5, buffer.Read(read));
        buffer.Position = 4;
        buffer.Write([9]);
        Assert.AreEqual(5L, buffer.Length, "rewriting inside the extent does not extend it");
        buffer.Position = 6;
        Assert.Throws<CStructWriteLimitException>(() => buffer.Write([1]), "the extent 7 exceeds the budget");
        Assert.AreEqual(5L, buffer.Length, "a rejected write changes nothing");
        buffer.Position = 0;
        buffer.Write([1, 2, 3]);
        Assert.Throws<CStructWriteLimitException>(() => buffer.Write([1]), "the traffic 7 exceeds the budget");

        // An empty write past the high-water mark still turns the gap into data, as both interpreter streams do.
        using var empty = MemoryWriteBuffer.ForNewArray(new WriteOptions());
        empty.Position = 2;
        empty.Write(ReadOnlySpan<byte>.Empty);
        Assert.AreEqual(2L, empty.Length);
    }

    /// <summary>
    ///     A span destination rejects a position outside it and a write past it without changing anything, and a zero fill
    ///     checks its budget first but its room chunk by chunk, so a long fill can stop after a whole chunk.
    /// </summary>
    [TestMethod]
    public unsafe void MemoryBuffer_OverASpan_FailsAsTheRegionStreamDoes()
    {
        byte[] storage = Enumerable.Repeat((byte)0xCC, 9000).ToArray();
        fixed (byte* region = storage)
        {
            using var buffer = MemoryWriteBuffer.ForSpan(region, 9000, new WriteOptions());
            CStructWriteException outside = Assert.Throws<CStructWriteException>(() => buffer.Position = 9001);
            Assert.AreEqual("The requested position is outside the supplied memory region.", outside.Message);
            buffer.Position = 8999;
            CStructWriteException full = Assert.Throws<CStructWriteException>(() => buffer.Write([1, 2]));
            StringAssert.StartsWith(full.Message, WriteFailures.DestinationCapacity);
            Assert.AreEqual(8999L, buffer.Position);
            Assert.AreEqual(0L, buffer.Length);

            buffer.Position = 0;
            Assert.Throws<CStructWriteException>(() => buffer.WriteZeroes(9001));
            Assert.AreEqual(8192L, buffer.Length, "the first chunk was written before the second found no room");
            Assert.AreEqual((byte)0, storage[8191]);
            Assert.AreEqual((byte)0xCC, storage[8192]);
        }
    }
}
