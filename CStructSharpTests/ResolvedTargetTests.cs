namespace CStructSharp.Tests;

using CStructSharp.Structure;

/// <summary>
///     Exercises <see cref="ResolvedTarget"/> and <see cref="TargetResolutionContext"/> directly, independent of a
///     real path-resolution traversal. Only reachable indirectly through the public API before these types were
///     extracted from the God-Object <c>CStruct</c> partial class.
/// </summary>
[TestClass]
public class ResolvedTargetTests
{
    /// <summary>A selected array index makes <see cref="ResolvedTarget.SelectsArrayElement"/> true; its absence, false.</summary>
    [TestMethod]
    public void SelectsArrayElement_ReflectsWhetherAnIndexWasSelected()
    {
        Assert.IsTrue(MakeTarget(selectedArrayIndex: 3).SelectsArrayElement);
        Assert.IsFalse(MakeTarget(selectedArrayIndex: null).SelectsArrayElement);
    }

    /// <summary>Any positive count of consumed pointer accessors marks the target as having traversed a pointer.</summary>
    [TestMethod]
    public void TraversesPointer_ReflectsWhetherAnyPointerAccessorsWereConsumed()
    {
        Assert.IsTrue(MakeTarget(pointerAccessorsConsumed: 1).TraversesPointer);
        Assert.IsFalse(MakeTarget(pointerAccessorsConsumed: 0).TraversesPointer);
    }

    /// <summary>
    ///     The constructor must snapshot the debug-prefix and selected-index lists rather than retaining the
    ///     caller's own mutable collection, so a later mutation of the caller's list cannot change an already
    ///     published, supposedly immutable target.
    /// </summary>
    [TestMethod]
    public void Constructor_CopiesListsInsteadOfRetainingTheCallersMutableCollection()
    {
        var debugPrefix = new List<CStructElement> { ScalarField("a"), };
        var selectedIndexes = new List<int> { 1, };

        ResolvedTarget target = new(
            address: 0,
            kind: ResolvedTargetKind.Field,
            declaredField: null,
            effectiveField: null,
            writableField: null,
            targetElement: null,
            debugPrefix: debugPrefix,
            codecName: null,
            isArray: false,
            arrayLength: null,
            selectedArrayIndex: null,
            selectedIndexes: selectedIndexes,
            bitOffset: 0,
            bitStorageSize: 0,
            unionStorageAddress: null,
            unionStorageSize: null,
            pointerStorageAddress: null,
            pointerTargetAddress: null,
            pointerAccessorsConsumed: 0,
            remainingPointerDepth: 0,
            alignment: 1,
            fixedSize: null,
            containingStructureDepth: 0);

        debugPrefix.Add(ScalarField("b"));
        selectedIndexes.Add(2);

        Assert.HasCount(1, target.DebugPrefix);
        Assert.HasCount(1, target.SelectedIndexes);
    }

    /// <summary>Entering a field appends it to the debug prefix and leaves unrelated union/pointer state untouched.</summary>
    [TestMethod]
    public void EnterField_AppendsFieldAndIndex_PreservesUnrelatedState()
    {
        var context = new TargetResolutionContext(
            debugPrefix: [ScalarField("root"),],
            selectedIndexes: [],
            unionStorageAddress: 100,
            unionStorageSize: 8);

        TargetResolutionContext next = context.EnterField(ScalarField("child"), selectedIndex: 5);

        Assert.HasCount(2, next.DebugPrefix);
        Assert.AreEqual("child", next.DebugPrefix[1].Name.Name);
        CollectionAssert.AreEqual(new[] { 5, }, next.SelectedIndexes.ToArray());
        Assert.AreEqual(100L, next.UnionStorageAddress);
        Assert.AreEqual(8, next.UnionStorageSize);
    }

    /// <summary>Entering a field without an array index leaves the selected-index list unchanged.</summary>
    [TestMethod]
    public void EnterField_WithoutASelectedIndex_DoesNotAppendToSelectedIndexes()
    {
        var context = new TargetResolutionContext(debugPrefix: [], selectedIndexes: [7,]);

        TargetResolutionContext next = context.EnterField(ScalarField("child"), selectedIndex: null);

        CollectionAssert.AreEqual(new[] { 7, }, next.SelectedIndexes.ToArray());
    }

    /// <summary>Entering a union records its storage address and size while leaving the debug prefix untouched.</summary>
    [TestMethod]
    public void EnterUnion_RecordsStorageAddressAndSize()
    {
        var context = new TargetResolutionContext(debugPrefix: [ScalarField("root"),], selectedIndexes: []);

        TargetResolutionContext next = context.EnterUnion(address: 64, size: 16);

        Assert.AreEqual(64L, next.UnionStorageAddress);
        Assert.AreEqual(16, next.UnionStorageSize);
        Assert.HasCount(1, next.DebugPrefix);
    }

    /// <summary>Following a pointer records its storage and target address and increments the consumed count.</summary>
    [TestMethod]
    public void FollowPointer_RecordsAddressesAndIncrementsConsumedCount()
    {
        var context = new TargetResolutionContext(debugPrefix: [], selectedIndexes: [], pointerAccessorsConsumed: 2);

        TargetResolutionContext next = context.FollowPointer(storageAddress: 40, targetAddress: 200);

        Assert.AreEqual(40L, next.PointerStorageAddress);
        Assert.AreEqual(200L, next.PointerTargetAddress);
        Assert.AreEqual(3, next.PointerAccessorsConsumed);
    }

    /// <summary>
    ///     A pointer chain long enough to overflow the consumed-count accounting must fail loudly instead of
    ///     silently wrapping back to a small, misleadingly safe-looking number.
    /// </summary>
    [TestMethod]
    public void FollowPointer_ConsumedCountAtMaxValue_ThrowsInsteadOfWrapping()
    {
        var context = new TargetResolutionContext(
            debugPrefix: [],
            selectedIndexes: [],
            pointerAccessorsConsumed: int.MaxValue);

        Assert.Throws<OverflowException>(() => context.FollowPointer(storageAddress: 0, targetAddress: 0));
    }

    private static Field ScalarField(string name)
    {
        return new Field(new Identifier("uint8"), new Identifier(name), Field.NoArray, 0);
    }

    private static ResolvedTarget MakeTarget(int? selectedArrayIndex = null, int pointerAccessorsConsumed = 0)
    {
        return new ResolvedTarget(
            address: 0,
            kind: ResolvedTargetKind.Field,
            declaredField: null,
            effectiveField: null,
            writableField: null,
            targetElement: null,
            debugPrefix: [],
            codecName: null,
            isArray: selectedArrayIndex.HasValue,
            arrayLength: null,
            selectedArrayIndex: selectedArrayIndex,
            selectedIndexes: [],
            bitOffset: 0,
            bitStorageSize: 0,
            unionStorageAddress: null,
            unionStorageSize: null,
            pointerStorageAddress: null,
            pointerTargetAddress: null,
            pointerAccessorsConsumed: pointerAccessorsConsumed,
            remainingPointerDepth: 0,
            alignment: 1,
            fixedSize: null,
            containingStructureDepth: 0);
    }
}
