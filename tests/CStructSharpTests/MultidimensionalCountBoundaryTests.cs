namespace CStructSharp.Tests;

/// <summary>Checks element-count arithmetic when selected reads must skip variable-sized multidimensional records.</summary>
[TestClass]
public class MultidimensionalCountBoundaryTests
{
    /// <summary>An unrepresentable count fails before traversal instead of wrapping to zero and exposing the wrong trailing byte.</summary>
    [TestMethod]
    public void SelectedTrailingField_RejectsOverflowingPrecedingElementCount()
    {
        var layout = new CStruct("struct child { uint8 count; uint8 data[count]; }; struct root { child values[65536][65536]; uint8 tail; };");
        using var input = new MemoryStream(new byte[] { 55, });

        Assert.Throws<OverflowException>(() => layout.ReadValue<byte>(input, "root.tail"));
        Assert.AreEqual(0L, input.Position);
    }
}
