namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Values;

/// <summary>
///     The compiled write engine's step semantics where the sweeps and corpora do not reach, each compared with the
///     golden outcomes (<see cref="EngineGolden"/>) through the golden harness and pinned to its expected outcome: every codec at
///     every write budget and span capacity, every terminated text type, the prefix a failed write leaves in a span, the
///     narrow text path that fails after writing earlier characters, tail padding written and charged, the inactive conditional member check,
///     captures of converted supplied values, bitfield units, staged unions, pointers, the memory destination's own
///     rules (gaps, read-back, budget, chunked zero fill), bitfields merged into a caller's stream, members written on their
///     own through a nested path, and update semantics switched on by update options.
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
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.NoFastPaths];

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
    ///     to one past the output's length and every span capacity fail or succeed exactly as the golden outcomes record.
    /// </summary>
    [TestMethod]
    public void Codecs_WriteAtEveryBudgetAndCapacity()
    {
        var layout = new CStruct(CodecLayout);
        byte[] data = CodecData;
        StructValue value = layout.Parse(data.AsSpan(), "rec", options: new ReadOptions());
        CollectionAssert.AreEqual(data, layout.Serialize("rec", value, options: new WriteOptions()));
        foreach (ExecutionPath path in Paths)
        {
            for (int limit = 1; limit <= data.Length + 1; limit++)
            {
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value, options: new WriteOptions { MaxTotalBytesWritten = limit, }), path);
                EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, limit - 1, "rec", value), path);
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value, options: new WriteOptions { MaxStringBytes = limit - 1, }), path);
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
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize(destination.AsSpan(), "rec", value, options: new WriteOptions()));
        Assert.AreEqual("c", failure.Member);
        Assert.AreEqual(5L, failure.Offset);
        Assert.AreEqual("rec", failure.Path);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 1, 4, 0xCC, 0xCC, 0xCC, }, destination);
        Assert.Throws<CStructWriteException>(() => layout.Serialize("rec", value, options: new WriteOptions()));
        foreach (ExecutionPath path in Paths)
        {
            EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, 8, "rec", value), path);
            EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value), path);
        }
    }

    /// <summary>
    ///     Every terminated text type is encoded as the catalog's terminated-string writer encodes it: the text and its
    ///     terminator in the type's encoding and byte order, a terminator inside the value rejected, text the encoding cannot
    ///     represent rejected, a non-text value converted to invariant text, and the per-string limit counted with the
    ///     terminator.
    /// </summary>
    [TestMethod]
    public void TerminatedText_EncodesAsTheCatalogWriter()
    {
        string[] types =
        [
            "ascii_string_zero", "ascii_string_newline", "utf8_string_zero", "utf8_string_newline",
            "unicode_string_zero>", "unicode_string_zero<", "unicode_string_newline>", "unicode_string_newline<", "cstring",
        ];
        object[] values = ["abc", string.Empty, "a\0b", "a\nb", "é", "\uD800", 1.5, 42];
        foreach (string type in types)
        {
            var layout = new CStruct($"struct rec {{ uint8 n; uint8 items[n]; {type} s; uint8 tail; }};");
            foreach (object text in values)
            {
                var value = new Dictionary<string, object?> { ["n"] = (byte)0, ["items"] = Array.Empty<byte>(), ["s"] = text, ["tail"] = (byte)9, };
                foreach (ExecutionPath path in Paths)
                {
                    EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value), path);
                    for (int limit = 0; limit <= 8; limit++)
                    {
                        EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value, options: new WriteOptions { MaxStringBytes = limit, }), path);
                    }
                }
            }
        }

        var bigEndian = new CStruct("struct rec { uint8 n; uint8 items[n]; unicode_string_newline> s; };");
        var sample = new Dictionary<string, object?> { ["n"] = (byte)0, ["items"] = Array.Empty<byte>(), ["s"] = "hi", };
        CollectionAssert.AreEqual(new byte[] { 0, 0, (byte)'h', 0, (byte)'i', 0, (byte)'\n', }, bigEndian.Serialize("rec", sample, options: new WriteOptions()));
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
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize(destination.AsSpan(), "rec", value, options: new WriteOptions()));
        Assert.AreEqual("name", failure.Member);
        CollectionAssert.AreEqual(new byte[] { 0, (byte)'a', (byte)'b', 0xCC, 0xCC, 0xCC, }, destination);
        foreach (ExecutionPath path in Paths)
        {
            EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, 6, "rec", value), path);
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
        WriteOptions required = new WriteOptions { MaxTotalBytesWritten = 12, };
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 1, 2, 3, 4, 9, 0, 0, 0, }, layout.Serialize("rec", value, options: required));
        CStructWriteLimitException failure = Assert.Throws<CStructWriteLimitException>(() => layout.Serialize("rec", value, options: required with { MaxTotalBytesWritten = 11, }));
        Assert.IsNull(failure.Member, "the tail belongs to no member");
        foreach (ExecutionPath path in Paths)
        {
            for (int limit = 1; limit <= 13; limit++)
            {
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value, options: new WriteOptions { MaxTotalBytesWritten = limit, }), path);
                EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, limit, "rec", value), path);
            }
        }
    }

    /// <summary>A value supplied for a member of an unselected conditional arm is rejected, naming the member but not as the failing member.</summary>
    [TestMethod]
    public void InactiveConditionalMember_IsRejected()
    {
        var layout = new CStruct("struct rec { uint8 f; if (f == 1) { uint8 x; } else { uint16 y; } uint8 t; };");
        var value = new Dictionary<string, object?> { ["f"] = (byte)0, ["x"] = (byte)5, ["y"] = (ushort)6, ["t"] = (byte)1, };
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("rec", value, options: new WriteOptions()));
        StringAssert.StartsWith(failure.Message, WriteFailures.InactiveConditionalField("x"));
        Assert.IsNull(failure.Member);
        value.Remove("x");
        CollectionAssert.AreEqual(new byte[] { 0, 6, 0, 1, }, layout.Serialize("rec", value, options: new WriteOptions()));
        foreach (ExecutionPath path in Paths)
        {
            EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value), path);
            EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", new Dictionary<string, object?>(value) { ["x"] = (byte)5, }), path);
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
        WriteOptions required = new WriteOptions();
        CollectionAssert.AreEqual(new byte[] { 2, 7, 8, }, layout.Serialize("rec", new Dictionary<string, object?> { ["n"] = "2", ["items"] = new byte[] { 7, 8, }, }, options: required));
        CollectionAssert.AreEqual(new byte[] { 2, 7, 8, }, layout.Serialize("rec", new Dictionary<string, object?> { ["n"] = 2.5, ["items"] = new byte[] { 7, 8, }, }, options: required));
        foreach (object count in new object[] { "2", 2.5, 1.5, "x", -1, 256, })
        {
            foreach (ExecutionPath path in Paths)
            {
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", new Dictionary<string, object?> { ["n"] = count, ["items"] = new byte[] { 7, 8, }, }), path);
            }
        }
    }

    /// <summary>
    ///     Mapped instances are bound at every struct the write enters, in a dictionary
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
        CollectionAssert.AreEqual(new byte[] { 2, 6, 7, 5, }, layout.Serialize("rec", mapped, options: new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject, }));
        var unknownInside = new Dictionary<string, object?>(mapped) { ["items"] = new object[] { new SharpEdgeOptionTests.InnerPoco { A = 6, }, new Dictionary<string, object?> { ["a"] = (byte)7, ["zz"] = 1, }, }, };
        foreach (ExecutionPath path in Paths)
        {
            foreach (WriteOptions options in new[] { new WriteOptions(), new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject, }, })
            {
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", mapped, options: options), path);
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", unknownInside, options: options), path);
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", new SharpEdgeOptionTests.InnerPoco { A = 1, }, options: options), path);
                EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, 3, "rec", mapped, options: options), path);
            }
        }
    }

    /// <summary>
    ///     A bitfield merges into its storage unit by reading the unit back: in a new span the bytes past what was written
    ///     read as zero (not the span's old contents), and every bitfield rewrites - and is charged for - its whole unit, so
    ///     a budget that covers the output's extent but not the rewrites fails at the second field.
    /// </summary>
    [TestMethod]
    public void Bitfields_ReadTheUnitBackAsWritten_AndChargeEveryRewrite()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 v[n]; uint8 a : 3; uint8 b : 5; };");
        var value = new Dictionary<string, object?> { ["n"] = (byte)0, ["v"] = Array.Empty<byte>(), ["a"] = (byte)5, ["b"] = (byte)3, };
        byte[] span = Enumerable.Repeat(EngineOperations.Unwritten, 4).ToArray();
        Assert.AreEqual(2, layout.Serialize(span.AsSpan(), "rec", value, options: new WriteOptions()));
        CollectionAssert.AreEqual(new byte[] { 0, 0x1D, 0xCC, 0xCC, }, span);
        CStructWriteLimitException failure = Assert.Throws<CStructWriteLimitException>(() => layout.Serialize("rec", value, options: new WriteOptions { MaxTotalBytesWritten = 2, }));
        Assert.AreEqual("b", failure.Member);
        CollectionAssert.AreEqual(new byte[] { 0, 0x1D, }, layout.Serialize("rec", value, options: new WriteOptions { MaxTotalBytesWritten = 3, }));
        foreach (ExecutionPath path in Paths)
        {
            for (int limit = 1; limit <= 4; limit++)
            {
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value, options: new WriteOptions { MaxTotalBytesWritten = limit, }), path);
                EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, limit - 1, "rec", value), path);
            }
        }
    }

    /// <summary>
    ///     A bitfield written to a caller's stream merges into the bytes the stream already holds - bits no field covers keep
    ///     them - where a new array reads zero; the member a nested path selects opens its own unit at the stream's position;
    ///     and past the stream's end the unit reads zero, except under update options, which fail before writing.
    /// </summary>
    [TestMethod]
    public void StreamBitfields_MergeIntoTheStreamsBytes()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 v[n]; uint8 a : 3; uint8 b : 2; uint8 tail; };");
        var value = new Dictionary<string, object?> { ["n"] = (byte)0, ["v"] = Array.Empty<byte>(), ["a"] = (byte)5, ["b"] = (byte)1, ["tail"] = (byte)9, };
        WriteOptions required = new WriteOptions();
        CollectionAssert.AreEqual(new byte[] { 0, 0x0D, 9, }, layout.Serialize("rec", value, options: required));

        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xFF, 0xFF, 0xFF]);
        stream.Position = 0;
        layout.Write(stream, "rec", value, options: required);
        CollectionAssert.AreEqual(new byte[] { 0, 0xED, 9, 0xFF, }, stream.ToArray(), "bits 5 to 7 keep the stream's ones");
        Assert.AreEqual(3L, stream.Position);

        stream.Position = 3;
        layout.Write(stream, "rec.b", (byte)2, options: required);
        CollectionAssert.AreEqual(new byte[] { 0, 0xED, 9, 0xFE, }, stream.ToArray(), "a selected bitfield opens its own unit at the position");
        Assert.AreEqual(3L, stream.Position, "the unit is not full, so the position returns to its start");
        stream.Position = 4;
        layout.Write(stream, "rec.a", (byte)3, options: required);
        CollectionAssert.AreEqual(new byte[] { 0, 0xED, 9, 0xFE, 3, }, stream.ToArray(), "past the end the unit reads zero");
        stream.Position = 5;
        CStructReadException incomplete = Assert.Throws<CStructReadException>(() => layout.Write(stream, "rec.a", (byte)3, options: new UpdateOptions()));
        StringAssert.StartsWith(incomplete.Message, WriteFailures.IncompleteBitfieldUnit.TrimEnd('.'));
        Assert.AreEqual(5L, stream.Length, "nothing is written");

        byte[][] prefills = [[], [0xFF, 0xFF, 0xFF, 0xFF], [0x00, 0xA5, 0x5A], [0x81]];
        foreach (ExecutionPath path in Paths)
        {
            foreach (byte[] prefill in prefills)
            {
                for (long start = 0; start <= prefill.Length; start++)
                {
                    EngineDifferential.AssertGolden(EngineOperations.Write(layout, prefill, start, "rec", value), path);
                    EngineDifferential.AssertGolden(EngineOperations.Write(layout, prefill, start, "rec.b", (byte)3), path);
                    EngineDifferential.AssertGolden(EngineOperations.Write(layout, prefill, start, "rec.a", (byte)9), path);
                    EngineDifferential.AssertGolden(EngineOperations.Write(layout, prefill, start, "rec", value, options: new UpdateOptions()), path);
                    EngineDifferential.AssertGolden(EngineOperations.Write(layout, prefill, start, "rec.b", (byte)1, options: new UpdateOptions()), path);
                }
            }
        }
    }

    /// <summary>
    ///     Update options switch on update semantics for <c>Write</c> and <c>Serialize</c>: tail padding keeps the bytes it
    ///     holds (a new array ends before it), a union is staged over its existing bytes when
    ///     <see cref="UpdateOptions.ClearUnionStorage"/> is off (and fails before writing it when the stream does not hold the
    ///     whole union), and plain options still zero the tail and the union.
    /// </summary>
    [TestMethod]
    public void UpdateOptions_KeepPaddingAndUnionStorage()
    {
        var layout = new CStruct("union u { uint8 a; uint32 b; }; struct rec { uint8 n; uint8 v[n]; u x; uint16 w; uint8 t; };", aligned: true);
        var value = new Dictionary<string, object?> { ["n"] = (byte)1, ["v"] = new byte[] { 7, }, ["x"] = UnionValue.FromMember("u", "a", (byte)9), ["w"] = (ushort)0x0102, ["t"] = (byte)3, };
        byte[] prefill = Enumerable.Repeat((byte)0xEE, 14).ToArray();

        // Writes the value over a copy of the prefill with the given options and returns the stream's bytes.
        byte[] WriteOver(byte[] bytes, WriteOptions options)
        {
            using var stream = new MemoryStream();
            stream.Write(bytes);
            stream.Position = 0;
            layout.Write(stream, "rec", value, options: options);
            return stream.ToArray();
        }

        CollectionAssert.AreEqual(new byte[] { 1, 7, 0xEE, 0xEE, 9, 0, 0, 0, 2, 1, 3, 0, 0xEE, 0xEE, }, WriteOver(prefill, new WriteOptions()), "padding between members is skipped, the tail zeroed");
        CollectionAssert.AreEqual(new byte[] { 1, 7, 0xEE, 0xEE, 9, 0, 0, 0, 2, 1, 3, 0xEE, 0xEE, 0xEE, }, WriteOver(prefill, new UpdateOptions()), "the tail keeps its bytes");
        CollectionAssert.AreEqual(new byte[] { 1, 7, 0xEE, 0xEE, 9, 0xEE, 0xEE, 0xEE, 2, 1, 3, 0xEE, 0xEE, 0xEE, }, WriteOver(prefill, new UpdateOptions { ClearUnionStorage = false, }), "the union keeps its other bytes");
        CStructReadException incompleteUnion = Assert.Throws<CStructReadException>(() => WriteOver(prefill[..6], new UpdateOptions { ClearUnionStorage = false, }));
        StringAssert.StartsWith(incompleteUnion.Message, WriteFailures.IncompleteUnionStorage.TrimEnd('.'));
        Assert.IsInstanceOfType<EndOfStreamException>(incompleteUnion.InnerException);
        CollectionAssert.AreEqual(new byte[] { 1, 7, 0, 0, 9, 0, 0, 0, 2, 1, 3, }, layout.Serialize("rec", value, options: new UpdateOptions()), "a new array ends before the skipped tail");

        foreach (ExecutionPath path in Paths)
        {
            foreach (UpdateOptions options in new[] { new UpdateOptions(), new UpdateOptions { ClearUnionStorage = false, }, })
            {
                foreach (int length in (int[])[0, 6, 8, 11, 14])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Write(layout, prefill[..length], 0, "rec", value, options: options), path);
                    EngineDifferential.AssertGolden(EngineOperations.Write(layout, prefill[..length], 0, "rec.x", value["x"]!, options: options), path);
                }

                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value, options: options), path);
                EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, 12, "rec", value, options: options), path);
                EngineDifferential.AssertGolden(EngineOperations.SerializeToWindows(layout, 1, "rec", value, options: options), path);
            }
        }
    }

    /// <summary>
    ///     A union's selected member is staged away from the destination: a member that cannot be written leaves the
    ///     union's bytes unwritten (a span keeps what preceded it), the staging has a budget of its own so the union is
    ///     charged once for its whole storage, and raw storage of the wrong size is rejected before anything is written.
    /// </summary>
    [TestMethod]
    public void Unions_AreStagedAndChargedOnce()
    {
        var layout = new CStruct("union u { uint8 a; uint32 b; }; struct rec { uint8 n; uint8 v[n]; u x; };");

        // Builds the record's value around one union selection.
        Dictionary<string, object?> Value(UnionValue union) => new() { ["n"] = (byte)1, ["v"] = new byte[] { 7, }, ["x"] = union, };
        WriteOptions required = new WriteOptions();
        CollectionAssert.AreEqual(new byte[] { 1, 7, 9, 0, 0, 0, }, layout.Serialize("rec", Value(UnionValue.FromMember("u", "a", (byte)9)), options: required));
        CollectionAssert.AreEqual(new byte[] { 1, 7, 9, 0, 0, 0, }, layout.Serialize("rec", Value(UnionValue.FromMember("u", "a", (byte)9)), options: required with { MaxTotalBytesWritten = 6, }));

        byte[] span = Enumerable.Repeat(EngineOperations.Unwritten, 7).ToArray();
        Assert.Throws<CStructWriteException>(() => layout.Serialize(span.AsSpan(), "rec", Value(UnionValue.FromMember("u", "b", "x")), options: required));
        CollectionAssert.AreEqual(new byte[] { 1, 7, 0xCC, 0xCC, 0xCC, 0xCC, 0xCC, }, span, "nothing of the union is written");

        UnionValue[] unions = [UnionValue.FromMember("u", "a", (byte)9), UnionValue.FromMember("u", "b", 0x01020304u), UnionValue.FromMember("u", "b", "x"), UnionValue.FromMember("u", "zz", 1), UnionValue.FromRaw("u", [1, 2]), UnionValue.FromRaw("u", [1, 2, 3, 4]), UnionValue.FromMember("v", "a", (byte)1)];
        foreach (ExecutionPath path in Paths)
        {
            foreach (UnionValue union in unions)
            {
                for (int limit = 4; limit <= 7; limit++)
                {
                    EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", Value(union), options: new WriteOptions { MaxTotalBytesWritten = limit, }), path);
                    EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, limit, "rec", Value(union)), path);
                }
            }
        }
    }

    /// <summary>
    ///     An anonymous promoted union writes the widest member its parent's value supplies, staged like a named union's
    ///     selection, and fails naming the union's members when none is supplied.
    /// </summary>
    [TestMethod]
    public void PromotedUnion_WritesTheWidestSuppliedMember()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 v[n]; union { uint8 small; uint16 wide; struct { uint8 lo; uint8 hi; }; }; };");
        WriteOptions required = new WriteOptions();
        var both = new Dictionary<string, object?> { ["n"] = (byte)0, ["v"] = Array.Empty<byte>(), ["small"] = (byte)1, ["wide"] = (ushort)0x0302, };
        CollectionAssert.AreEqual(new byte[] { 0, 2, 3, }, layout.Serialize("rec", both, options: required));
        var parts = new Dictionary<string, object?> { ["n"] = (byte)0, ["v"] = Array.Empty<byte>(), ["hi"] = (byte)6, };
        var none = new Dictionary<string, object?> { ["n"] = (byte)0, ["v"] = Array.Empty<byte>(), };
        StringAssert.StartsWith(Assert.Throws<CStructWriteException>(() => layout.Serialize("rec", none, options: required)).Message, "No member of the anonymous union was supplied");
        foreach (ExecutionPath path in Paths)
        {
            foreach (Dictionary<string, object?> value in new[] { both, parts, none, })
            {
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value), path);
                EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, 2, "rec", value), path);
            }
        }
    }

    /// <summary>
    ///     A pointer writes only its stored address, encoded by the operation's addressing mode and origin, and a scalar
    ///     pointer may be null (the null address) while a pointer array may not.
    /// </summary>
    [TestMethod]
    public void Pointers_WriteTheirEncodedAddress()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 v[n]; uint8 *p; uint8 *q[2]; };", 2);
        var value = new Dictionary<string, object?> { ["n"] = (byte)0, ["v"] = Array.Empty<byte>(), ["p"] = 10L, ["q"] = new object?[] { null, 12L, }, };
        CollectionAssert.AreEqual(new byte[] { 0, 10, 0, 0, 0, 12, 0, }, layout.Serialize("rec", value, options: new WriteOptions()));
        CollectionAssert.AreEqual(new byte[] { 0, 6, 0, 0, 0, 8, 0, }, layout.Serialize("rec", value, options: new WriteOptions { AddressingMode = PointerAddressingMode.Relative, Origin = 4, }));
        foreach (ExecutionPath path in Paths)
        {
            foreach (object? pointer in new object?[] { null, 10L, -1L, 70000L, "x", })
            {
                foreach (WriteOptions options in new[] { new WriteOptions(), new WriteOptions { AddressingMode = PointerAddressingMode.Relative, Origin = 11, }, })
                {
                    EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", new Dictionary<string, object?>(value) { ["p"] = pointer, }, options: options), path);
                    EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", new Dictionary<string, object?>(value) { ["q"] = pointer, }, options: options), path);
                }
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
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0x78, 0, 0x79, 0, 0, 0, 7, 0, }, known.Serialize("rec", knownValue, options: new WriteOptions()));
        var dynamic = new CStruct("struct rec { uint8 tag; char name[]; wchar< wide[]; uint16 after; };", aligned: true);
        var dynamicValue = new Dictionary<string, object?> { ["tag"] = (byte)1, ["name"] = "a", ["wide"] = "xy", ["after"] = (ushort)7, };
        var asserted = new CStruct("struct rec { uint8 tag; wchar< wide[] @2; };", aligned: true);
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0x78, 0, 0, 0, }, asserted.Serialize("rec", new Dictionary<string, object?> { ["tag"] = (byte)1, ["wide"] = "x", }, options: new WriteOptions()));
        foreach (ExecutionPath path in Paths)
        {
            EngineDifferential.AssertGolden(EngineOperations.Serialize(asserted, "rec", new Dictionary<string, object?> { ["tag"] = (byte)1, ["wide"] = "x", }), path);
            EngineDifferential.AssertGolden(EngineOperations.Serialize(known, "rec", knownValue), path);
            EngineDifferential.AssertGolden(EngineOperations.Serialize(dynamic, "rec", dynamicValue), path);
            EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(dynamic, 9, "rec", dynamicValue), path);
        }
    }

    /// <summary>
    ///     The memory destination behaves as a budget stream over a growing memory stream: a write past the
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

        // An empty write past the high-water mark turns the gap into data too, as a budget stream over a memory stream does.
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
