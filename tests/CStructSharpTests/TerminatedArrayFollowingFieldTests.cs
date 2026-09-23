namespace CStructSharp.Tests;

/// <summary>Checks address traversal includes a terminated array's zero element in its storage extent.</summary>
[TestClass]
public class TerminatedArrayFollowingFieldTests
{
    /// <summary>A later field begins after the array's values and its complete terminator.</summary>
    /// <param name="composite">Whether each fixed-size element is a record or a primitive.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FollowingField_StartsAfterTerminator(bool composite)
    {
        string element = composite ? "item" : "uint8";
        var layout = new CStruct("struct item { uint8 value; }; struct root { " + element + " items[]; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 11, 22, 0, 99, });
        Assert.AreEqual(3L, layout.ResolveAddress(source, "root.tail"));
        Assert.AreEqual(0L, source.Position);
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(source, "root.tail"));
    }
}
