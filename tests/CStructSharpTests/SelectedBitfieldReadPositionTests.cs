namespace CStructSharp.Tests;

/// <summary>Checks selected bitfield reads preserve the active shared storage position.</summary>
[TestClass]
public class SelectedBitfieldReadPositionTests
{
    /// <summary>A partial MSVC unit remains active, while a completely consumed unit advances to its end.</summary>
    /// <param name="declaration">The storage type and bit width.</param>
    /// <param name="expected">The selected integer value.</param>
    /// <param name="advance">Bytes advanced beyond the input origin.</param>
    [TestMethod]
    [DataRow("uint8 value:3;", 4, 0)]
    [DataRow("uint16 value:8;", 52, 0)]
    [DataRow("uint16 value:16;", 4660, 2)]
    public void SelectedBitfield_RetainsOrCompletesItsUnit(string declaration, int expected, int advance)
    {
        // MSVC packing retains the complete declared storage unit instead of shrinking a packed SysV window.
        var layout = new CStruct(
            "struct root { " + declaration + " };",
            compilationOptions: new CStructCompilationOptions { BitfieldPacking = BitfieldPacking.Msvc, });
        using var source = new MemoryStream(new byte[] { 0xAA, 0xBB, 0x34, 0x12, });
        source.Position = 2;

        Assert.AreEqual(expected, layout.ReadValue<int>(source, "root.value"));
        Assert.AreEqual(2L + advance, source.Position);
    }
}
