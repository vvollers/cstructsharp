namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Checks selected pointer reads: they keep the depth of the path that reached their storage, and a selected
///     subtree leaves no stale active-cycle entries behind.
/// </summary>
[TestClass]
public class SelectedPointerTests
{
    /// <summary>The selected pointer would be a second dereference after the explicit parent accessor.</summary>
    /// <param name="array">Whether the selected pointer is an indexed array element.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectedNestedPointer_IncludesTheExplicitParentInItsDepthLimit(bool array)
    {
        string dimension = array ? "[1]" : string.Empty;
        string index = array ? "[0]" : string.Empty;
        var layout = new CStruct("struct node { uint8 *next" + dimension + "; }; struct root { node *head; };", pointerSize: 1);
        byte[] source = [1, 2, 9,];

        // The .value accessor has already consumed the one allowed pointer level.
        Assert.Throws<CStructReadLimitException>(() => layout.ReadValue(source.AsSpan(), "root.head.value.next" + index, options: new ReadOptions { MaxPointerDepth = 1, }));
    }

    /// <summary>A budget of two permits the explicit parent dereference and the selected pointer's target read.</summary>
    /// <param name="array">Whether the selected pointer is an indexed array element.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectedNestedPointer_ReadsTheTargetWithinItsCombinedDepthBudget(bool array)
    {
        string dimension = array ? "[1]" : string.Empty;
        string index = array ? "[0]" : string.Empty;
        var layout = new CStruct("struct node { uint8 *next" + dimension + "; }; struct root { node *head; };", pointerSize: 1);
        byte[] source = [1, 2, 9,];

        var pointer = (Pointer)layout.ReadValue(source.AsSpan(), "root.head.value.next" + index, options: new ReadOptions { MaxPointerDepth = 2, })!;

        Assert.IsTrue(pointer.IsDereferenced);
        Assert.AreEqual(2L, pointer.Address);
        Assert.AreEqual((byte)9, pointer.Value);
    }

    /// <summary>A selected subtree starts its own active branch while retaining the path's consumed depth budget.</summary>
    [TestMethod]
    public void SelectedSubtree_ReportsItsDepthLimitBeforeASecondCycle()
    {
        var layout = new CStruct("struct node { node *next; }; struct root { node *first; };", pointerSize: 1);
        using var source = new MemoryStream(new byte[] { 1, 1, });

        // One path accessor and one decoded next pointer exhaust the limit; path bookkeeping is no longer active.
        Assert.Throws<CStructReadLimitException>(() =>
            layout.ReadValue(source, "root.first.value", options: new ReadOptions { MaxPointerDepth = 2, }));
    }
}
