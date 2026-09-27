namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks complete-array selections and runtime count failures in standalone writes.</summary>
[TestClass]
public class WritePathBoundaryTests
{
    /// <summary>Selecting an entire array needs no index and writes at the caller's current destination position.</summary>
    [TestMethod]
    public void SelectedArray_WritesTheWholeValueWithoutAnIndex()
    {
        var layout = new CStruct("struct root { uint8 prefix; uint8 values[2]; };");
        using var destination = new MemoryStream(new byte[] { 9, 0, 0, 8, });
        destination.Position = 1;

        layout.Write(destination, "root.values", new byte[] { 11, 12, });

        CollectionAssert.AreEqual(new byte[] { 9, 11, 12, 8, }, destination.ToArray());
        Assert.AreEqual(3L, destination.Position);
    }

    /// <summary>A negative caller count is a runtime sizing failure while resolving a selected array element.</summary>
    [TestMethod]
    public void SelectedIndex_PreservesRuntimeCountFailureKind()
    {
        var layout = new CStruct("#define COUNT 2\nstruct root { uint8 values[COUNT]; };");
        var variables = new Dictionary<string, int> { ["COUNT"] = -1, };

        // The declaration compiled with a valid count; the caller's later override fails during path resolution.
        CStructReadException failure = Assert.Throws<CStructReadException>(() => layout.Serialize("root.values[0]", (byte)7, variables));
        StringAssert.Contains(failure.Message, "Array length cannot be negative: values");
    }
}
