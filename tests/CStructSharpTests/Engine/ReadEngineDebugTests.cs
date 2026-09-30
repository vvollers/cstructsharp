namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The compiled engine's debug parse, pinned on one layout that holds every recording rule: a record per value with its
///     range, never the bytes (except a union's), unnamed padding under <c>_</c>, an anonymous bitfield under an empty
///     segment, bitfields over their whole storage unit, an enum's number, a record per character of a <c>char[N]</c>, one
///     record for byte-counted text, element paths of a struct array, a union's own record after its views, and a deferred
///     pointer's target after its struct's last member. Each case also runs through the golden harness, so the
///     golden outcomes (<see cref="EngineGolden"/>) pin exactly these records.
/// </summary>
[TestClass]
public class ReadEngineDebugTests
{
    /// <summary>A packed layout with one-byte pointers that holds every recording rule.</summary>
    private const string Layout = """
        enum color : uint8 { red = 1, green = 2 };
        union u { uint16 w; uint8 b[2]; };
        struct node { uint8 v; };
        struct rec {
          uint8 n; uint8 _[1]; uint8 lo : 3; uint8 : 2; uint8 hi : 3; color c; char name[3]; utf8 word[4];
          node items[2]; u value; node *p; uint8 tail;
        };
        """;

    /// <summary>Input for <see cref="Layout"/>: the pointer holds 17, where the target node follows the tail.</summary>
    private static readonly byte[] Data = [2, 0xAA, 0xC5, 2, (byte)'a', (byte)'b', 0, (byte)'h', (byte)'i', 0, 0, 7, 8, 0x34, 0x12, 17, 9, 0x55];

    /// <summary>
    ///     The engine records every value in read order with its range, path, type spelling and value:
    ///     the pointer's own record at its address and its target's after the struct's last member.
    /// </summary>
    [TestMethod]
    public void DebugParse_RecordsEveryValueInReadOrder()
    {
        var layout = new CStruct(Layout, 1);
        (StructValue value, IReadOnlyList<DebugData> debug) = layout.ParseWithDebug(Data, "rec", options: new ReadOptions());

        string[] expected =
        [
            "rec.n [0, 1) uint8 = 2",
            "rec._ [1, 2) uint8 = 170",
            "rec.lo [2, 3) uint8 = 5",
            "rec. [2, 3) uint8 = 0",
            "rec.hi [2, 3) uint8 = 6",
            "rec.c [3, 4) color = 2",
            "rec.name [4, 5) char = a",
            "rec.name [5, 6) char = b",
            "rec.name [6, 7) char = \0",
            "rec.word [7, 11) utf8 = hi\0\0",
            "rec.items[0].v [11, 12) uint8 = 7",
            "rec.items[1].v [12, 13) uint8 = 8",
            "rec.value.w [13, 15) uint16 = 4660",
            "rec.value.b [13, 14) uint8 = 52",
            "rec.value.b [14, 15) uint8 = 18",
            "rec.value [13, 15) u = " + value["value"],
            "rec.p [15, 16) node = " + value["p"],
            "rec.tail [16, 17) uint8 = 9",
            "rec.p.v [17, 18) uint8 = 85",
        ];
        string[] actual = debug.Select(record => record.ToString()).ToArray();
        Assert.AreEqual(string.Join("\n", expected), string.Join("\n", actual));
        Assert.IsInstanceOfType<System.Numerics.BigInteger>(debug[5].Value);
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, debug[15].Bytes.ToArray(), "a union's record carries its storage");
        Assert.IsTrue(debug.Where((_, index) => index != 15).All(record => record.Bytes.IsEmpty), "every other record holds its range only");
        Assert.AreEqual((byte)0x55, ((StructValue)((Pointer)value["p"]!).Value!)["v"]);

        foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Sequence, EngineInput.ExposedStream, EngineInput.ChunkedStream1])
        {
            ReadOptions read = input == EngineInput.ExposedStream ? new ReadOptions { AddressingMode = PointerAddressingMode.Relative, Origin = EngineStreams.ExposedStart, } : new ReadOptions();
            EngineDifferential.AssertGolden(EngineOperations.ParseWithDebug(layout, Data, input, "rec", options: read));
            EngineDifferential.AssertGolden(EngineOperations.ReadValueWithDebug(layout, Data, input, "rec", options: read));
        }
    }

    /// <summary>
    ///     A debug parse returns no records when it fails, and the failure names the member, path and offset the
    ///     golden outcomes record, from every truncation of the input.
    /// </summary>
    [TestMethod]
    public void DebugParse_FailsWithoutRecordsAtEveryTruncation()
    {
        var layout = new CStruct(Layout, 1);
        for (int length = 0; length < Data.Length; length++)
        {
            EngineDifferential.AssertGolden(EngineOperations.ParseWithDebug(layout, Data[..length], EngineInput.Span, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValueWithDebug(layout, Data[..length], EngineInput.ChunkedStream3, "rec"));
        }

        Assert.Throws<CStructReadException>(() => layout.ParseWithDebug(Data[..16], "rec", options: new ReadOptions()));
    }
}
