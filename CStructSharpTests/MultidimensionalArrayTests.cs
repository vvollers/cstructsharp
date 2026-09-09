namespace CStructSharp.Tests;

using System.IO;

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
}
