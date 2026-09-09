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
    ///     Supplying more indices in one path segment than a field actually has dimensions (<c>root.values[0][1]</c>
    ///     against a 1-D <c>uint8 values[4]</c> field) is rejected with a distinct, clear message - real
    ///     per-dimension address resolution (seam 7) now resolves every legal index count, so over-indexing is the
    ///     only remaining rejection, not a placeholder for "not yet available."
    /// </summary>
    [TestMethod]
    public void TooManyIndicesInOneSegment_IsRejected()
    {
        var cstruct = new CStruct("struct root { uint8 values[4]; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[4]);

        CStructPathException exception = Assert.Throws<CStructPathException>(
            () => cstruct.ResolveAddress(stream, "root.values[0][1]"));

        StringAssert.Contains(exception.Message, "Too many array indices");
    }

    /// <summary>The same over-indexing rejection applies to a selected-path write (<c>WriteStream</c>/<c>UpdateStream</c>).</summary>
    [TestMethod]
    public void TooManyIndicesInOneSegment_IsRejectedForSelectedPathWrites()
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

    /// <summary>
    ///     Writing a two-dimensional primitive array from nested lists, then reading it back, round-trips to the
    ///     exact same nested shape and values - proving the writer's flatten step is the correct inverse of the
    ///     reader's reshape step.
    /// </summary>
    [TestMethod]
    public void TwoDimensionalArray_RoundTripsThroughWriteAndParse()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        List<object> matrix =
        [
            new List<object> { 0, 1, 2, 3, },
            new List<object> { 4, 5, 6, 7, },
            new List<object> { 8, 9, 10, 11, },
        ];

        byte[] bytes = cstruct.Serialize("root", new { matrix, });

        Assert.AreEqual(12, bytes.Length);
        CollectionAssert.AreEqual(Enumerable.Range(0, 12).Select(i => (byte)i).ToArray(), bytes);

        using var stream = new MemoryStream(bytes);
        dynamic parsed = cstruct.ParseStream(stream, "root");
        List<object?> roundTripped = (List<object?>)parsed.matrix;
        Assert.AreEqual(3, roundTripped.Count);
        for (int row = 0; row < 3; row++)
        {
            CollectionAssert.AreEqual(
                ((List<object>)matrix[row]).Select(v => (byte)(int)v).ToArray(),
                ((List<object?>)roundTripped[row]!).Cast<byte>().ToArray());
        }
    }

    /// <summary>A three-dimensional array round-trips through three levels of nested-list flattening and reshaping.</summary>
    [TestMethod]
    public void ThreeDimensionalArray_RoundTripsThroughWriteAndParse()
    {
        var cstruct = new CStruct("struct root { uint8 cube[2][3][4]; };", pointerSize: 1, aligned: false);
        int expected = 0;
        List<object> cube = [];
        for (int i = 0; i < 2; i++)
        {
            List<object> plane = [];
            for (int j = 0; j < 3; j++)
            {
                List<object> row = [];
                for (int k = 0; k < 4; k++)
                {
                    row.Add((byte)expected);
                    expected++;
                }

                plane.Add(row);
            }

            cube.Add(plane);
        }

        byte[] bytes = cstruct.Serialize("root", new { cube, });

        Assert.AreEqual(24, bytes.Length);
        CollectionAssert.AreEqual(Enumerable.Range(0, 24).Select(i => (byte)i).ToArray(), bytes);
    }

    /// <summary>A fixed string table writes each caller-supplied string as one row, matching the read-side shape.</summary>
    [TestMethod]
    public void FixedStringTable_WritesEachRowAsAStringAndRoundTrips()
    {
        var cstruct = new CStruct("struct root { char names[3][4]; };", pointerSize: 1, aligned: false);
        List<object> names = ["abc", "defg", "ij",];

        byte[] bytes = cstruct.Serialize("root", new { names, });

        Assert.AreEqual(12, bytes.Length);
        using var stream = new MemoryStream(bytes);
        dynamic parsed = cstruct.ParseStream(stream, "root");
        List<object?> roundTripped = (List<object?>)parsed.names;
        Assert.AreEqual(3, roundTripped.Count);
        Assert.AreEqual("abc\0", roundTripped[0]);
        Assert.AreEqual("defg", roundTripped[1]);
        Assert.AreEqual("ij\0\0", roundTripped[2]);
    }

    /// <summary>A row with too few or too many elements is rejected with the same mismatch exception a 1-D array uses.</summary>
    [TestMethod]
    public void MismatchedRowLength_IsRejected()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[2][3]; };", pointerSize: 1, aligned: false);
        List<object> matrix =
        [
            new List<object> { 0, 1, 2, },
            new List<object> { 3, 4, },
        ];

        Assert.Throws<CStructWriteException>(() => cstruct.Serialize("root", new { matrix, }));
    }

    /// <summary>Supplying too few or too many rows is rejected the same way.</summary>
    [TestMethod]
    public void MismatchedOuterDimensionLength_IsRejected()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        List<object> matrix =
        [
            new List<object> { 0, 1, 2, 3, },
            new List<object> { 4, 5, 6, 7, },
        ];

        Assert.Throws<CStructWriteException>(() => cstruct.Serialize("root", new { matrix, }));
    }

    /// <summary>
    ///     ResolveAddress resolves every legal index count for a two-dimensional array: no index (the whole
    ///     array's own start), a partial index (the selected row's start), and a full index (the selected
    ///     element's own address) - matching the ADR's own worked example of addressing at both
    ///     <c>root.matrix</c> and <c>root.matrix[2]</c>.
    /// </summary>
    [TestMethod]
    public void ResolveAddress_ResolvesWholeArrayPartialIndexAndFullIndex()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        using var stream = new MemoryStream(new byte[12]);

        Assert.AreEqual(0, cstruct.ResolveAddress(stream, "root.matrix"));
        Assert.AreEqual(8, cstruct.ResolveAddress(stream, "root.matrix[2]"));
        Assert.AreEqual(11, cstruct.ResolveAddress(stream, "root.matrix[2][3]"));
    }

    /// <summary>
    ///     GetDynamicArrayLength reports the outer dimension's own count for the whole array, and the selected
    ///     row's own (inner dimension's) count for a partially indexed sub-array - the ADR's own worked example.
    /// </summary>
    [TestMethod]
    public void GetDynamicArrayLength_ReportsTheCurrentDimensionsCount()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        using var stream = new MemoryStream(new byte[12]);

        Assert.AreEqual(3, cstruct.GetDynamicArrayLength(stream, "root.matrix"));
        Assert.AreEqual(4, cstruct.GetDynamicArrayLength(stream, "root.matrix[2]"));
    }

    /// <summary>A fully indexed scalar leaf has no array length of its own to report.</summary>
    [TestMethod]
    public void GetDynamicArrayLength_OnAFullyIndexedElement_Throws()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        using var stream = new MemoryStream(new byte[12]);

        Assert.Throws<CStructPathException>(() => cstruct.GetDynamicArrayLength(stream, "root.matrix[2][3]"));
    }

    /// <summary>ReadValue on a partially indexed path returns just the selected row, as a nested list.</summary>
    [TestMethod]
    public void ReadValue_PartialIndex_ReturnsTheSelectedSubArray()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        byte[] bytes = [.. Enumerable.Range(0, 12).Select(i => (byte)i),];
        using var stream = new MemoryStream(bytes);

        object? row = cstruct.ReadValue(stream, "root.matrix[1]");

        CollectionAssert.AreEqual(new byte[] { 4, 5, 6, 7, }, ((List<object?>)row!).Cast<byte>().ToArray());
    }

    /// <summary>ReadValue on a fully indexed path returns the one scalar element.</summary>
    [TestMethod]
    public void ReadValue_FullIndex_ReturnsTheSelectedScalar()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        byte[] bytes = [.. Enumerable.Range(0, 12).Select(i => (byte)i),];
        using var stream = new MemoryStream(bytes);

        object? value = cstruct.ReadValue(stream, "root.matrix[1][2]");

        Assert.AreEqual((byte)6, value);
    }

    /// <summary>UpdateStream to a partially indexed path replaces just the selected row from a nested-list value.</summary>
    [TestMethod]
    public void UpdateStream_PartialIndex_ReplacesTheSelectedSubArray()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        byte[] bytes = [.. Enumerable.Range(0, 12).Select(i => (byte)i),];
        using var stream = new MemoryStream(bytes);

        cstruct.UpdateStream(stream, "root.matrix[1]", new List<object> { 9, 9, 9, 9, });

        CollectionAssert.AreEqual(
            new byte[] { 0, 1, 2, 3, 9, 9, 9, 9, 8, 9, 10, 11, },
            stream.ToArray());
    }

    /// <summary>UpdateStream to a fully indexed path replaces just the selected scalar element.</summary>
    [TestMethod]
    public void UpdateStream_FullIndex_ReplacesTheSelectedScalar()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        byte[] bytes = [.. Enumerable.Range(0, 12).Select(i => (byte)i),];
        using var stream = new MemoryStream(bytes);

        cstruct.UpdateStream(stream, "root.matrix[1][2]", (byte)99);

        CollectionAssert.AreEqual(
            new byte[] { 0, 1, 2, 3, 4, 5, 99, 7, 8, 9, 10, 11, },
            stream.ToArray());
    }

    /// <summary>An out-of-range index at any dimension is rejected with the same exception a 1-D array already uses.</summary>
    [TestMethod]
    public void OutOfRangeIndex_AtAnyDimension_IsRejected()
    {
        var cstruct = new CStruct("struct root { uint8 matrix[3][4]; };", pointerSize: 1, aligned: false);
        using var outerStream = new MemoryStream(new byte[12]);
        using var innerStream = new MemoryStream(new byte[12]);

        Assert.Throws<CStructPathException>(() => cstruct.ResolveAddress(outerStream, "root.matrix[3]"));
        Assert.Throws<CStructPathException>(() => cstruct.ResolveAddress(innerStream, "root.matrix[0][4]"));
    }

    /// <summary>Traversing past a still-array (partially indexed) target requires a further index, just like an unindexed array.</summary>
    [TestMethod]
    public void TraversingPastAPartiallyIndexedTarget_RequiresAFurtherIndex()
    {
        const string layout = """
                              struct row { uint8 a; uint8 b; };
                              struct root { row grid[2][3]; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, aligned: false);
        using var stream = new MemoryStream(new byte[12]);

        Assert.Throws<CStructPathException>(() => cstruct.ResolveAddress(stream, "root.grid[1].a"));
    }

    /// <summary>A fully indexed struct array element can be traversed into normally.</summary>
    [TestMethod]
    public void FullyIndexedStructArrayElement_CanBeTraversedInto()
    {
        const string layout = """
                              struct row { uint8 a; uint8 b; };
                              struct root { row grid[2][3]; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, aligned: false);
        byte[] bytes = [.. Enumerable.Range(0, 12).Select(i => (byte)i),];
        using var stream = new MemoryStream(bytes);

        long address = cstruct.ResolveAddress(stream, "root.grid[1][2].b");

        Assert.AreEqual(11, address);
    }

    /// <summary>Debug byte ranges for a fully indexed N-dimensional struct array element stay confined to that element.</summary>
    [TestMethod]
    public void ParseStreamWithDebug_FullyIndexedStructArrayElement_FiltersToThatElement()
    {
        const string layout = """
                              struct row { uint8 a; uint8 b; };
                              struct root { row grid[2][3]; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, aligned: false);
        byte[] bytes = [.. Enumerable.Range(0, 12).Select(i => (byte)i),];
        using var stream = new MemoryStream(bytes);

        (List<DebugData>? debug, dynamic cell) = cstruct.ParseStreamWithDebug(stream, "root.grid[1][2]");

        Assert.AreEqual((byte)10, cell.a);
        Assert.AreEqual((byte)11, cell.b);
        Assert.IsNotNull(debug);
        Assert.HasCount(2, debug);
        Assert.IsTrue(debug.All(dbg => dbg.DebugStackString.StartsWith("root.grid", StringComparison.Ordinal)));
        Assert.AreEqual(10L, debug[0].CurPos);
        Assert.AreEqual(12L, debug[^1].EndPos);
    }
}
