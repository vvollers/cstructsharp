namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Streams;
using CStructSharp.Writing;

/// <summary>
///     Exercises the compiled engine's per-operation write state directly, independent of a real write: the options and
///     nesting depth the state carries (<see cref="WriteEngineState"/>), the string budget and zero fill of the budget
///     stream the engine writes through (<see cref="WriteBudgetStream"/>), and the option snapshots and validation every
///     write takes at its boundary (<see cref="WriteOptionSnapshots"/>).
/// </summary>
[TestClass]
public class WriteEngineStateTests
{
    /// <summary>A check that runs against a write state passed by reference.</summary>
    /// <param name="state">The state.</param>
    private delegate void StateAction(ref WriteEngineState state);

    /// <summary>Valid options must produce a state exposing those options and the layout's placement, at depth zero.</summary>
    [TestMethod]
    public void State_ExposesTheSuppliedOptions()
    {
        var options = new WriteOptions { AddressingMode = PointerAddressingMode.Relative, Origin = 7, UnknownMembers = UnknownMemberPolicy.Reject, };

        RunState(
            new CStruct("struct rec { uint8 a; };", aligned: true),
            options,
            (ref WriteEngineState state) =>
            {
                Assert.AreEqual(PointerAddressingMode.Relative, state.Options.AddressingMode);
                Assert.AreEqual(7L, state.Options.Origin);
                Assert.IsTrue(state.RejectUnknownMembers);
                Assert.IsTrue(state.Layout.Aligned);
                Assert.AreEqual(0, state.StructureDepth);
            });
    }

    /// <summary>Entering structures up to the configured limit succeeds; one more must fail without corrupting the depth.</summary>
    [TestMethod]
    public void EnterStructure_ExceedsMaxNestingDepth_ThrowsAndLeavesDepthAtTheLimit()
    {
        RunState(
            new CStruct("struct rec { uint8 a; };"),
            new WriteOptions { MaxNestingDepth = 1, },
            (ref WriteEngineState state) =>
            {
                state.EnterStructure();

                bool failed = false;
                try
                {
                    state.EnterStructure();
                }
                catch (CStructWriteLimitException)
                {
                    failed = true;
                }

                Assert.IsTrue(failed, "a second level exceeds the limit");
                Assert.AreEqual(1, state.StructureDepth);
            });
    }

    /// <summary>Releasing a structure level allows another entry afterward.</summary>
    [TestMethod]
    public void ReleasedLevel_AllowsAnotherEntry()
    {
        RunState(
            new CStruct("struct rec { uint8 a; };"),
            new WriteOptions { MaxNestingDepth = 1, },
            (ref WriteEngineState state) =>
            {
                state.EnterStructure();
                state.StructureDepth--;
                state.EnterStructure();

                Assert.AreEqual(1, state.StructureDepth);
            });
    }

    /// <summary>The budget stream a write goes through checks one string's encoded bytes against the per-string limit.</summary>
    [TestMethod]
    public void EnsureStringBytes_ExceedsTheConfiguredBudget_Throws()
    {
        using var stream = new MemoryStream();
        var budget = new WriteBudgetStream(stream, new WriteOptions { MaxStringBytes = 4, });

        budget.EnsureStringBytes(4);
        Assert.Throws<CStructWriteLimitException>(() => budget.EnsureStringBytes(5));
    }

