namespace CStructSharp.Tests;

/// <summary>Locks one compiled composite extent across root, nested, array, pointer, and union entry points.</summary>
[TestClass]
public class CompiledExecutionParityTests
{
    /// <summary>Requires every composite read entry point to consume the selected type's complete compiled extent.</summary>
    [TestMethod]
    public void AlignedCompositeSelections_ConsumeTheCompleteCompiledExtent()
    {
        const string child = "struct child { uint32 value; byte tail; };";
        (string Name, CStruct Layout, byte[] Input, string Path, long ExpectedPosition)[] cases =
        [
            (
                "root",
                new CStruct(child, pointerSize: 1, aligned: true),
                new byte[8],
                "child",
                8),
            (
                "nested",
                new CStruct(
                    child + " struct root { byte prefix; child item; byte suffix; };",
                    pointerSize: 1,
                    aligned: true),
                new byte[16],
                "root.item",
                12),
            (
                "array-element",
                new CStruct(
                    child + " struct root { child items[2]; };",
                    pointerSize: 1,
                    aligned: true),
                new byte[16],
                "root.items[1]",
                16),
            (
                "pointer-target",
                new CStruct(
                    child + " struct root { child *item; };",
                    pointerSize: 1,
                    aligned: true),
                [4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,],
                "root.item.value",
                12),
            (
                "union",
                new CStruct(
                    child + " union choice { child item; uint64 wide; }; " +
                    "struct root { byte prefix; choice value; };",
                    pointerSize: 1,
                    aligned: true),
                new byte[16],
                "root.value",
                16),
        ];

        foreach ((string name, CStruct layout, byte[] input, string path, long expectedPosition) in cases)
        {
            using var parseStream = new MemoryStream(input, writable: false);
            _ = layout.ParseStream(parseStream, path);
            Assert.AreEqual(expectedPosition, parseStream.Position, name + "/parse");

            using var debugStream = new MemoryStream(input, writable: false);
            _ = layout.ParseStreamWithDebug(debugStream, path);
            Assert.AreEqual(expectedPosition, debugStream.Position, name + "/debug");

            using var valueStream = new MemoryStream(input, writable: false);
            _ = layout.ReadValue(valueStream, path);
            Assert.AreEqual(expectedPosition, valueStream.Position, name + "/read-value");
        }
    }

    /// <summary>Requires a struct union member to advance through its own fields after beginning at the union address.</summary>
    [TestMethod]
    public void UnionStructMember_UsesSequentialCompiledFieldTraversal()
    {
        var layout = new CStruct(
            "struct child { uint32 value; byte tail; }; " +
            "union choice { child item; uint64 wide; };",
            pointerSize: 1,
            aligned: true);
        byte[] input = [0x78, 0x56, 0x34, 0x12, 0xA5, 0, 0, 0,];

        using var parseStream = new MemoryStream(input, writable: false);
        var parsed = (UnionValue)layout.ParseStream(parseStream, "choice");
        AssertUnionChild(parsed, "parse");
        Assert.AreEqual(8L, parseStream.Position);

        using var debugStream = new MemoryStream(input, writable: false);
        (List<DebugData> _, dynamic debugResult) = layout.ParseStreamWithDebug(debugStream, "choice");
        AssertUnionChild((UnionValue)debugResult, "debug");
        Assert.AreEqual(8L, debugStream.Position);

        using var valueStream = new MemoryStream(input, writable: false);
        AssertUnionChild((UnionValue)layout.ReadValue(valueStream, "choice")!, "read-value");
        Assert.AreEqual(8L, valueStream.Position);
    }

    private static void AssertUnionChild(UnionValue union, string operation)
    {
        dynamic child = union.Members["item"]!;
        Assert.AreEqual(0x12345678U, (uint)child.value, operation + "/value");
        Assert.AreEqual((byte)0xA5, (byte)child.tail, operation + "/tail");
    }
}
