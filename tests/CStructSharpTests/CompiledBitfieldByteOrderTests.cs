namespace CStructSharp.Tests;

using CStructSharp.Compilation;

/// <summary>Checks that compiled packed bitfield windows honor explicit storage byte order.</summary>
[TestClass]
public class CompiledBitfieldByteOrderTests
{
    /// <summary>Big-endian storage uses a whole unit for low-first bits and a narrow window for high-first bits.</summary>
    /// <param name="allocation">The direction in which bitfields consume the storage unit.</param>
    /// <param name="expectedBytes">The compiled storage window size in bytes.</param>
    [TestMethod]
    [DataRow(BitfieldAllocation.LowBitFirst, 2)]
    [DataRow(BitfieldAllocation.HighBitFirst, 1)]
    public void ExplicitBigEndianStorage_ControlsThePackedWindow(BitfieldAllocation allocation, int expectedBytes)
    {
        var layout = new CStruct("struct root { uint16> bits : 3; };", compilationOptions: new CStructCompilationOptions { BitfieldPacking = BitfieldPacking.SysV, BitfieldAllocation = allocation, });
        var root = (CompiledCompositeType)layout.CompiledModel.Symbols["root"].Symbol.Definition!;

        Assert.AreEqual(false, root.Fields[0].BitStorageIsLittleEndian);
        Assert.AreEqual(expectedBytes, root.Fields[0].BitUnitSize);
        Assert.AreEqual(expectedBytes, root.Symbol.FixedSize);
    }
}