    /// <summary>The budget stream writes a zero-filled region and advances the stream.</summary>
    [TestMethod]
    public void WriteZeroes_WritesTheRequestedZeroFilledRegion()
    {
        using var stream = new MemoryStream();
        var budget = new WriteBudgetStream(stream, new WriteOptions());

        budget.WriteZeroes(3);

        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, }, stream.ToArray());
    }

    /// <summary>Null options snapshot to WriteOptions' own documented defaults.</summary>
    [TestMethod]
    public void SnapshotWriteOptions_NullOptions_UsesWriteOptionsDefaults()
    {
        WriteOptions snapshot = WriteOptionSnapshots.SnapshotWriteOptions(null);

        Assert.AreEqual(new WriteOptions().MaxArrayElements, snapshot.MaxArrayElements);
        Assert.AreEqual(new WriteOptions().MaxNestingDepth, snapshot.MaxNestingDepth);
    }

    /// <summary>Passing an UpdateOptions instance to SnapshotWriteOptions must preserve its update-specific semantics.</summary>
    [TestMethod]
    public void SnapshotWriteOptions_GivenUpdateOptions_RetainsUpdateSemantics()
    {
        var update = new UpdateOptions { DereferencePointers = false, };

        WriteOptions snapshot = WriteOptionSnapshots.SnapshotWriteOptions(update);

        Assert.IsInstanceOfType<UpdateOptions>(snapshot);
        Assert.IsFalse(((UpdateOptions)snapshot).DereferencePointers);
    }

    /// <summary>Every field of a caller-supplied UpdateOptions, including inherited WriteOptions fields, must carry over.</summary>
    [TestMethod]
    public void SnapshotUpdateOptions_CopiesEveryFieldIncludingInheritedWriteOptions()
    {
        var options = new UpdateOptions
        {
            MaxArrayElements = 3,
            DereferencePointers = false,
            RequireExistingPointerTarget = false,
            ClearUnionStorage = false,
            MaxTraversalPointerDepth = 2,
        };

        UpdateOptions snapshot = WriteOptionSnapshots.SnapshotUpdateOptions(options);

        Assert.AreEqual(3, snapshot.MaxArrayElements);
        Assert.IsFalse(snapshot.DereferencePointers);
        Assert.IsFalse(snapshot.RequireExistingPointerTarget);
        Assert.IsFalse(snapshot.ClearUnionStorage);
        Assert.AreEqual(2, snapshot.MaxTraversalPointerDepth);
    }

    /// <summary>
    ///     WriteOptions and UpdateOptions are records whose snapshot is the record's own <c>with</c> copy. The record's
    ///     structural equality is the proof: a snapshot value-equal to its source across every property is exactly what
    ///     <c>with</c> guarantees, and unlike a hand-picked property list the assertion covers a property added later.
    /// </summary>
    [TestMethod]
    public void SnapshotUpdateOptions_ProducesARecordValueEqualToTheSource()
    {
        var options = new UpdateOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            MaxArrayElements = 3,
            MaxStringBytes = 11,
            MaxTotalBytesWritten = 22,
            MaxNestingDepth = 33,
            Origin = 44,
            DereferencePointers = false,
            RequireExistingPointerTarget = false,
            ClearUnionStorage = false,
            MaxTraversalPointerDepth = 2,
            MaxTraversalPointerTargetBytes = 55,
            MaxTraversalStringBytes = 66,
            MaxTraversalBytesRead = 77,
            MaxTraversalNestingDepth = 88,
        };

        UpdateOptions snapshot = WriteOptionSnapshots.SnapshotUpdateOptions(options);

        Assert.AreEqual(options, snapshot);
        Assert.AreNotSame(options, snapshot);
    }

    /// <summary>The same record-equality proof for the plain WriteOptions snapshot path (a non-UpdateOptions source).</summary>
    [TestMethod]
    public void SnapshotWriteOptions_ProducesARecordValueEqualToTheSource()
    {
        var options = new WriteOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            MaxArrayElements = 3,
            MaxStringBytes = 11,
            MaxTotalBytesWritten = 22,
            MaxNestingDepth = 33,
            Origin = 44,
        };

        WriteOptions snapshot = WriteOptionSnapshots.SnapshotWriteOptions(options);

        Assert.AreEqual(options, snapshot);
        Assert.AreNotSame(options, snapshot);
    }

    /// <summary>A negative array-element limit is not a valid write budget.</summary>
    [TestMethod]
    public void ValidateWriteOptions_NegativeMaxArrayElements_Throws()
    {
        var options = new WriteOptions { MaxArrayElements = -1, };

        Assert.Throws<ArgumentOutOfRangeException>(() => WriteOptionSnapshots.ValidateWriteOptions(options));
    }

    /// <summary>A non-positive nesting depth would forbid even the root object.</summary>
    [TestMethod]
    public void ValidateWriteOptions_NonPositiveMaxNestingDepth_Throws()
    {
        var options = new WriteOptions { MaxNestingDepth = 0, };

        Assert.Throws<ArgumentOutOfRangeException>(() => WriteOptionSnapshots.ValidateWriteOptions(options));
    }

    /// <summary>A finite, valid set of write options passes validation without throwing.</summary>
    [TestMethod]
    public void ValidateWriteOptions_ValidOptions_DoesNotThrow()
    {
        WriteOptionSnapshots.ValidateWriteOptions(new WriteOptions());
    }

    /// <summary>Runs <paramref name="body"/> with the write state of one operation on <paramref name="layout"/>, then releases it.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="options">The operation's options.</param>
    /// <param name="body">The checks, given the state by reference.</param>
    private static void RunState(CStruct layout, WriteOptions options, StateAction body)
    {
        VariableSlots slots = VariableSlots.Create(layout.Compilation.SlotTable, LayoutVariableInput.FromIntegers(null));
        var state = new WriteEngineState(layout, slots, options);
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
