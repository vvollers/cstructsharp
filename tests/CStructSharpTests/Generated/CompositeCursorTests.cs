namespace CStructSharpTests.Generated;

using CStructSharp;
using CStructSharp.Generated;

/// <summary>Pins <see cref="CompositeCursor"/>, the field placement rule generated readers and writers drive.</summary>
[TestClass]
public class CompositeCursorTests
{
    /// <summary>A non-bitfield member closes the bitfield run: a later bitfield opens a new unit after it instead of joining the earlier run, under both packings and for the separator form.</summary>
    [TestMethod]
    public void AdvanceToField_ClosesTheBitfieldRun()
    {
        foreach (BitfieldPacking packing in new[] { BitfieldPacking.SysV, BitfieldPacking.Msvc })
        {
            // struct r { uint8 a:4; uint8 b; uint8 c:4; };
            var cursor = CompositeCursor.Start(0, aligned: false, packing, BitfieldAllocation.LowBitFirst);
            BitfieldSlot a = cursor.AdvanceToBitfield(1, 1, 4, 4, littleEndian: true, "a");
            Assert.AreEqual(new BitfieldSlot(0, 1, 0), a, packing.ToString());
            long b = cursor.AdvanceToField(1);
            Assert.AreEqual(1L, b, packing.ToString());
            cursor.CompleteField(b + 1);
            BitfieldSlot c = cursor.AdvanceToBitfield(1, 1, 4, 4, littleEndian: true, "c");
            Assert.AreEqual(new BitfieldSlot(2, 1, 0), c, $"{packing}: 'c' starts a new unit after 'b' rather than filling 'a''s unit");
            Assert.AreEqual(3L, cursor.Finish(1), packing.ToString());

            // The runtime places the same layout the same way.
            var layout = new CStruct("struct r { uint8 a:4; uint8 b; uint8 c:4; };", compilationOptions: new CStructCompilationOptions { BitfieldPacking = packing, });
            Assert.AreEqual(3, layout.GetStructSizeInBytes("r"), packing.ToString());
            Assert.AreEqual(2L, layout.ResolveAddress(new byte[3], "r.c"), packing.ToString());
        }
    }
}
