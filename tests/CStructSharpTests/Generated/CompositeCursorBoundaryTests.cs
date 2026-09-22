namespace CStructSharp.Tests.Generated;

using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Checks zero-width separators and diagnostic offsets at nonzero composite origins.</summary>
[TestClass]
public class CompositeCursorBoundaryTests
{
    /// <summary>Aligned cursors round both an ordinary field start and the final composite extent; packed cursors do neither.</summary>
    /// <param name="aligned">Whether natural byte alignment applies.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OrdinaryFieldAndFinish_ApplyAlignmentOnlyWhenEnabled(bool aligned)
    {
        var cursor = CompositeCursor.Start(1, aligned, BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst);
        Assert.AreEqual(aligned ? 4L : 1L, cursor.AdvanceToField(4));
        cursor.CompleteField(5);
        Assert.AreEqual(aligned ? 8L : 5L, cursor.Finish(4));
        Assert.AreEqual(5L, cursor.Current, "Finishing computes tail padding without changing field placement state.");
    }

    /// <summary>A packed field wholly inside a declared cell retains that shared storage window, including its final bit.</summary>
    /// <param name="precedingBits">Bits already placed in the four-byte cell.</param>
    [TestMethod]
    [DataRow(9)]
    [DataRow(31)]
    public void PackedFieldWithinCell_RetainsTheDeclaredWindow(int precedingBits)
    {
        var cursor = CompositeCursor.Start(0, false, BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst);
        Assert.AreEqual(new BitfieldSlot(0, 4, 0), cursor.AdvanceToBitfield(4, 4, precedingBits, 32, true, "before"));
        Assert.AreEqual(new BitfieldSlot(0, 4, precedingBits), cursor.AdvanceToBitfield(4, 4, 1, 32, true, "after"));
    }

    /// <summary>A leading MSVC separator starts from the actual origin and adds alignment only when requested.</summary>
    /// <param name="aligned">Whether the separator's declared alignment applies.</param>
    /// <param name="expected">The next field's input-relative byte offset.</param>
    [TestMethod]
    [DataRow(false, 3L)]
    [DataRow(true, 4L)]
    public void MsvcLeadingSeparator_UsesTheCompositeOrigin(bool aligned, long expected)
    {
        var cursor = CompositeCursor.Start(3, aligned, BitfieldPacking.Msvc, BitfieldAllocation.LowBitFirst);
        Assert.AreEqual(expected, cursor.AdvanceToSeparator(4, 4, 1));
        Assert.AreEqual(new BitfieldSlot(expected, 1, 0), cursor.AdvanceToBitfield(1, 1, 1, 1, true, "value"));
        Assert.AreEqual(expected + 1, cursor.Current);
    }

    /// <summary>A separator closes the old MSVC storage unit without allocating another unit of its own.</summary>
    [TestMethod]
    public void MsvcSeparator_ResetsThePreviousStorageUnit()
    {
        var cursor = CompositeCursor.Start(3, false, BitfieldPacking.Msvc, BitfieldAllocation.LowBitFirst);
        Assert.AreEqual(new BitfieldSlot(3, 4, 0), cursor.AdvanceToBitfield(4, 4, 3, 3, true, "before"));
        Assert.AreEqual(7L, cursor.AdvanceToSeparator(1, 1, 3));
        Assert.AreEqual(new BitfieldSlot(7, 4, 0), cursor.AdvanceToBitfield(4, 4, 3, 3, true, "after"));
        Assert.AreEqual(11L, cursor.Current);
    }

    /// <summary>A leading SysV separator rounds its actual bit position to the declared cell boundary.</summary>
    [TestMethod]
    public void SysVLeadingSeparator_UsesTheCompositeOriginInBits()
    {
        var cursor = CompositeCursor.Start(3, false, BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst);
        Assert.AreEqual(4L, cursor.AdvanceToSeparator(4, 4, 16));
        Assert.AreEqual(new BitfieldSlot(4, 1, 0), cursor.AdvanceToBitfield(1, 1, 3, 16, true, "value"));
        Assert.AreEqual(5L, cursor.Current);
    }

    /// <summary>A SysV reverse-filled cell remains fully occupied when a smaller separator follows it.</summary>
    [TestMethod]
    public void SysVSeparator_PreservesTheOccupiedReverseCell()
    {
        var cursor = CompositeCursor.Start(0, false, BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst);
        Assert.AreEqual(new BitfieldSlot(0, 4, 0), cursor.AdvanceToBitfield(4, 4, 1, 9, false, "before"));
        Assert.AreEqual(4L, cursor.Current);
        Assert.AreEqual(4L, cursor.AdvanceToSeparator(1, 1, 9));
        Assert.AreEqual(new BitfieldSlot(4, 1, 0), cursor.AdvanceToBitfield(1, 1, 1, 9, false, "after"));
    }

    /// <summary>An unsupported packed window reports its bit offset within a byte, not a multiplied absolute position.</summary>
    [TestMethod]
    public void PackedWindowFailure_ReportsTheBitRemainderAndActualExtent()
    {
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 prefix; uint8 a:4; uint64 b:64; };"));
        StringAssert.Contains(failure.Message, "Bitfield 'b' (64 bits at bit offset 4) would span 9 bytes");
        StringAssert.Contains(failure.Message, "at most 8 are supported");
    }
}
