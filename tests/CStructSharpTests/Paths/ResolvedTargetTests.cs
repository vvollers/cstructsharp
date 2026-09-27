namespace CStructSharp.Tests;

using CStructSharp.Addressing;
using CStructSharp.Compilation;

/// <summary>
///     Exercises <see cref="ResolvedTarget"/> and <see cref="TargetResolutionContext"/> directly, independent of a
///     real path-resolution traversal.
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
    ///     A target must snapshot the debug-prefix and selected-index lists rather than retaining the caller's own
    ///     mutable collection, so a later mutation of the caller's list cannot change an already published,
    ///     supposedly immutable target. (An array is kept as it is: the traversal context builds a fresh one per step
    ///     and never mutates it, which is what the address benchmarks pay for otherwise.)
    /// </summary>
    [TestMethod]
    public void Field_CopiesListsInsteadOfRetainingTheCallersMutableCollection()
    {
        var debugPrefix = new List<string> { "a", };
        var selectedIndexes = new List<int> { 1, };
        var context = new TargetResolutionContext(debugPrefix, selectedIndexes);

        ResolvedTarget target = ResolvedTarget.Field(Value(), 0, default, 0, 0, 0, context);

        debugPrefix.Add("b");
        selectedIndexes.Add(2);

        Assert.HasCount(1, target.DebugPrefix);
        Assert.HasCount(1, target.SelectedIndexes);
    }

    /// <summary>Each factory records its kind and the field it reads, and a field target keeps the pointer levels it declares.</summary>
    [TestMethod]
    public void Factories_RecordKindAndFields()
    {
        var context = new TargetResolutionContext(["root",], []);
        CompiledField value = Value();

        ResolvedTarget field = ResolvedTarget.Field(value, 4, new ArraySelection(true, null, 2), 0, 0, 1, context);
        Assert.AreEqual(ResolvedTargetKind.ArrayElement, field.Kind);
        Assert.AreSame(value, field.EffectiveCompiledField);
        Assert.AreSame(value, field.WritableCompiledField);
        Assert.AreEqual(1, field.ContainingStructureDepth);

        ResolvedTarget address = ResolvedTarget.PointerAddress(value, 4, 8, default, 0, context);
        Assert.AreEqual(ResolvedTargetKind.PointerAddress, address.Kind);
        Assert.AreEqual(8, address.Alignment);
        Assert.IsNull(address.WritableCompiledField);

        ResolvedTarget pointed = ResolvedTarget.PointerValue(value, value, 200, 0, default, 0, context.FollowPointer(200));
        Assert.AreEqual(ResolvedTargetKind.PointerValue, pointed.Kind);
        Assert.AreEqual(200L, pointed.PointerTargetAddress);
        Assert.IsTrue(pointed.TraversesPointer);
    }

    /// <summary>Entering a field appends it to the debug prefix and leaves the pointer state untouched.</summary>
    [TestMethod]
    public void EnterField_AppendsFieldAndIndex_PreservesUnrelatedState()
    {
        var context = new TargetResolutionContext(
            debugPrefix: ["root",],
            selectedIndexes: [],
            pointerTargetAddress: 100,
            pointerAccessorsConsumed: 1);

        TargetResolutionContext next = context.EnterField("child", selectedIndexes: [5,]);

        Assert.HasCount(2, next.DebugPrefix);
        Assert.AreEqual("child", next.DebugPrefix[1]);
        CollectionAssert.AreEqual(new[] { 5, }, next.SelectedIndexes.ToArray());
        Assert.AreEqual(100L, next.PointerTargetAddress);
        Assert.AreEqual(1, next.PointerAccessorsConsumed);
    }

    /// <summary>Entering a field without an array index leaves the selected-index list unchanged.</summary>
    [TestMethod]
    public void EnterField_WithoutASelectedIndex_DoesNotAppendToSelectedIndexes()
    {
        var context = new TargetResolutionContext(debugPrefix: [], selectedIndexes: [7,]);

        TargetResolutionContext next = context.EnterField("child", selectedIndexes: []);

        CollectionAssert.AreEqual(new[] { 7, }, next.SelectedIndexes.ToArray());
    }

    /// <summary>Following a pointer records its target address and increments the consumed count.</summary>
    [TestMethod]
    public void FollowPointer_RecordsTargetAndIncrementsConsumedCount()
    {
        var context = new TargetResolutionContext(debugPrefix: [], selectedIndexes: [], pointerAccessorsConsumed: 2);

        TargetResolutionContext next = context.FollowPointer(targetAddress: 200);

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

        Assert.Throws<OverflowException>(() => context.FollowPointer(targetAddress: 0));
    }

    /// <summary>Creates a field target with the given array selection and pointer traversal count.</summary>
    private static ResolvedTarget MakeTarget(int? selectedArrayIndex = null, int pointerAccessorsConsumed = 0)
    {
        var context = new TargetResolutionContext([], [], pointerAccessorsConsumed: pointerAccessorsConsumed);
        return ResolvedTarget.Field(Value(), 0, new ArraySelection(selectedArrayIndex.HasValue, null, selectedArrayIndex), 0, 0, 0, context);
    }

    /// <summary>Returns the compiled <c>value</c> field of a one-field struct.</summary>
    private static CompiledField Value()
    {
        var layout = new CStruct("struct root { uint32 value; };");
        return ((CompiledCompositeType)layout.CompiledModel.Composites[layout.GetStruct("root")].Definition!).Fields[0];
    }
}
