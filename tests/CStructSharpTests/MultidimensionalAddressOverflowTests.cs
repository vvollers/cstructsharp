namespace CStructSharp.Tests;

/// <summary>Checks selected multidimensional paths reject an overflowing leaf index before traversal.</summary>
[TestClass]
public class MultidimensionalAddressOverflowTests
{
    /// <summary>Skipping rows of variable-size elements must use checked multiplication.</summary>
    /// <param name="composite">Whether the leaf is a runtime-sized record rather than a variable-width integer.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectedRow_RejectsOverflowWithoutReading(bool composite)
    {
        string element = composite ? "item" : "uleb128_32";
        var layout = new CStruct("struct item { uint8 count; uint8 values[count]; }; struct root { " + element + " rows[3][1073741824]; };");
        using var source = new MemoryStream(new byte[] { 0, });
        var options = new ReadOptions { MaxArrayElements = int.MaxValue, };
        Assert.Throws<OverflowException>(() => layout.ResolveAddress(source, "root.rows[2]", options: options));
        Assert.AreEqual(0L, source.Position);
    }
}
