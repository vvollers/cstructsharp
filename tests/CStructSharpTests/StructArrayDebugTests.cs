namespace CStructSharpTests;

using CStructSharp;

/// <summary>Composite array debug paths identify the exact element behind each byte range.</summary>
[TestClass]
public class StructArrayDebugTests
{
    /// <summary>Runtime outer counts and fixed inner dimensions retain all coordinates.</summary>
    [TestMethod]
    public void NestedArrays_HaveDistinctPathsAndRanges()
    {
        const string layout = """
                              struct cell { uint16 val; };
                              struct row { cell cells[2][2]; };
                              struct root { uint8 count; row data[count]; };
                              """;
        byte[] bytes = new byte[17];
        bytes[0] = 2;
        var parser = new CStruct(layout, aligned: false);
        using var stream = new MemoryStream(bytes);
        (List<DebugData> debug, _) = parser.ParseStreamWithDebug(stream, "root");
        for (int i = 0; i < 8; i++)
        {
            string path = $"root.data[{i / 4}].cells[{(i % 4) / 2}][{i % 2}].val";
            DebugData entry = debug.Single(item => item.DebugStackString == path);
            Assert.AreEqual(1L + (i * 2), entry.CurPos);
            Assert.AreEqual(3L + (i * 2), entry.EndPos);
        }
    }

    /// <summary>Lazy paths remain valid after later reads and concurrent formatting.</summary>
    [TestMethod]
    public void RetainedDebugPaths_AreIndependentOfLaterOperations()
    {
        var parser = new CStruct("struct cell { uint8 value; }; struct root { cell cells[2][2]; };");
        (List<DebugData> retained, _) = parser.ParseStreamWithDebug(new MemoryStream(new byte[4]), "root");
        Parallel.For(0, 32, _ =>
        {
            parser.ParseStreamWithDebug(new MemoryStream(new byte[4]), "root");
            string[] paths = retained.Select(item => item.DebugStackString).ToArray();
            CollectionAssert.AreEqual(
                new[] { "root.cells[0][0].value", "root.cells[0][1].value", "root.cells[1][0].value", "root.cells[1][1].value" },
                paths);
        });
    }

    /// <summary>Array indices survive pointer dereferencing, including address-storage records.</summary>
    [TestMethod]
    public void StructPointerArray_IndexesTargetsAndAddresses()
    {
        const string layout = """
                              struct cell { uint16 val; };
                              struct root { cell *data[2]; };
                              """;
        var parser = new CStruct(layout, pointerSize: 1, aligned: false);
        using var stream = new MemoryStream(new byte[] { 2, 4, 11, 0, 22, 0 });
        (List<DebugData> debug, _) = parser.ParseStreamWithDebug(stream, "root");
        Assert.AreEqual(2L, debug.Single(item => item.DebugStackString == "root.data[0].val").CurPos);
        Assert.AreEqual(4L, debug.Single(item => item.DebugStackString == "root.data[1].val").CurPos);
        Assert.AreEqual(0L, debug.Single(item => item.DebugStackString == "root.data[0]").CurPos);
        Assert.AreEqual(1L, debug.Single(item => item.DebugStackString == "root.data[1]").CurPos);
    }
}
