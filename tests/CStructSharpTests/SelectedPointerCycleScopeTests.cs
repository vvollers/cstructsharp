namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks selected-path traversal does not leave stale active-cycle entries in the decoder.</summary>
[TestClass]
public class SelectedPointerCycleScopeTests
{
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
