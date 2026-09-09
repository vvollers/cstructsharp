namespace CStructSharp.Tests;

using System.IO;
using CStructSharp.Structure;

/// <summary>
///     Verifies multidimensional arrays (LANG-05, <c>value[rows][columns]</c>), following
///     <c>docs/adr/0016-multidimensional-arrays.md</c>'s fixed-dimensions-only first slice.
/// </summary>
[TestClass]
public class MultidimensionalArrayTests
{
    /// <summary>
    ///     Multiple indices in one path segment (<c>root.matrix[2][3]</c>) already parse successfully (LANG-05's
    ///     path grammar accepts repeated brackets), but real per-dimension address resolution has not landed yet
    ///     in this seam - resolving such a path must fail with a distinct, clear message, not a misleading one,
    ///     and not silently resolve to the wrong element.
    /// </summary>
    [TestMethod]
    public void MultipleIndicesInOneSegment_IsRejectedWithADistinctMessageUntilRealSupportLands()
    {
        var cstruct = new CStruct("struct root { uint8 values[4]; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[4]);

        CStructPathException exception = Assert.Throws<CStructPathException>(
            () => cstruct.ResolveAddress(stream, "root.values[0][1]"));

        StringAssert.Contains(exception.Message, "multidimensional");
    }

    /// <summary>The same temporary rejection applies to a selected-path write (<c>WriteStream</c>/<c>UpdateStream</c>).</summary>
    [TestMethod]
    public void MultipleIndicesInOneSegment_IsRejectedForSelectedPathWrites()
    {
        var cstruct = new CStruct("struct root { uint8 values[4]; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[4]);

        Assert.Throws<CStructPathException>(
            () => cstruct.UpdateStream(stream, "root.values[0][1]", (byte)9));
    }

    /// <summary>
    ///     A two-dimensional fixed array declares and compiles correctly - the outermost dimension's own count
    ///     (via <see cref="CompiledField.FixedArrayCount"/>) and the total element count both report correctly,
    ///     and a sibling field placed after the array lands at exactly <c>rows * columns</c> bytes, proving the
    ///     total-size math (seam 2) is wired end to end through real declaration/compilation, not just reachable
    ///     via a hand-built shape.
    /// </summary>
    [TestMethod]
    public void TwoDimensionalArray_DeclaresAndCompilesWithTheCorrectTotalSize()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; uint8 tail; };", pointerSize: 1, aligned: false);

        Assert.AreEqual(13, cstruct.GetStructSizeInBytes("root"));

        Struct root = cstruct.GetStruct("root");
        var compiledRoot = (CompiledCompositeType)cstruct.CompiledModel.Composites[root].Definition!;
        CompiledField matrixField = compiledRoot.Fields[0];
        Assert.AreEqual(2, matrixField.Array.Dimensions.Length);
        Assert.AreEqual(3, matrixField.FixedArrayCount);
        Assert.AreEqual(12, matrixField.Array.TotalFixedElementCount);
        Assert.AreEqual(12, matrixField.FixedStorageSize);
    }

    /// <summary>A three-dimensional fixed array declares and compiles with the product of all three dimensions.</summary>
    [TestMethod]
    public void ThreeDimensionalArray_DeclaresAndCompilesWithTheCorrectTotalSize()
    {
        var cstruct = new CStruct("struct root { uint8 cube[2][3][4]; uint8 tail; };", pointerSize: 1, aligned: false);

        Assert.AreEqual(25, cstruct.GetStructSizeInBytes("root"));

        Struct root = cstruct.GetStruct("root");
        var compiledRoot = (CompiledCompositeType)cstruct.CompiledModel.Composites[root].Definition!;
        CompiledField cubeField = compiledRoot.Fields[0];
        Assert.AreEqual(3, cubeField.Array.Dimensions.Length);
        Assert.AreEqual(24, cubeField.Array.TotalFixedElementCount);
    }

    /// <summary>A two-dimensional array of structs multiplies both dimensions by the struct's own compiled size.</summary>
    [TestMethod]
    public void TwoDimensionalStructArray_DeclaresAndCompilesWithTheCorrectTotalSize()
    {
        const string layout = """
                              struct row { uint8 a; uint8 b; };
                              struct root { row grid[2][3]; uint8 tail; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, aligned: false);

        Assert.AreEqual(2, cstruct.GetStructSizeInBytes("row"));
        Assert.AreEqual(13, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>
    ///     A fixed table of fixed-width strings (<c>char names[10][32];</c>) - a common real-world C idiom -
    ///     declares and compiles with the innermost dimension behaving exactly like today's existing
    ///     <c>char[32]</c> fixed-buffer field, and the outer dimension as an ordinary array-of-buffer dimension.
    /// </summary>
    [TestMethod]
    public void FixedStringTable_DeclaresAndCompilesWithTheCorrectTotalSize()
    {
        var cstruct = new CStruct("struct root { char names[10][32]; uint8 tail; };", pointerSize: 1, aligned: false);

        Assert.AreEqual(321, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>
    ///     Only the outermost dimension of a multidimensional array may ever be runtime-expression-sized
    ///     (ADR-016 decision 2), and this fixed-dimensions-only slice does not implement even that yet - a
    ///     runtime-sized dimension anywhere in a two-or-more-dimension declaration is rejected with a distinct,
    ///     clear message, not silently mishandled.
    /// </summary>
    [TestMethod]
    public void RuntimeSizedDimension_IsRejectedInAMultidimensionalArray()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { uint8 count; uint8 values[count][4]; };", pointerSize: 1));

        StringAssert.Contains(exception.Message, "compile-time-fixed");
    }

    /// <summary>
    ///     An unsized dimension (<c>char names[10][];</c>) is permanently restricted to being the sole dimension
    ///     of a one-dimensional array (ADR-016 decision 6) - rejected as an inner dimension of a multidimensional
    ///     declaration, not silently treated as some other shape.
    /// </summary>
    [TestMethod]
    public void UnsizedInnerDimension_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { char names[10][]; };", pointerSize: 1));
    }
}
