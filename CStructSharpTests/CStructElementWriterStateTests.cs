namespace CStructSharp.Tests;

/// <summary>
///     Exercises <see cref="CStructElementWriterState"/> directly, independent of a real write operation. Only
///     reachable indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c>
///     partial class.
/// </summary>
[TestClass]
public class CStructElementWriterStateTests
{
    /// <summary>A valid stream and options must produce a state exposing those options' derived fields.</summary>
    [TestMethod]
    public void Constructor_ValidInputs_ExposesDerivedFields()
    {
        using var stream = new MemoryStream();
        var options = new WriteOptions { AddressingMode = PointerAddressingMode.Relative, Origin = 7, };

        var state = new CStructElementWriterState(stream, [], aligned: true, options);

        Assert.AreEqual(PointerAddressingMode.Relative, state.AddressingMode);
        Assert.AreEqual(7L, state.PointerOrigin);
        Assert.IsTrue(state.Aligned);
        Assert.AreEqual(0, state.StructureDepth);
    }

    /// <summary>A negative initial structure depth cannot represent a real traversal position.</summary>
    [TestMethod]
    public void Constructor_NegativeInitialStructureDepth_Throws()
    {
        using var stream = new MemoryStream();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CStructElementWriterState(stream, [], aligned: false, new WriteOptions(), initialStructureDepth: -1));
    }

    /// <summary>An initial structure depth beyond the configured nesting limit is already out of range at construction.</summary>
    [TestMethod]
    public void Constructor_InitialStructureDepthBeyondLimit_Throws()
    {
        using var stream = new MemoryStream();
        var options = new WriteOptions { MaxNestingDepth = 2, };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CStructElementWriterState(stream, [], aligned: false, options, initialStructureDepth: 3));
    }

    /// <summary>Entering structures up to the configured limit succeeds; one more must fail without corrupting the depth.</summary>
    [TestMethod]
    public void EnterStructure_ExceedsMaxNestingDepth_ThrowsAndLeavesDepthAtTheLimit()
    {
        using var stream = new MemoryStream();
        var state = new CStructElementWriterState(stream, [], aligned: false, new WriteOptions { MaxNestingDepth = 1, });

        state.EnterStructure();

        Assert.Throws<CStructWriteLimitException>(() => state.EnterStructure());
        Assert.AreEqual(1, state.StructureDepth);
    }

    /// <summary>Exiting a structure releases exactly one level, allowing another entry afterward.</summary>
    [TestMethod]
    public void ExitStructure_ReleasesOneLevel()
    {
        using var stream = new MemoryStream();
        var state = new CStructElementWriterState(stream, [], aligned: false, new WriteOptions { MaxNestingDepth = 1, });

        state.EnterStructure();
        state.ExitStructure();
        state.EnterStructure();

        Assert.AreEqual(1, state.StructureDepth);
    }

    /// <summary>EnsureStringBytes must delegate to the underlying write-budget stream's string-byte check.</summary>
    [TestMethod]
    public void EnsureStringBytes_ExceedsTheConfiguredBudget_Throws()
    {
        using var stream = new MemoryStream();
        var state = new CStructElementWriterState(stream, [], aligned: false, new WriteOptions { MaxStringBytes = 4, });

        state.EnsureStringBytes(4);
        Assert.Throws<CStructWriteLimitException>(() => state.EnsureStringBytes(5));
    }

    /// <summary>WriteZeroes must delegate to the underlying write-budget stream and actually advance the stream.</summary>
    [TestMethod]
    public void WriteZeroes_WritesTheRequestedZeroFilledRegion()
    {
        using var stream = new MemoryStream();
        var state = new CStructElementWriterState(stream, [], aligned: false, new WriteOptions());

        state.WriteZeroes(3);

        CollectionAssert.AreEqual(new byte[] { 0, 0, 0, }, stream.ToArray());
    }

    /// <summary>Null options snapshot to WriteOptions' own documented defaults.</summary>
    [TestMethod]
    public void SnapshotWriteOptions_NullOptions_UsesWriteOptionsDefaults()
    {
        WriteOptions snapshot = CStructElementWriterState.SnapshotWriteOptions(null);

        Assert.AreEqual(new WriteOptions().MaxArrayElements, snapshot.MaxArrayElements);
        Assert.AreEqual(new WriteOptions().MaxNestingDepth, snapshot.MaxNestingDepth);
    }

    /// <summary>Passing an UpdateOptions instance to SnapshotWriteOptions must preserve its update-specific semantics.</summary>
    [TestMethod]
    public void SnapshotWriteOptions_GivenUpdateOptions_RetainsUpdateSemantics()
    {
        var update = new UpdateOptions { DereferencePointers = false, };

        WriteOptions snapshot = CStructElementWriterState.SnapshotWriteOptions(update);

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

        UpdateOptions snapshot = CStructElementWriterState.SnapshotUpdateOptions(options);

        Assert.AreEqual(3, snapshot.MaxArrayElements);
        Assert.IsFalse(snapshot.DereferencePointers);
        Assert.IsFalse(snapshot.RequireExistingPointerTarget);
        Assert.IsFalse(snapshot.ClearUnionStorage);
        Assert.AreEqual(2, snapshot.MaxTraversalPointerDepth);
    }

    /// <summary>
    ///     Regression coverage for the architecture improvement plan's optimization (AP-2.1) that converted WriteOptions/UpdateOptions to records and replaced their hand-maintained
    ///     property-by-property snapshot with the record's own <c>with</c> expression. Uses the record's
    ///     auto-generated structural equality as the proof: a snapshot that is value-equal to its source across
    ///     every one of its properties (not just the handful an earlier, more narrowly-listed test happens to
    ///     assert on) is exactly the guarantee `with` is meant to provide - and, unlike a hand-picked property
    ///     list, this assertion could never silently start passing again if a future property were added and
    ///     accidentally left out of a hand-maintained copy.
    /// </summary>
    [TestMethod]
    public void SnapshotUpdateOptions_ProducesARecordValueEqualToTheSource()
    {
        var options = new UpdateOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            BindingMode = PocoBindingMode.PublicReadWrite,
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

        UpdateOptions snapshot = CStructElementWriterState.SnapshotUpdateOptions(options);

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
            BindingMode = PocoBindingMode.PublicReadWrite,
            MaxArrayElements = 3,
            MaxStringBytes = 11,
            MaxTotalBytesWritten = 22,
            MaxNestingDepth = 33,
            Origin = 44,
        };

        WriteOptions snapshot = CStructElementWriterState.SnapshotWriteOptions(options);

        Assert.AreEqual(options, snapshot);
        Assert.AreNotSame(options, snapshot);
    }

    /// <summary>A negative array-element limit is not a valid write budget.</summary>
    [TestMethod]
    public void ValidateWriteOptions_NegativeMaxArrayElements_Throws()
    {
        var options = new WriteOptions { MaxArrayElements = -1, };

        Assert.Throws<ArgumentOutOfRangeException>(() => CStructElementWriterState.ValidateWriteOptions(options));
    }

    /// <summary>A non-positive nesting depth would forbid even the root object.</summary>
    [TestMethod]
    public void ValidateWriteOptions_NonPositiveMaxNestingDepth_Throws()
    {
        var options = new WriteOptions { MaxNestingDepth = 0, };

        Assert.Throws<ArgumentOutOfRangeException>(() => CStructElementWriterState.ValidateWriteOptions(options));
    }

    /// <summary>A finite, valid set of write options passes validation without throwing.</summary>
    [TestMethod]
    public void ValidateWriteOptions_ValidOptions_DoesNotThrow()
    {
        CStructElementWriterState.ValidateWriteOptions(new WriteOptions());
    }
}
