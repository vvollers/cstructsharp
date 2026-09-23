namespace CStructSharp.Tests;

using CStructSharp.Addressing;
using CStructSharp.Diagnostics;

/// <summary>Checks that path indexes contain decimal digits only, including at their trailing boundary.</summary>
[TestClass]
public class PathIndexBoundaryTests
{
    /// <summary>A numeric prefix followed by NUL characters is not a valid decimal path index.</summary>
    /// <param name="index">The invalid index spelling before its closing bracket.</param>
    [TestMethod]
    [DataRow("0\0")]
    [DataRow("1\0")]
    [DataRow("1\0\0")]
    public void DecimalIndex_RejectsTrailingNulCharacters(string index)
    {
        string path = "root.items[" + index + "]";

        // The path grammar is stricter than any permissive trailing-character handling in numeric conversion.
        CStructPathException failure = Assert.Throws<CStructPathException>(() => CStructPathResolver.Parse(path));

        StringAssert.StartsWith(failure.Message, "Invalid array index:");
    }
}
