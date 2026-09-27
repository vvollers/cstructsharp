namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks that invalid nested write paths fail before changing destination bytes or position.</summary>
[TestClass]
public class WritePathFailureTests
{
    /// <summary>Path resolution identifies the invalid traversal without attempting to encode the supplied value.</summary>
    /// <param name="path">The invalid path from the root.</param>
    /// <param name="diagnostic">The focused explanation expected from layout resolution.</param>
    [TestMethod]
    [DataRow("root.scalar[0]", "not an indexable fixed array")]
    [DataRow("root.grid[0][0][0]", "Too many array indices for grid: expected at most 2, got 3")]
    [DataRow("root.grid[2]", "out of range")]
    [DataRow("root.items.value", "An array index is required")]
    [DataRow("root.pointer.value", "Write cannot dereference pointer targets")]
    [DataRow("root.scalar.value", "Cannot traverse through scalar field")]
    public void InvalidNestedPath_LeavesDestinationUntouched(string path, string diagnostic)
    {
        var layout = new CStruct("struct leaf { uint8 value; }; struct root { uint8 scalar; uint8 grid[2][2]; leaf items[2]; leaf *pointer; };");
        byte[] bytes = [11, 22, 33, 44];
        using var destination = new MemoryStream((byte[])bytes.Clone(), writable: true);
        destination.Position = 1;

        // A scalar input avoids data-object traversal: the layout itself must reject the selected path.
        CStructPathException failure = Assert.Throws<CStructPathException>(() => layout.Write(destination, path, (byte)9));
        StringAssert.Contains(failure.Message, diagnostic);
        Assert.AreEqual(1L, destination.Position);
        CollectionAssert.AreEqual(bytes, destination.ToArray());
    }

    /// <summary>Selected writes resolve both named and inline struct aliases before looking up their members.</summary>
    /// <param name="definition">A layout exporting a struct alias named alias.</param>
    [TestMethod]
    [DataRow("struct item { uint8 value; }; typedef item alias;")]
    [DataRow("typedef struct { uint8 value; } alias;")]
    public void StructAlias_ResolvesSelectedField(string definition)
    {
        var layout = new CStruct(definition);
        using var destination = new MemoryStream();
        layout.Write(destination, "alias.value", (byte)37);
        CollectionAssert.AreEqual(new byte[] { 37, }, destination.ToArray());
        Assert.AreEqual(1L, destination.Position);
    }

    /// <summary>Enum and primitive roots explain why a member path cannot be resolved without touching output.</summary>
    /// <param name="definition">A scalar declaration exported as scalar.</param>
    [TestMethod]
    [DataRow("enum scalar : uint8 { ONE = 1 };")]
    [DataRow("typedef uint8 scalar;")]
    public void ScalarRoot_RejectsMemberPathWithContext(string definition)
    {
        var layout = new CStruct(definition);
        using var destination = new MemoryStream(new byte[] { 11, 12, });
        destination.Position = 1;

        // Shape resolution must reject the member before any writer can consume the scalar input.
        CStructPathException failure = Assert.Throws<CStructPathException>(() => layout.Write(destination, "scalar.value", (byte)37));
        StringAssert.Contains(failure.Message, "Cannot resolve path segment: value");
        Assert.AreEqual(1L, destination.Position);
        CollectionAssert.AreEqual(new byte[] { 11, 12, }, destination.ToArray());
    }
}
