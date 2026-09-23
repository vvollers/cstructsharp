namespace CStructSharp.Tests;

/// <summary>Checks terminator-count arithmetic independently of scanning the encoded values.</summary>
[TestClass]
public class TerminatedStorageCountTests
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
        Assert.AreEqual(stored, CStruct.CountStoredTerminatedElements(values));
    }

    /// <summary>The arithmetic limit can be verified without allocating or scanning billions of encoded elements.</summary>
    [TestMethod]
    public void MaximumValueCount_CannotAlsoStoreATerminator()
    {
        Assert.Throws<OverflowException>(() => CStruct.CountStoredTerminatedElements(int.MaxValue));
    }
}
