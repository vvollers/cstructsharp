namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks compiled bitfield placement and array diagnostics without allocating large binary inputs.</summary>
[TestClass]
public class LayoutPlacementBoundaryTests
{
    /// <summary>Direct conditional bitfields explain that a named struct must own their placement.</summary>
    [TestMethod]
    public void ConditionalBitfield_ExplainsTheRequiredGroup()
    {
        // A conditional bit cannot safely continue a surrounding unconditional bit run.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 flag; if (flag) { uint8 value:1; } };"));
        StringAssert.Contains(failure.Message, "Place conditional bitfields inside a named struct group.");
    }

    /// <summary>A call in one dimension is folded even when the other dimension is already a literal.</summary>
    [TestMethod]
    public void MixedArrayDimensions_FoldTheirLayoutCalls()
    {
        var layout = new CStruct("struct root { uint8 values[sizeof(uint16)][3]; uint8 tail; };");
        Assert.AreEqual(7, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(6L, layout.ResolveAddress(new byte[7], "root.tail"));
    }

    /// <summary>A negative explicit byte offset has its own diagnostic instead of a generic offset mismatch.</summary>
    [TestMethod]
    public void NegativeOffset_ExplainsTheNonnegativeRequirement()
    {
        // The expected offset is zero, but negativity must be diagnosed before comparing the positions.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 value @(-1); };"));
        StringAssert.Contains(failure.Message, "Explicit offset assertion must be non-negative: value = -1");
    }

    /// <summary>A bitfield or separator after the largest fixed array must not wrap its compiled byte position.</summary>
    /// <param name="lastDeclaration">The bitfield declaration that extends placement beyond Int32.</param>
    [TestMethod]
    [DataRow("uint16 :0;")]
    [DataRow("uint8 last:1;")]
    public void BitPlacement_RejectsFixedExtentOverflow(string lastDeclaration)
    {
        // This only constructs size metadata; no multi-gigabyte byte array is needed.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct($"struct root {{ uint8 prefix[2147483647]; {lastDeclaration} }};"));
        Assert.IsInstanceOfType<OverflowException>(failure.InnerException);
    }

    /// <summary>Low-bit-first values in big-endian cells occupy their complete declared cell in packed SysV layouts.</summary>
    [TestMethod]
    public void BigEndianBitfield_RetainsItsFullCell()
    {
        var layout = new CStruct("struct root { uint16> value:1; uint8 tail; };");
        Assert.AreEqual(3, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(2L, layout.ResolveAddress(new byte[3], "root.tail"));
        dynamic parsed = layout.Parse(new byte[] { 0, 1, 23, }, "root");
        Assert.AreEqual(1, Convert.ToInt32(parsed.value));
        Assert.AreEqual((byte)23, (byte)parsed.tail);
    }

    /// <summary>An ordinary byte ends the preceding bit run so later bits start after that byte.</summary>
    [TestMethod]
    public void OrdinaryField_SeparatesBitRuns()
    {
        var layout = new CStruct("struct root { uint16 first:1; uint8 middle; uint16 last:1; };");
        Assert.AreEqual(3, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(2L, layout.ResolveAddress(new byte[3], "root.last"));
        dynamic parsed = layout.Parse(new byte[] { 1, 29, 0, }, "root");
        Assert.AreEqual(1, Convert.ToInt32(parsed.first));
        Assert.AreEqual((byte)29, (byte)parsed.middle);
        Assert.AreEqual(0, Convert.ToInt32(parsed.last));
    }

    /// <summary>A runtime-sized multidimensional array explains the supported fixed-dimension requirement.</summary>
    [TestMethod]
    public void RuntimeMultidimensionalArray_ExplainsItsLimitation()
    {
        // The count is a field value, so it cannot be folded to a compile-time dimension.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 count; uint8 values[count][2]; };"));
        StringAssert.Contains(failure.Message, "Every dimension of a multidimensional array must be a compile-time-fixed count");
        StringAssert.Contains(failure.Message, "runtime-sized dimension is not yet supported for two or more dimensions: values");
    }
}
