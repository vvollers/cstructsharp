namespace CStructSharp.Tests;

/// <summary>
///     The compiled read engine's arrays where the sweeps and corpora do not reach: terminated arrays (their scan and
///     re-read are both charged), <c>[EOF]</c> arrays, multidimensional arrays read flat and then nested, and large arrays
///     read in 64 KiB blocks. Each case checks its outcomes against the golden outcomes (<see cref="EngineGolden"/>) under
///     both execution paths, and pins the expected outcome.
/// </summary>
[TestClass]
public class ReadEngineArrayTests
{
    /// <summary>
    ///     A terminated array is scanned for its all-zero element from its start, then read: the scan's bytes are charged
    ///     and the elements again when they are read, so the whole read of three elements costs
    ///     tag 1 + scan 8 + re-read 6 + tail 1 = 16 bytes of budget. Input that ends before the terminator, and more
    ///     elements than the limit allows, fail at the array's start. Arrays of scalars and of structs (through the element
    ///     struct's block path) agree at every budget, truncation and source.
    /// </summary>
    [TestMethod]
    public void TerminatedArrays_ChargeTheScanAndTheReread()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint16 values[]; uint8 tail; };");
        var structs = new CStruct("struct e { uint8 a; uint8 b; }; struct rec { e items[]; uint8 tail; };");
        byte[] data = [7, 1, 0, 2, 0, 3, 0, 0, 0, 9];
        byte[] entries = [1, 2, 3, 4, 0, 0, 9];
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            for (long budget = 1; budget <= 17; budget++)
            {
                var read = new ReadOptions { MaxTotalBytesRead = budget, };
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1, EngineInput.ChunkedStream3])
                {
                    string outcome = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec", options: read), path: path);
                    string expected = budget < 16 ? "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n" : "result.tail = Byte 9\n";
                    StringAssert.Contains(outcome, expected, "budget " + budget + " from " + input);
                    EngineDifferential.AssertGolden(EngineOperations.Parse(structs, entries, input, "rec", options: read), path: path);
                }
            }

            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.ChunkedStream1])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                    EngineDifferential.AssertGolden(EngineOperations.Parse(structs, entries[..Math.Min(length, entries.Length)], input, "rec"), path: path);
                }
            }

            string unterminated = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..5], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(unterminated, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(unterminated, "position = 1\n");
            string limited = EngineDifferential.AssertGolden(
                EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: new ReadOptions { MaxArrayElements = 2, }),
                path: path);
            StringAssert.Contains(limited, "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n");
            StringAssert.Contains(limited, "position = 1\n");
        }
    }

    /// <summary>
    ///     An <c>[EOF]</c> array is counted from its placed start to the end of the input without reading: a trailing
    ///     partial element and a start that alignment moves past the end fail there, no remaining bytes is an empty typed
    ///     array, the element limit applies, and a <c>char[EOF]</c> is trimmed text - as the golden outcomes record, from every source.
    /// </summary>
    [TestMethod]
    public void ToEndArrays_CountWholeElementsFromTheirStart()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint16 values[EOF]; };");
        var aligned = new CStruct("struct rec { uint8 tag; uint32 values[EOF]; };", aligned: true);
        var text = new CStruct("struct rec { uint8 tag; char text[EOF]; };");
        byte[] data = [7, 1, 0, 2, 0, 3, 0];
        byte[] words = [7, 0, 0, 0, 1, 0, 0, 0];
        byte[] letters = [7, (byte)'h', (byte)'i', 0, 0];
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            foreach ((CStruct subject, byte[] bytes) in ((CStruct, byte[])[])[(layout, data), (aligned, words), (text, letters)])
            {
                for (int length = 0; length <= bytes.Length; length++)
                {
                    foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                    {
                        foreach (bool trim in (bool[])[true, false])
                        {
                            EngineDifferential.AssertGolden(EngineOperations.Parse(subject, bytes[..length], input, "rec", options: new ReadOptions { TrimFixedText = trim, }), path: path);
                        }
                    }
                }
            }

            string remainder = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..6], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(remainder, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(remainder, "position = 1\n");
            string past = EngineDifferential.AssertGolden(EngineOperations.Parse(aligned, words[..1], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(past, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            string empty = EngineDifferential.AssertGolden(EngineOperations.Parse(aligned, words[..4], EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(empty, "result.values = PrimitiveArray<UInt32> [0]\n");
            string limited = EngineDifferential.AssertGolden(
                EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: new ReadOptions { MaxArrayElements = 2, }),
                path: path);
            StringAssert.Contains(limited, "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n");
            string trimmed = EngineDifferential.AssertGolden(
                EngineOperations.Parse(text, letters, EngineInput.Stream, "rec", options: new ReadOptions { TrimFixedText = true, }),
                path: path);
            StringAssert.Contains(trimmed, "result.text = String \"hi\"\n");
        }
    }

    /// <summary>
    ///     A multidimensional array reads all its elements in row-major order under one element limit and is then nested:
    ///     lists at every level (never typed arrays), <c>wchar</c> rows as strings validated row by row after every
    ///     character was read, and three dimensions. Typedef roots read standalone; their selected reads check the outermost
    ///     count against the element limit first, as the path resolver does, and then the read checks the total.
    /// </summary>
    [TestMethod]
    public void MultidimensionalArrays_ReadFlatThenNest()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint16 grid[2][3]; wchar< names[2][2]; uint8 cube[2][1][2]; uint8 tail; };");
        byte[] data = [7, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, (byte)'a', 0, 0, 0, (byte)'b', 0, (byte)'c', 0, 1, 2, 3, 4, 9];
        var roots = new CStruct("typedef uint8 table[2][3]; typedef char rows[2][3];");
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.grid = List<Object> [2]\n");
            StringAssert.Contains(complete, "result.grid[1] = List<Object> [3]\n");
            StringAssert.Contains(complete, "result.names[1] = String \"bc\"\n");
            StringAssert.Contains(complete, "result.cube[1][0] = List<Object> [2]\n");
            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                }
            }

            for (long budget = 1; budget <= data.Length + 1; budget++)
            {
                var read = new ReadOptions { MaxTotalBytesRead = budget, };
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.ChunkedStream3, "rec", options: read), path: path);
            }

            // The limit applies to all 6 + 4 + 4 elements of a table, not its outermost count.
            foreach (int limit in (int[])[1, 2, 3, 4, 5, 6])
            {
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: new ReadOptions { MaxArrayElements = limit, }), path: path);
            }

            // A lone surrogate in the second row fails once every character was read, after the names.
            byte[] invalid = (byte[])data.Clone();
            invalid[17] = 0x00;
            invalid[18] = 0xD8;
            string surrogate = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, invalid, EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(surrogate, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(surrogate, "position = 21\n");

            foreach (string root in (string[])["table", "rows"])
            {
                for (int length = 0; length <= 6; length++)
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(roots, data[1..(1 + length)], EngineInput.Span, root), path: path);
                }

                // The outermost count (2) passes a limit of 2 before the read's total (6) fails it.
                foreach (int limit in (int[])[1, 2, 5, 6])
                {
                    var limited = new ReadOptions { MaxArrayElements = limit, };
                    EngineDifferential.AssertGolden(EngineOperations.ReadValue(roots, data[1..7], EngineInput.Span, root, options: limited), path: path);
                }

                EngineDifferential.AssertGolden(EngineOperations.ReadValue(roots, data[1..7], EngineInput.Span, root), path: path);
            }
        }
    }

    /// <summary>
    ///     Large data-sized and multidimensional arrays read in blocks of 64 KiB: a byte budget or
    ///     an input that ends inside the first or the second block fails at that block, cancellation is observed only
    ///     before a block, and a terminated array charges its whole scan before its blocks.
    /// </summary>
    [TestMethod]
    public void LargeArrays_ReadAndFailIn64KiBBlocks()
    {
        var table = new CStruct("struct rec { uint8 tag; uint8 big[300][300]; uint8 tail; };");
        var terminated = new CStruct("struct rec { uint8 tag; uint8 values[]; uint8 tail; };");
        byte[] big = [7, .. Enumerable.Range(0, 90000).Select(index => (byte)((index % 251) + 1)), 9];
        byte[] list = [7, .. Enumerable.Range(0, 70000).Select(index => (byte)((index % 251) + 1)), 0, 9];
        const long Scanned = 1 + 70001;
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream])
            {
                foreach (long budget in (long[])[65536, 65537, 65538, 90000, 90001, 90002])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(table, big, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }), path: path);
                }

                foreach (int length in (int[])[65536, 65537, 65538, 90000, 90001])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(table, big[..length], input, "rec"), path: path);
                }

                foreach (long budget in (long[])[Scanned, Scanned + 1, Scanned + 65536, Scanned + 65537, Scanned + 70000, Scanned + 70001, Scanned + 70002])
                {
                    string outcome = EngineDifferential.AssertGolden(
                        EngineOperations.Parse(terminated, list, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        path: path);
                    StringAssert.Contains(outcome, budget < Scanned + 70001 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            // Cancelled inside the first block, the check before the second block ends the read; inside the second, the
            // read completes, because no block, struct entry or string chunk follows.
            string first = EngineDifferential.AssertGolden(EngineOperations.CancelledParse(table, big, 10), path: path);
            StringAssert.Contains(first, "failure = failure System.OperationCanceledException\n");
            string second = EngineDifferential.AssertGolden(EngineOperations.CancelledParse(table, big, 70000), path: path);
            StringAssert.Contains(second, "result.tail = Byte 9\n");
        }
    }
}
