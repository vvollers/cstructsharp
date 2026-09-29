namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>Protects bounded path construction and allocation-free reuse of formatted debug paths.</summary>
[TestClass]
public class DebugPathCostTests
{
    /// <summary>
    ///     Consumers may revisit a debug path many times without formatting it again, whether the interpreter or the
    ///     compiled engine recorded it.
    /// </summary>
    /// <param name="engine">Whether the compiled engine must run the debug parse; otherwise the interpreter runs it.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void RepeatedDebugPathAccess_DoesNotAllocate(bool engine)
    {
        var layout = new CStruct("struct cell { uint8 value; }; struct root { cell cells[2]; };");
        ReadOptions options = engine ? EngineSelections.EngineRequired() : EngineSelections.InterpreterOnly();
        (_, IReadOnlyList<DebugData> debug) = layout.ParseWithDebug(new MemoryStream(new byte[2]), "root", options: options);
        DebugData item = debug[1];
        Assert.AreEqual("root.cells[1].value", item.Path);
        for (int index = 0; index < 1000; index++)
        {
            GC.KeepAlive(item.Path);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            GC.KeepAlive(item.Path);
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
