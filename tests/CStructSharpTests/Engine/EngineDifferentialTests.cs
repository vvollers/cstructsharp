namespace CStructSharp.Tests;

using System.Collections.Generic;
using CStructSharp.Values;

/// <summary>
///     Runs the golden harness over a representative set of layouts and operations: every operation must render
///     its golden outcome (<see cref="EngineGolden"/>) on the compiled engine. The harness itself must report a planted
///     difference as a readable diff.
/// </summary>
[TestClass]
public class EngineDifferentialTests
{
    /// <summary>A fixed record: every member at a fixed offset, including an enum and fixed text.</summary>
    private const string FixedLayout = """
        enum kind : uint8 { small = 1, large = 2 };
        struct rec { uint16 id; int32 value; kind which; char tag[4]; };
        """;

    /// <summary>Two fixed records; the second is followed by one byte of a truncated third.</summary>
    private static readonly byte[] FixedRecords = [7, 0, 0xFE, 0xFF, 0xFF, 0xFF, 2, (byte)'a', (byte)'b', 0, 0, 8, 0, 1, 0, 0, 0, 1, (byte)'x', (byte)'y', (byte)'z', (byte)'w'];

    /// <summary>The read input forms every read case runs over.</summary>
    private static readonly EngineInput[] ReadInputs = [EngineInput.Span, EngineInput.ByteArray, EngineInput.Memory, EngineInput.Stream, EngineInput.Sequence];

