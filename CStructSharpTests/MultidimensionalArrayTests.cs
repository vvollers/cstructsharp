namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>
    ///     A two-dimensional primitive array reads into an N-deep nested list matching the declared shape
    ///     (<c>root.matrix</c> is a list of 3 rows, each a list of 4 columns), and elements are read in row-major
    ///     order - the same sequential byte order a flat array of the same total count would use.
    /// </summary>
    [TestMethod]
    public void TwoDimensionalArray_ParsesIntoANestedListInRowMajorOrder()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        byte[] bytes = [.. Enumerable.Range(0, 12).Select(i => (byte)i),];
        using var stream = new MemoryStream(bytes);

        dynamic result = cstruct.ParseStream(stream, "root");
        List<object?> matrix = (List<object?>)result.matrix;

        Assert.AreEqual(3, matrix.Count);
        for (int row = 0; row < 3; row++)
        {
            var rowValues = (List<object?>)matrix[row]!;
            Assert.AreEqual(4, rowValues.Count);
            for (int column = 0; column < 4; column++)
            {
                Assert.AreEqual((byte)((row * 4) + column), rowValues[column]);
            }
        }
    }

    /// <summary>A three-dimensional primitive array nests three levels deep, again in row-major order.</summary>
    [TestMethod]
    public void ThreeDimensionalArray_ParsesIntoAThreeLevelNestedList()
    {
        var cstruct = new CStruct("struct root { uint8 cube[2][3][4]; };", pointerSize: 1, aligned: false);
        byte[] bytes = [.. Enumerable.Range(0, 24).Select(i => (byte)i),];
        using var stream = new MemoryStream(bytes);

        dynamic result = cstruct.ParseStream(stream, "root");
        List<object?> cube = (List<object?>)result.cube;

        Assert.AreEqual(2, cube.Count);
        int expected = 0;
        for (int i = 0; i < 2; i++)
        {
            var plane = (List<object?>)cube[i]!;
            Assert.AreEqual(3, plane.Count);
            for (int j = 0; j < 3; j++)
            {
                var row = (List<object?>)plane[j]!;
                Assert.AreEqual(4, row.Count);
                for (int k = 0; k < 4; k++)
                {
                    Assert.AreEqual((byte)expected, row[k]);
                    expected++;
                }
            }
        }
    }

    /// <summary>A two-dimensional array of structs nests one list level per array dimension around each struct.</summary>
    [TestMethod]
    public void TwoDimensionalStructArray_ParsesIntoANestedListOfStructs()
    {
        const string layout = """
                              struct row { uint8 a; uint8 b; };
                              struct root { row grid[2][3]; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, aligned: false);
        byte[] bytes = [.. Enumerable.Range(0, 12).Select(i => (byte)i),];
        using var stream = new MemoryStream(bytes);

        dynamic result = cstruct.ParseStream(stream, "root");
        List<object?> grid = (List<object?>)result.grid;

        Assert.AreEqual(2, grid.Count);
        int expected = 0;
        for (int i = 0; i < 2; i++)
        {
            var rowOfCells = (List<object?>)grid[i]!;
            Assert.AreEqual(3, rowOfCells.Count);
            for (int j = 0; j < 3; j++)
            {
                dynamic cell = rowOfCells[j]!;
                Assert.AreEqual((byte)expected, cell.a);
                expected++;
                Assert.AreEqual((byte)expected, cell.b);
                expected++;
            }
        }
    }

    /// <summary>
    ///     A fixed table of fixed-width strings (<c>char names[10][32];</c>) reads each row exactly like today's
    ///     existing <c>char[32]</c> fixed buffer (a single string, NUL padding included up to the fixed width),
    ///     with only the outer dimension nesting into a list.
    /// </summary>
    [TestMethod]
    public void FixedStringTable_ParsesEachRowAsAStringAndNestsTheOuterDimension()
    {
        var cstruct = new CStruct("struct root { char names[3][4]; };", pointerSize: 1, aligned: false);
        byte[] bytes = "abc\0defgijkl"u8.ToArray();
        using var stream = new MemoryStream(bytes);

        dynamic result = cstruct.ParseStream(stream, "root");
        List<object?> names = (List<object?>)result.names;

        Assert.AreEqual(3, names.Count);
        Assert.AreEqual("abc\0", names[0]);
        Assert.AreEqual("defg", names[1]);
        Assert.AreEqual("ijkl", names[2]);
    }
}
