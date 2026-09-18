namespace CStructSharp.Tests;

using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     Exercises <see cref="LayoutMath"/> directly, independent of a compiled <see cref="CStruct"/> layout. Only
///     reachable indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c>
///     partial class.
/// </summary>
[TestClass]
public class LayoutMathTests
{
    /// <summary>An already-aligned offset must be returned unchanged, never rounded up to the next unit.</summary>
    [TestMethod]
    public void AlignUpInt32_ValueAlreadyAligned_ReturnsSameValue()
    {
        Assert.AreEqual(8, LayoutMath.AlignUp(8, 4));
        Assert.AreEqual(0, LayoutMath.AlignUp(0, 4));
    }

    /// <summary>A misaligned offset must round up to the next multiple of the requested alignment.</summary>
    [TestMethod]
    public void AlignUpInt32_ValueNotAligned_RoundsUpToNextMultiple()
    {
        Assert.AreEqual(8, LayoutMath.AlignUp(5, 4));
        Assert.AreEqual(4, LayoutMath.AlignUp(1, 4));
    }

    /// <summary>An alignment of one never changes the value, regardless of what it is.</summary>
    [TestMethod]
    public void AlignUpInt32_AlignmentOfOne_IsANoOp()
    {
        Assert.AreEqual(7, LayoutMath.AlignUp(7, 1));
    }

    /// <summary>A non-positive alignment is not a valid layout rule.</summary>
    [TestMethod]
    public void AlignUpInt32_NonPositiveAlignment_Throws()
    {
        Assert.Throws<CStructLayoutException>(() => LayoutMath.AlignUp(4, 0));
        Assert.Throws<CStructLayoutException>(() => LayoutMath.AlignUp(4, -1));
    }

    /// <summary>Rounding up must not silently wrap past the 32-bit range.</summary>
    [TestMethod]
    public void AlignUpInt32_RoundingPastMaxValue_Throws()
    {
        Assert.Throws<OverflowException>(() => LayoutMath.AlignUp(int.MaxValue - 1, 8));
    }

    /// <summary>The 64-bit overload must apply the identical rounding rule as the 32-bit overload.</summary>
    [TestMethod]
    public void AlignUpInt64_ValueNotAligned_RoundsUpToNextMultiple()
    {
        Assert.AreEqual(8L, LayoutMath.AlignUp(5L, 4));
        Assert.AreEqual(8L, LayoutMath.AlignUp(8L, 4));
    }

    /// <summary>The 64-bit overload must reject a non-positive alignment identically to the 32-bit overload.</summary>
    [TestMethod]
    public void AlignUpInt64_NonPositiveAlignment_Throws()
    {
        Assert.Throws<CStructLayoutException>(() => LayoutMath.AlignUp(4L, 0));
    }

    /// <summary>The 64-bit overload must not silently wrap past the range either.</summary>
    [TestMethod]
    public void AlignUpInt64_RoundingPastMaxValue_Throws()
    {
        Assert.Throws<OverflowException>(() => LayoutMath.AlignUp(long.MaxValue - 1, 8));
    }
}
