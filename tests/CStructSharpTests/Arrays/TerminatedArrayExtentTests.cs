namespace CStructSharp.Tests;

using CStructSharp.Engine;

/// <summary>
///     Checks the extent of terminated arrays: terminator-count arithmetic independently of scanning the encoded
///     values, and a following field that starts after the terminator.
/// </summary>
[TestClass]
public class TerminatedArrayExtentTests
{
    /// <summary>The storage count includes exactly one terminator, including at the largest valid boundary.</summary>
    /// <param name="values">The number of values before the terminator.</param>
    /// <param name="stored">The expected total number of stored elements.</param>
    [TestMethod]
    [DataRow(0, 1)]
    [DataRow(12, 13)]
    [DataRow(int.MaxValue - 1, int.MaxValue)]
    public void ValidValueCount_IncludesOneTerminator(int values, int stored)
    {
        Assert.AreEqual(stored, TargetResolver.CountStoredTerminatedElements(values));
    }

    /// <summary>The arithmetic limit can be verified without allocating or scanning billions of encoded elements.</summary>
    [TestMethod]
    public void MaximumValueCount_CannotAlsoStoreATerminator()
    {
        Assert.Throws<OverflowException>(() => TargetResolver.CountStoredTerminatedElements(int.MaxValue));
    }

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
