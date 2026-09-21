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
    [DataRow("root.grid[0][0][0]", "Too many array indices")]
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
}
