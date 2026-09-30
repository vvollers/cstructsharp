namespace CStructSharp.Tests;

using CStructSharp.Addressing;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Reading;

/// <summary>
///     Exercises the compiled engine's path resolver (<see cref="TargetResolver"/>) and the <see cref="ResolvedPath"/> it
///     returns directly: the kind of target, the field it describes, the array selection, the pointers followed, the
///     nesting depth above the target, and the debug names a debug parse records the target under.
/// </summary>
[TestClass]
public class ResolvedPathTests
{
    /// <summary>
    ///     A layout with a nested struct, an array, and a pointer to a struct. Its input: <c>a = 1</c>, <c>items = 2, 3</c>,
    ///     <c>inner.b = 4</c>, then a one-byte pointer (pointer size 1) to byte 6, where the pointed-to <c>inner</c> holds 9.
    /// </summary>
    private const string Layout = "struct inner { uint8 b; }; struct root { uint8 a; uint8 items[2]; inner nested; inner* p; uint8 tail; };";

    /// <summary>The input bytes of <see cref="Layout"/>.</summary>
    private static readonly byte[] Data = [1, 2, 3, 4, 6, 0, 9];

    /// <summary>A selected array index makes <see cref="ResolvedPath.SelectsArrayElement"/> true; its absence, false.</summary>
    [TestMethod]
    public void SelectsArrayElement_ReflectsWhetherAnIndexWasSelected()
    {
        ResolvedPath element = Resolve("root.items[1]");
        Assert.IsTrue(element.SelectsArrayElement);
        Assert.AreEqual(1, element.SelectedArrayIndex);
        Assert.AreEqual(2L, element.Address);

        ResolvedPath array = Resolve("root.items");
        Assert.IsFalse(array.SelectsArrayElement);
        Assert.IsTrue(array.IsArray);
        Assert.AreEqual(2, array.ArrayLength);
    }

    /// <summary>A <c>.value</c> accessor counts as a consumed pointer accessor; a path without one consumes none.</summary>
    [TestMethod]
    public void PointerAccessors_CountTheFollowedPointers()
    {
        Assert.AreEqual(1, Resolve("root.p.value").PointerAccessorsConsumed);
        Assert.AreEqual(0, Resolve("root.nested.b").PointerAccessorsConsumed);
        Assert.IsNull(Resolve("root.nested.b").PointerTargetAddress);
    }

    /// <summary>
    ///     Each kind of target records its kind and the field that describes it: a field, an array element, a pointer's
    ///     stored address (with the pointer as its field), and a pointer's target (with the address it stored and the
    ///     composite it points to); a member of a nested struct is one level deeper than a member of the root.
    /// </summary>
    [TestMethod]
    public void Kinds_RecordTheirFieldsAndDepth()
    {
        ResolvedPath field = Resolve("root.a");
        Assert.AreEqual(ResolvedTargetKind.Field, field.Kind);
        Assert.AreEqual("a", field.Effective!.Name);
        Assert.AreEqual(1, field.ContainingStructureDepth);

        ResolvedPath nested = Resolve("root.nested.b");
        Assert.AreEqual(ResolvedTargetKind.Field, nested.Kind);
        Assert.AreEqual(3L, nested.Address);
        Assert.AreEqual(2, nested.ContainingStructureDepth);

        ResolvedPath element = Resolve("root.items[0]");
        Assert.AreEqual(ResolvedTargetKind.ArrayElement, element.Kind);
        Assert.AreEqual("items", element.Declared!.Name);
        Assert.AreEqual(1, element.Indexes);

        ResolvedPath address = Resolve("root.p.address");
        Assert.AreEqual(ResolvedTargetKind.PointerAddress, address.Kind);
        Assert.AreEqual("p", address.Effective!.Name);
        Assert.AreEqual(4L, address.Address);

        ResolvedPath pointed = Resolve("root.p.value");
        Assert.AreEqual(ResolvedTargetKind.PointerValue, pointed.Kind);
        Assert.AreEqual(6L, pointed.Address);
        Assert.AreEqual(6L, pointed.PointerTargetAddress);
        Assert.AreEqual("inner", pointed.TargetComposite!.Name);
    }

    /// <summary>
    ///     The debug names of a path are its struct and field segments in order - the root's struct, then each member the
    ///     path enters - and a resolution appends to the caller's list only.
    /// </summary>
    [TestMethod]
    public void DebugNames_AppendTheSegmentsInOrder()
    {
        var names = new List<string>();
        _ = Resolve("root.nested", names);
        CollectionAssert.AreEqual(new[] { "root", "nested", }, names);

        var others = new List<string>();
        _ = Resolve("root.items[1]", others);
        CollectionAssert.AreEqual(new[] { "root", "nested", }, names, "a later resolution leaves an earlier list alone");
        CollectionAssert.AreEqual(new[] { "root", "items", }, others);
    }

    /// <summary>Resolves a path of <see cref="Layout"/> over <see cref="Data"/> with the engine's resolver.</summary>
    /// <param name="path">The path.</param>
    /// <param name="debugNames">The list that receives the path's debug names, or <see langword="null"/>.</param>
    /// <returns>The resolved target.</returns>
    private static unsafe ResolvedPath Resolve(string path, List<string>? debugNames = null)
    {
        var layout = new CStruct(Layout, pointerSize: 1);
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(null);
        VariableSlots slots = VariableSlots.Create(layout.Compilation.SlotTable, LayoutVariableInput.FromIntegers(null));
        var state = new ReadEngineState(layout, slots, settings, null);
        try
        {
            fixed (byte* region = Data)
            {
                var cursor = new MemoryReadCursor(region, Data.Length, 0, settings.MaxStringBytes, settings.MaxTotalBytesRead, default);
                return TargetResolver.Resolve(ref cursor, ref state, layout.ParsePath(path), debugNames, readsTarget: false);
            }
        }
        finally
        {
            state.Release();
            slots.Dispose();
        }
    }
}