    /// <summary>A fixed record reads its golden outcome through every operation, input form, and execution path.</summary>
    [TestMethod]
    public void FixedRecord_ReadsThroughEveryOperationInputAndPath()
    {
        var layout = new CStruct(FixedLayout);
        byte[] one = FixedRecords[..11];
        foreach (ExecutionPath path in Enum.GetValues<ExecutionPath>())
        {
            foreach (EngineInput input in ReadInputs)
            {
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, one, input, "rec"), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, one[..5], input, "rec"), path: path);
                EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, one, input, "rec"), path: path);
                EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, one, input, "rec.which"), path: path);
                EngineDifferential.AssertGolden(EngineOperations.ReadValue<int>(layout, one, input, "rec.value"), path: path);
                EngineDifferential.AssertGolden(EngineOperations.ReadValue<StructValue>(layout, one, input, "rec"), path: path);
                EngineDifferential.AssertGolden(EngineOperations.ReadValue<byte>(layout, one, input, "rec.value"), path: path);
            }

            EngineDifferential.AssertGolden(EngineOperations.ParseAsync(layout, one, "rec"), path: path);
            EngineDifferential.AssertGolden(EngineOperations.ParseAsync(layout, one[..4], "rec"), path: path);
        }

        EngineOutcome outcome = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, one, EngineInput.Stream, "rec"));
        Assert.AreEqual(1, outcome.EngineRuns);
        StringAssert.Contains(outcome.Rendering, "result.id = UInt16 7\n");
        StringAssert.Contains(outcome.Rendering, "result.value = Int32 -2\n");
        StringAssert.Contains(outcome.Rendering, "result.which = EnumValueResult kind name=large value=2");
        StringAssert.Contains(outcome.Rendering, "result.tag = String \"ab\\u0000\\u0000\"\n");
        StringAssert.Contains(outcome.Rendering, "position = 11\n");
    }

    /// <summary>Record sequences read their golden outcomes over memory, a multi-segment sequence, and a stream, including a truncated last record.</summary>
    [TestMethod]
    public void RecordSequences_ReadFromMemorySequencesAndStreams()
    {
        var fixedLayout = new CStruct(FixedLayout);
        var sizedLayout = new CStruct("struct rec { uint8 n; uint8 items[n]; };");
        foreach (EngineInput input in (EngineInput[])[EngineInput.Memory, EngineInput.Sequence, EngineInput.Stream])
        {
            EngineDifferential.AssertGolden(EngineOperations.ParseMany(fixedLayout, FixedRecords[..22], input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.ParseMany(fixedLayout, FixedRecords, input, "rec"));
            EngineOutcome outcome = EngineDifferential.AssertGolden(EngineOperations.ParseMany(sizedLayout, [2, 1, 2, 0, 3, 9], input, "rec"));
            StringAssert.Contains(outcome.Rendering, "count = 2\n");
            StringAssert.Contains(outcome.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
        }
    }

    /// <summary>A count-sized array reads, resolves, and reports its length as the golden outcomes record, and fails as they record when truncated.</summary>
    [TestMethod]
    public void CountSizedArray_ReadsResolvesAndMeasures()
    {
        var layout = new CStruct("struct rec { uint8 n; uint16 items[n]; uint8 tail; };");
        byte[] data = [2, 1, 0, 2, 0, 9];
        foreach (EngineInput input in ReadInputs)
        {
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..4], input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxArrayElements = 1 }));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, input, "rec.items"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, input, "rec.items[1]"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue<ushort[]>(layout, data, input, "rec.items"));
            EngineDifferential.AssertGolden(EngineOperations.ResolveAddress(layout, data, input, "rec.tail"));
            EngineDifferential.AssertGolden(EngineOperations.ResolveAddress(layout, data, input, "rec.items[3]"));
            EngineDifferential.AssertGolden(EngineOperations.GetArrayLength(layout, data, input, "rec.items"));
        }

        EngineOutcome outcome = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: ExecutionPath.NoFastPaths);
        StringAssert.Contains(outcome.Rendering, "result.items = PrimitiveArray<UInt16> [2]\n");
        Assert.AreEqual(1, outcome.EngineRuns);

        // A selected read of a member is a path read the engine runs.
        outcome = EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, EngineInput.Span, "rec.items"));
        Assert.AreEqual(1, outcome.EngineRuns);
    }

    /// <summary>Both branches of a conditional group read their golden outcomes.</summary>
    [TestMethod]
    public void Conditional_ReadsBothBranches()
    {
        var layout = new CStruct("struct rec { uint8 flag; if (flag == 1) { uint16 yes; } else { uint8 no; } uint8 tail; };");
        foreach (EngineInput input in ReadInputs)
        {
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [1, 0x34, 0x12, 9], input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [0, 5, 9], input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, [0, 5, 9], input, "rec.yes"));
            EngineDifferential.AssertGolden(EngineOperations.ResolveAddress(layout, [1, 0x34, 0x12, 9], input, "rec.tail"));
        }
    }

    /// <summary>Nested structs and arrays of them read their golden outcomes as a whole, by nested path, and by element.</summary>
    [TestMethod]
    public void NestedStructs_ReadWholeByPathAndByElement()
    {
        var layout = new CStruct("struct inner { uint8 a; uint8 b; }; struct rec { uint8 n; inner items[n]; inner last; };");
        byte[] data = [2, 1, 2, 3, 4, 5, 6];
        foreach (EngineInput input in ReadInputs)
        {
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec.last"));
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec.items[1]"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, input, "rec.items"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, input, "rec.last.b"));
        }

        EngineOutcome outcome = EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, EngineInput.Span, "rec.items"));
        StringAssert.Contains(outcome.Rendering, "result = List<Object> [2]\n");
    }

    /// <summary>A union keeps its raw storage and every member view, as the golden outcomes record.</summary>
    [TestMethod]
    public void Union_KeepsRawStorageAndMemberViews()
    {
        var layout = new CStruct("union u { uint8 a; uint16 b; }; struct rec { u value; uint8 tail; };");
        foreach (EngineInput input in ReadInputs)
        {
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [0x34, 0x12, 9], input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, [0x34, 0x12, 9], input, "rec.value"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, [0x34, 0x12, 9], input, "u"));
        }

        EngineOutcome outcome = EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, [0x34, 0x12, 9], EngineInput.Span, "rec.value"));
        StringAssert.Contains(outcome.Rendering, "result = UnionValue \"u\" {2} selected=none raw=[2] 3412\n");
    }

    /// <summary>Bitfields read, resolve, and update as the golden outcomes record.</summary>
    [TestMethod]
    public void Bitfields_ReadResolveAndUpdate()
    {
        var layout = new CStruct("struct rec { uint8 low : 4; uint8 high : 4; uint16 rest; };");
        byte[] data = [0x21, 0x34, 0x12];
        foreach (EngineInput input in ReadInputs)
        {
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, input, "rec.high"));
            EngineDifferential.AssertGolden(EngineOperations.ResolveAddress(layout, data, input, "rec.rest"));
        }

        EngineDifferential.AssertGolden(EngineOperations.Update(layout, data, EngineInput.Span, "rec.high", 7));
        EngineDifferential.AssertGolden(EngineOperations.Update(layout, data, EngineInput.Stream, "rec.high", 16));
    }

    /// <summary>A pointer, its target, and its address read and resolve as the golden outcomes record; a pointer past the input fails as they record.</summary>
    [TestMethod]
    public void Pointer_ReadsAndResolvesItsTarget()
    {
        var layout = new CStruct("struct rec { uint16 *p; uint8 tail; };", pointerSize: 4);
        byte[] data = [8, 0, 0, 0, 9, 0, 0, 0, 0x34, 0x12];
        byte[] dangling = [40, 0, 0, 0, 9];
        foreach (EngineInput input in ReadInputs)
        {
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, dangling, input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { DereferencePointers = false }));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, input, "rec.p"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, input, "rec.p.value"));
            EngineDifferential.AssertGolden(EngineOperations.ResolveAddress(layout, data, input, "rec.p.value"));
            EngineDifferential.AssertGolden(EngineOperations.ResolveAddress(layout, data, input, "rec.p.address"));
        }

        EngineOutcome outcome = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"));
        StringAssert.Contains(outcome.Rendering, "result.p = Pointer address=8 depth=1 dereferenced=True null=False\nresult.p-> = UInt16 4660\n");
    }

    /// <summary>A terminated string reads and reports its length as the golden outcomes record, and a missing terminator fails as they record.</summary>
    [TestMethod]
    public void TerminatedString_ReadsAndReportsItsLength()
    {
        var layout = new CStruct("struct rec { char name[]; uint8 tail; };");
        byte[] data = [(byte)'h', (byte)'i', 0, 9];
        foreach (EngineInput input in ReadInputs)
        {
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..2], input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxStringBytes = 2 }));
            EngineDifferential.AssertGolden(EngineOperations.GetArrayLength(layout, data, input, "rec.name"));
            EngineDifferential.AssertGolden(EngineOperations.ResolveAddress(layout, data, input, "rec.tail"));
        }
    }

    /// <summary>A custom codec reads, writes, and updates as the golden outcomes record.</summary>
    [TestMethod]
    public void CustomCodec_ReadsWritesAndUpdates()
    {
        var layout = new CStruct("struct rec { vlq count; vlq values[count]; uint8 tail; };", compilationOptions: new CStructCompilationOptions { Codecs = [VlqCodec.Instance,], });
        byte[] data = [2, 0x80, 0x01, 5, 9];
        foreach (EngineInput input in ReadInputs)
        {
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..2], input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.ResolveAddress(layout, data, input, "rec.tail"));
        }

        var value = new Dictionary<string, object?> { ["count"] = 2u, ["values"] = new object[] { 128u, 5u, }, ["tail"] = (byte)9, };
        EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value));
        EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, 3, "rec", value));
        EngineDifferential.AssertGolden(EngineOperations.Update(layout, data, EngineInput.Span, "rec.values[1]", 6u));
        EngineDifferential.AssertGolden(EngineOperations.Update(layout, data, EngineInput.Span, "rec.values[1]", 300u));
    }

    /// <summary>Debug parses of a root and of a path record the golden values and byte ranges.</summary>
    [TestMethod]
    public void DebugParses_RecordValuesAndByteRanges()
    {
        var layout = new CStruct("struct inner { uint8 a; uint16 b; }; struct rec { uint8 n; inner items[n]; char name[]; };");
        byte[] data = [2, 1, 2, 0, 3, 4, 0, (byte)'o', (byte)'k', 0];
        foreach (EngineInput input in ReadInputs)
        {
            EngineDifferential.AssertGolden(EngineOperations.ParseWithDebug(layout, data, input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.ParseWithDebug(layout, data, input, "rec.items[1]"));
            EngineDifferential.AssertGolden(EngineOperations.ParseWithDebug(layout, data[..5], input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValueWithDebug(layout, data, input, "rec"));
            EngineDifferential.AssertGolden(EngineOperations.ReadValueWithDebug(layout, data, input, "rec.items"));
        }

        EngineOutcome outcome = EngineDifferential.AssertGolden(EngineOperations.ParseWithDebug(layout, data, EngineInput.Span, "rec"));
        StringAssert.Contains(outcome.Rendering, "debug = List<DebugData> [6]\ndebug[0] = path=\"rec.n\" start=0 end=1 length=1 type=\"uint8\" bytes=[0]\ndebug[0].value = Byte 2\n");
    }

    /// <summary>Writes to a new array, a span of several capacities, a pre-filled stream, and a buffer writer produce their golden bytes and failures.</summary>
    [TestMethod]
    public void Writes_FillEveryDestination()
    {
        var fixedLayout = new CStruct(FixedLayout);
        var sizedLayout = new CStruct("struct rec { uint8 n; uint16 items[n]; uint8 tail; };");
        StructValue fixedValue = fixedLayout.Parse(FixedRecords.AsSpan(0, 11), "rec");
        var sizedValue = new Dictionary<string, object?> { ["n"] = (byte)2, ["items"] = new ushort[] { 1, 2, }, ["tail"] = (byte)9, };
        var tooMany = new Dictionary<string, object?> { ["n"] = (byte)1, ["items"] = new ushort[] { 1, 2, }, ["tail"] = (byte)9, };
        byte[] prefill = [0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA];
        foreach (ExecutionPath path in Enum.GetValues<ExecutionPath>())
        {
            foreach ((CStruct layout, object value) in new (CStruct, object)[] { (fixedLayout, fixedValue), (sizedLayout, sizedValue), (sizedLayout, tooMany) })
            {
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value), path: path);
                foreach (int capacity in (int[])[0, 5, 6, 11, 14])
                {
                    EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(layout, capacity, "rec", value), path: path);
                }

                EngineDifferential.AssertGolden(EngineOperations.Write(layout, prefill, 2, "rec", value), path: path);
                EngineDifferential.AssertGolden(EngineOperations.WriteAsync(layout, prefill, 3, "rec", value), path: path);
                EngineDifferential.AssertGolden(EngineOperations.SerializeToBufferWriter(layout, "rec", value), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, "rec", value, options: new WriteOptions { MaxTotalBytesWritten = 3 }), path: path);
            }

            EngineDifferential.AssertGolden(EngineOperations.Serialize(sizedLayout, "rec.items", new ushort[] { 5, 6, }, new Dictionary<string, int> { ["n"] = 2 }), path: path);
        }

        EngineOutcome outcome = EngineDifferential.AssertGolden(EngineOperations.SerializeToSpan(sizedLayout, 8, "rec", sizedValue));
        StringAssert.Contains(outcome.Rendering, "result = Int32 6\ndestination = [8] 020100020009CCCC\n");
    }

    /// <summary>Updates of a span and a stream, synchronous and asynchronous, change their golden bytes or fail as the golden outcomes record.</summary>
    [TestMethod]
    public void Updates_ChangeSpansAndStreams()
    {
        var layout = new CStruct("struct rec { uint8 n; uint16 items[n]; char name[]; uint8 tail; };");
        byte[] data = [2, 1, 0, 2, 0, (byte)'a', (byte)'b', 0, 9];
        foreach (ExecutionPath path in Enum.GetValues<ExecutionPath>())
        {
            foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream])
            {
                EngineDifferential.AssertGolden(EngineOperations.Update(layout, data, input, "rec.items[1]", (ushort)0x1234), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Update(layout, data, input, "rec.tail", (byte)7), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Update(layout, data, input, "rec.name", "xy"), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Update(layout, data, input, "rec.name", "longer"), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Update(layout, data, input, "rec.items[2]", (ushort)1), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Update(layout, data[..4], input, "rec.tail", (byte)7), path: path);
            }

            EngineDifferential.AssertGolden(EngineOperations.UpdateAsync(layout, data, "rec.items[0]", (ushort)3), path: path);
        }
    }

    /// <summary>
    ///     The harness detects a difference planted in a run's rendering and reports it as a line diff that shows the golden
    ///     outcome's line (-) and the changed line (+).
    /// </summary>
    [TestMethod]
    public void PlantedDifference_FailsWithReadableDiff()
    {
        var layout = new CStruct("struct rec { uint8 n; uint16 items[n]; uint8 tail; };");
        GoldenOperation operation = EngineOperations.Parse(layout, [2, 1, 0, 2, 0, 9], EngineInput.Span, "rec");

        // A recording run accepts every outcome as the new reference, so nothing planted can be reported: it records the
        // unaltered outcome of each of the three checks below instead.
        if (EngineGolden.Recording)
        {
            for (int check = 0; check < 3; check++)
            {
                EngineDifferential.AssertGolden(operation);
            }

            return;
        }

        AssertFailedException failure = Assert.Throws<AssertFailedException>(
            () => EngineDifferential.AssertGolden(operation, alter: rendering => rendering.Replace("result.items[1] = UInt16 2", "result.items[1] = UInt16 3", StringComparison.Ordinal)));
        StringAssert.Contains(failure.Message, "Parse rec (Span) (Fastest): the golden outcome (-) and the engine (+) differ:");
        StringAssert.Contains(failure.Message, "\n  result.items[0] = UInt16 1\n- result.items[1] = UInt16 2\n+ result.items[1] = UInt16 3\n  result.tail = Byte 9\n");

        // A dropped line and an added line are reported as well; an unchanged rendering passes.
        failure = Assert.Throws<AssertFailedException>(
            () => EngineDifferential.AssertGolden(operation, alter: rendering => rendering + "position = 6\n"));
        StringAssert.Contains(failure.Message, "+ position = 6\n");
        EngineDifferential.AssertGolden(operation, alter: rendering => rendering);
    }

    /// <summary>The line diff keeps unchanged lines near a change, elides distant ones, and aligns insertions.</summary>
    [TestMethod]
    public void Diff_ShowsChangesInContext()
    {
        string expected = string.Join("\n", Enumerable.Range(0, 20).Select(index => "line " + index));
        string actual = expected.Replace("line 10", "line ten", StringComparison.Ordinal).Replace("line 15\n", "line 15\ninserted\n", StringComparison.Ordinal);
        string diff = EngineDifferential.Diff(expected, actual);
        Assert.AreEqual(
            "  ...\n  line 7\n  line 8\n  line 9\n- line 10\n+ line ten\n  line 11\n  line 12\n  line 13\n  line 14\n  line 15\n+ inserted\n  line 16\n  line 17\n  line 18\n  ...\n",
            diff);
    }
}
