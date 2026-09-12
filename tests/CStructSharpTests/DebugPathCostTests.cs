namespace CStructSharpTests;

using CStructSharp;

/// <summary>Protects bounded path construction and allocation-free reuse of formatted debug paths.</summary>
[TestClass]
public class DebugPathCostTests
{
    /// <summary>Consumers may revisit a debug path many times without formatting it again.</summary>
    [TestMethod]
    public void RepeatedDebugPathAccess_DoesNotAllocate()
    {
        var layout = new CStruct("struct cell { uint8 value; }; struct root { cell cells[2]; };");
        (List<DebugData> debug, _) = layout.ParseStreamWithDebug(new MemoryStream(new byte[2]), "root");
        DebugData item = debug[1];
        Assert.AreEqual("root.cells[1].value", item.DebugStackString);
        for (int index = 0; index < 1000; index++)
        {
            GC.KeepAlive(item.DebugStackString);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            GC.KeepAlive(item.DebugStackString);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(0L, allocated);
    }

    /// <summary>Repeated shared segments can exceed Int32 path length without allocating a huge string.</summary>
    [TestMethod]
    public void PathLengthOverflow_IsRejectedBeforeFormatting()
    {
        string segment = new('x', 1024 * 1024);
        DebugPath? path = null;
        for (int index = 0; index < 2047; index++)
        {
            path = new DebugPath(path, segment);
        }

        Assert.Throws<OverflowException>(() => new DebugPath(path, segment));
    }
}
