namespace CStructSharp.Tests;

/// <summary>Checks standalone selected-field writes from a caller-selected destination position.</summary>
[TestClass]
public class WriterSelectedPlacementTests
{
    /// <summary>A selected bitfield starts at the current output position, without moving back to a default storage origin.</summary>
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
    }
}
