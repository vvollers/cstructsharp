namespace CStructSharp.Tests;

/// <summary>
///     The compiled read engine's shared storage: a bitfield's storage unit and a union's raw storage are read again for
///     every bitfield and member view, and charged each time, while the position and the variables return to the
///     storage's start. Each case checks its outcomes against the golden outcomes (<see cref="EngineGolden"/>) under both
///     execution paths, and pins the expected outcome.
/// </summary>
[TestClass]
public class ReadEngineSharedStorageTests
{
    /// <summary>
    ///     Every bitfield reads its whole storage unit again, and is charged for it: three
    ///     bitfields sharing a two-byte unit cost 6 bytes, the tail 1 more. While bits of a unit remain, the position is
    ///     back at the unit's start, so a later member that cannot be placed fails from there. A packed window whose
    ///     placed unit differs from its declared type, the MSVC and high-bit-first rules, and values without sign extension
    ///     (an <see cref="int"/> below 32 bits, a <see cref="ulong"/> from 32 on) read as the golden outcomes record, from every source.
    /// </summary>
    [TestMethod]
    public void BitfieldUnits_AreReadAgainForEveryBitfield()
    {
        var layout = new CStruct("struct rec { uint16 a : 4; uint16 b : 4; uint16 c : 8; uint8 tail; };");
        byte[] data = [0x21, 0x43, 0x09];
        var aligned = new CStruct("struct rec { uint8 a : 4; uint32 b; };", aligned: true);
        var window = new CStruct("struct rec { uint8 a : 7; uint16 b : 9; uint8 tail; };");
        var signs = new CStruct("struct rec { int8 s : 4; uint64 big : 40; };");
        var options = new CStructCompilationOptions { BitfieldPacking = BitfieldPacking.Msvc, BitfieldAllocation = BitfieldAllocation.HighBitFirst, };
        var msvc = new CStruct("struct rec { uint8 a : 3; uint16 b : 5; uint16 c : 9; uint8 tail; };", compilationOptions: options);
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            for (long budget = 1; budget <= 8; budget++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    string outcome = EngineDifferential.AssertGolden(
                        EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        path: path);
                    StringAssert.Contains(outcome, budget < 7 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.b = Int32 2\n");
            StringAssert.Contains(complete, "result.c = Int32 67\n");

            // a leaves bits of its unit, so the position is back at 0 when b's aligned start (4) lies past the input.
            string rewound = EngineDifferential.AssertGolden(EngineOperations.Parse(aligned, [0x05], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(rewound, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(rewound, "position = 0\n");

            string unsigned = EngineDifferential.AssertGolden(EngineOperations.Parse(signs, [0xFF, 0x01, 0x02, 0x03, 0x04, 0xFF, 0, 0, 0], EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(unsigned, "result.s = Int32 15\n");
            StringAssert.Contains(unsigned, "result.big = UInt64 ");
            foreach ((CStruct subject, byte[] bytes) in ((CStruct, byte[])[])[(layout, data), (window, [0xFF, 0x81, 0x02, 0x09]), (msvc, [0x05, 0x34, 0x12, 0x09]), (signs, [0xFF, 0x01, 0x02, 0x03, 0x04, 0xFF, 0, 0, 0])])
            {
                for (int length = 0; length <= bytes.Length; length++)
                {
                    foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                    {
                        EngineDifferential.AssertGolden(EngineOperations.Parse(subject, bytes[..length], input, "rec"), path: path);
                    }
                }

                for (long budget = 1; budget <= (bytes.Length * 3) + 1; budget++)
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(subject, bytes, EngineInput.Span, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }), path: path);
                }
            }
        }
    }

    /// <summary>
    ///     A union charges its raw storage and then each member view (3 + 1 + 2 + 3 bytes here); every member starts at the
    ///     union's first byte with the variables it was entered with, and nothing a member captures is visible after the
    ///     union, so the struct's own <c>n</c> sizes the later array. A member that fails leaves the position at the union's
    ///     end, a nesting limit at its start; a promoted union claims no nesting level; numeric array views are typed arrays.
    /// </summary>
    [TestMethod]
    public void Unions_ChargeStorageAndViews_AndRestoreTheirEntryState()
    {
        var layout = new CStruct("union u { uint8 n; uint16 w; uint8 bytes[3]; }; struct rec { uint8 n; u value; uint8 items[n]; uint8 tail; };");
        byte[] data = [2, 7, 0, 0, 5, 6, 9];
        var invalid = new CStruct("union v { uint8 raw[4]; wchar< s[2]; }; struct rec { uint8 tag; v value; uint8 tail; };");
        var promoted = new CStruct("struct rec { uint8 a; union { uint8 x; uint16 y; }; uint8 tail; };");
        var hidden = new CStruct("union w { uint8 m; }; struct rec { w value; uint8 items[m]; };");
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.items = PrimitiveArray<Byte> [2]\n");
            StringAssert.Contains(complete, "PrimitiveArray<Byte> [3]\n");
            for (long budget = 1; budget <= 14; budget++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    string outcome = EngineDifferential.AssertGolden(
                        EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        path: path);
                    StringAssert.Contains(outcome, budget < 13 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream3])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                }
            }

            string failed = EngineDifferential.AssertGolden(EngineOperations.Parse(invalid, [1, 0x00, 0xD8, 0x41, 0x00, 9], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(failed, "failure.member = \"s\"\n");
            StringAssert.Contains(failed, "position = 5\n");

            string nested = EngineDifferential.AssertGolden(
                EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: new ReadOptions { MaxNestingDepth = 1, }),
                path: path);
            StringAssert.Contains(nested, "CStructReadLimitException");
            StringAssert.Contains(nested, "position = 1\n");
            string flat = EngineDifferential.AssertGolden(
                EngineOperations.Parse(promoted, [1, 2, 3, 9], EngineInput.Span, "rec", options: new ReadOptions { MaxNestingDepth = 1, }),
                path: path);
            StringAssert.Contains(flat, "result.tail = Byte 9\n");
            string invisible = EngineDifferential.AssertGolden(EngineOperations.Parse(hidden, [1, 5], EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(invisible, "Undefined expression identifier: m");
        }
    }
}
