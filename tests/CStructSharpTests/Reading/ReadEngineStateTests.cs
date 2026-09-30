namespace CStructSharp.Tests;

using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Reading;

/// <summary>
///     Exercises the compiled engine's per-operation read state directly, independent of a real parse: the validation of
///     the source and settings (<see cref="ReadOperationSettings.Validate"/>), the settings and nesting depth the state
///     carries (<see cref="ReadEngineState"/>), qualified publication into the slots, and the debug recorder.
/// </summary>
[TestClass]
public class ReadEngineStateTests
{
    /// <summary>A check that runs against a read state passed by reference.</summary>
    /// <param name="state">The state.</param>
    private delegate void StateAction(ref ReadEngineState state);

    /// <summary>Each invalid budget explains which setting prevents a read operation from starting.</summary>
    [TestMethod]
    public void Validate_InvalidBudgetsIdentifyTheRejectedSetting()
    {
        using var stream = new MemoryStream(new byte[4]);
        ReadOperationSettings defaults = ReadOperationSettings.SnapshotReadOptions(null);
        (ReadOperationSettings Settings, string Reason)[] cases =
        [
            (defaults with { MaxPointerDepth = -1, }, "Maximum pointer depth cannot be negative"),
            (defaults with { MaxPointerTargetBytes = -1, }, "Maximum pointer target bytes cannot be negative"),
            (defaults with { MaxArrayElements = -1, }, "Maximum array elements cannot be negative"),
            (defaults with { MaxStringBytes = -1, }, "Read byte limits cannot be negative"),
            (defaults with { MaxTotalBytesRead = -1, }, "Read byte limits cannot be negative"),
            (defaults with { MaxNestingDepth = 0, }, "Maximum nesting depth must be greater than zero"),
        ];

        foreach ((ReadOperationSettings settings, string reason) in cases)
        {
            // Check the diagnostic as well as rejection so callers can correct the responsible option.
            ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() => ReadOperationSettings.Validate(stream, settings));
            Assert.AreEqual("options", failure.ParamName);
            StringAssert.Contains(failure.Message, reason);
        }
    }

    /// <summary>
    ///     While a prefix is active a captured value is published under the qualified name; a removed value removes the
    ///     qualified one, so a missing field cannot leave a stale qualified value; without a prefix nothing is published.
    /// </summary>
    [TestMethod]
    public void PublishQualified_RemovesStaleValuesAndStopsWithoutAPrefix()
    {
        var layout = new CStruct("struct header { uint8 count; }; struct rec { header hdr; uint8 items[hdr.count]; };");
        ReadProgram.QualifiedTarget[] targets = layout.Compilation.SlotTable.ReadPrograms.GetQualifiedTargets("count");
        Assert.HasCount(1, targets);
        int slot = targets[0].Slot;
        RunState(
            layout,
            ReadOperationSettings.SnapshotReadOptions(null),
            (ref ReadEngineState state) =>
            {
                state.QualifiedPrefix = "hdr.";
                state.PublishQualified(targets, SlotValue.FromLiteral(3));
                Assert.AreEqual(SlotState.Literal, state.Slots.Get(slot).State);
                Assert.AreEqual((Int128)3, state.Slots.Get(slot).Value);

                state.PublishQualified(targets, SlotValue.Undefined);
                Assert.AreEqual(SlotState.Undefined, state.Slots.Get(slot).State, "a removed value removes the qualified one");

                state.QualifiedPrefix = null;
                state.PublishQualified(targets, SlotValue.FromLiteral(5));
                Assert.AreEqual(SlotState.Undefined, state.Slots.Get(slot).State, "nothing is published without a prefix");
            });
    }

    /// <summary>Default settings of a valid readable, seekable stream produce a state exposing those settings.</summary>
    [TestMethod]
    public void State_ExposesTheSuppliedSettings()
    {
        using var stream = new MemoryStream(new byte[16]);
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(
            new ReadOptions { MaxPointerDepth = 5, MaxArrayElements = 10, MaxNestingDepth = 8, });
        ReadOperationSettings.Validate(stream, settings);

        RunState(
            new CStruct("struct rec { uint8 a; };", aligned: true),
            settings,
            (ref ReadEngineState state) =>
            {
                Assert.AreEqual(5, state.MaxPointerDepth);
                Assert.AreEqual(10, state.MaxArrayElements);
                Assert.AreEqual(8, state.MaxNestingDepth);
                Assert.IsTrue(state.Layout.Aligned);
                Assert.AreEqual(0, state.StructureDepth);
            });
    }

    /// <summary>A null stream is a caller programming error, not a domain failure.</summary>
    [TestMethod]
    public void Validate_NullStream_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => ReadOperationSettings.Validate(null!, ReadOperationSettings.SnapshotReadOptions(null)));
    }

    /// <summary>A write-only, non-readable stream cannot back a read operation.</summary>
    [TestMethod]
    public void Validate_NonReadableStream_Throws()
    {
        using var writeOnly = new UnreadableStream();

        ArgumentException failure = Assert.Throws<ArgumentException>(() => ReadOperationSettings.Validate(writeOnly, ReadOperationSettings.SnapshotReadOptions(null)));
        StringAssert.Contains(failure.Message, "Parsing requires a readable, seekable stream");
        Assert.AreEqual("stream", failure.ParamName);
    }

    /// <summary>A negative pointer depth has no meaningful safety interpretation.</summary>
    [TestMethod]
    public void Validate_NegativeMaxPointerDepth_Throws()
    {
        using var stream = new MemoryStream(new byte[4]);
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(null) with { MaxPointerDepth = -1, };

        Assert.Throws<ArgumentOutOfRangeException>(() => ReadOperationSettings.Validate(stream, settings));
    }

    /// <summary>A zero or negative nesting depth would forbid even the root object.</summary>
    [TestMethod]
    public void Validate_NonPositiveMaxNestingDepth_Throws()
    {
        using var stream = new MemoryStream(new byte[4]);
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(null) with { MaxNestingDepth = 0, };

        Assert.Throws<ArgumentOutOfRangeException>(() => ReadOperationSettings.Validate(stream, settings));
    }

    /// <summary>Entering structures up to the configured limit succeeds; one more must fail and not corrupt the depth.</summary>
    [TestMethod]
    public void EnterStructure_ExceedsMaxNestingDepth_ThrowsAndLeavesDepthAtTheLimit()
    {
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(null) with { MaxNestingDepth = 2, };
        RunState(
            new CStruct("struct rec { uint8 a; };"),
            settings,
            (ref ReadEngineState state) =>
            {
                var cursor = new MemoryReadCursor([0], 0, 1, 0, settings.MaxStringBytes, settings.MaxTotalBytesRead, default);
                state.EnterStructure(ref cursor);
                state.EnterStructure(ref cursor);

                bool failed = false;
                try
                {
                    state.EnterStructure(ref cursor);
                }
                catch (CStructReadLimitException)
                {
                    failed = true;
                }

                Assert.IsTrue(failed, "a third level exceeds the limit");
                Assert.AreEqual(2, state.StructureDepth);
            });
    }

    /// <summary>Releasing a structure level allows another entry afterward.</summary>
    [TestMethod]
    public void ReleasedLevel_AllowsAnotherEntry()
    {
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(new ReadOptions { MaxNestingDepth = 1, });
        RunState(
            new CStruct("struct rec { uint8 a; };"),
            settings,
            (ref ReadEngineState state) =>
            {
                var cursor = new MemoryReadCursor([0], 0, 1, 0, settings.MaxStringBytes, settings.MaxTotalBytesRead, default);
                state.EnterStructure(ref cursor);
                state.StructureDepth--;
                state.EnterStructure(ref cursor);

                Assert.AreEqual(1, state.StructureDepth);
            });
    }

    /// <summary>
    ///     A debug record holds exactly the requested byte range, the value and its type, and no copy of the bytes; a
    ///     zero-width value has an empty range.
    /// </summary>
    [TestMethod]
    public void DebugRecord_CapturesTheRequestedRange()
    {
        var recorder = new DebugRecorder(trace: false);
        recorder.Record(1, 3, null, 0x2233, "uint16");
        recorder.Record(0, 0, null, 0, "none");

        Assert.HasCount(2, recorder.Records);
        DebugData entry = recorder.Records[0];
        Assert.AreEqual(1L, entry.Start);
        Assert.AreEqual(3L, entry.End);
        Assert.AreEqual("uint16", entry.TypeName);
        Assert.IsTrue(entry.Bytes.IsEmpty, "a field record carries its range, not a copy of its bytes");
        Assert.AreEqual(0L, recorder.Records[1].Length);
        Assert.IsTrue(recorder.Records[1].Bytes.IsEmpty);
    }

    /// <summary>Runs <paramref name="body"/> with the read state of one operation on <paramref name="layout"/>, then releases it.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="settings">The operation's settings.</param>
    /// <param name="body">The checks, given the state by reference.</param>
    private static void RunState(CStruct layout, ReadOperationSettings settings, StateAction body)
    {
        VariableSlots slots = VariableSlots.Create(layout.Compilation.SlotTable, LayoutVariableInput.FromIntegers(null));
        var state = new ReadEngineState(layout, slots, settings, null);
        try
        {
            body(ref state);
        }
        finally
        {
            state.Release();
            slots.Dispose();
        }
    }
}
