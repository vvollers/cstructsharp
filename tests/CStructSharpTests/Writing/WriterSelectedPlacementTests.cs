namespace CStructSharp.Tests;

/// <summary>Checks standalone selected-field writes from a caller-selected destination position.</summary>
[TestClass]
public class WriterSelectedPlacementTests
{
    /// <summary>A selected partial bitfield starts and retains its shared unit at the current output position.</summary>
    /// <param name="aligned">Whether ordinary field alignment is enabled.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectedBitfieldWrite_PreservesTheCurrentOutputOrigin(bool aligned)
    {
        var layout = new CStruct("struct root { uint8 value:3; };", aligned: aligned);
        using var destination = new MemoryStream(new byte[] { 0x11, 0x22, 0x33, 0xF8, });
        destination.Position = 3;
        layout.Write(destination, "root.value", (byte)5);
        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22, 0x33, 0xFD, }, destination.ToArray());
        Assert.AreEqual(3L, destination.Position, "A partially occupied storage unit remains the active write position.");
    }

    /// <summary>A selected bitfield that fills its storage unit advances to its end rather than reopening that unit.</summary>
    /// <param name="declaration">The bitfield storage type and width.</param>
    /// <param name="value">A value that fits the complete storage unit.</param>
    /// <param name="encoded">Expected little-endian bytes for that value.</param>
    [TestMethod]
    [DataRow("uint8 value:8;", 53, new byte[] { 53, })]
    [DataRow("uint16 value:16;", 4660, new byte[] { 52, 18, })]
    public void FullUnitBitfieldWrite_AdvancesPastItsStorage(string declaration, int value, byte[] encoded)
    {
        var layout = new CStruct("struct root { " + declaration + " };");
        using var destination = new MemoryStream(new byte[] { 0x11, 0xAA, 0xBB, 0xCC, });
        destination.Position = 1;
        layout.Write(destination, "root.value", value);
        byte[] expected = [0x11, 0xAA, 0xBB, 0xCC];
        encoded.CopyTo(expected, 1);
        CollectionAssert.AreEqual(expected, destination.ToArray());
        Assert.AreEqual(1L + encoded.Length, destination.Position);
    }
}
