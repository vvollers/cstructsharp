namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks literal array aliases receive the same count validation as expression aliases.</summary>
[TestClass]
public class LiteralTypedefCountBoundaryTests
{
    /// <summary>Array alias counts must be valid even when no later field uses the alias.</summary>
    /// <param name="count">The invalid literal spelling.</param>
    /// <param name="used">Whether a struct field instantiates the alias.</param>
    [TestMethod]
    [DataRow("-1", false)]
    [DataRow("-1", true)]
    [DataRow("0xFFFFFFFF", false)]
    [DataRow("0xFFFFFFFF", true)]
    [DataRow("2147483648", false)]
    [DataRow("2147483648", true)]
    public void ArrayAlias_RejectsInvalidLiteralCount(string count, bool used)
    {
        string member = used ? "invalid value;" : "uint8 value;";

        // Validate the declaration itself instead of relying on a later field or read to reject its count.
        Assert.Throws<CStructLayoutException>(() =>
            new CStruct("typedef uint8 invalid[" + count + "]; struct root { " + member + " };"));
    }

    /// <summary>Zero and positive literal counts retain their root-array read behavior.</summary>
    /// <param name="count">The valid element count.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(2)]
    public void ArrayAlias_AcceptsNonnegativeLiteralCount(int count)
    {
        var layout = new CStruct("typedef uint8 values[" + count + "];");
        using var source = new MemoryStream(new byte[count]);
        Assert.AreEqual(count, layout.ReadValue<byte[]>(source, "values").Length);
        Assert.AreEqual((long)count, source.Position);
    }
}
