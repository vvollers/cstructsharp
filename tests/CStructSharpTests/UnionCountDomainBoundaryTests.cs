namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks runtime union array counts retain read-domain diagnostics.</summary>
[TestClass]
public class UnionCountDomainBoundaryTests
{
    /// <summary>Union-size failures retain read diagnostics during member selection and preceding-field measurement.</summary>
    /// <param name="followingField">Whether the path must first measure a containing union field.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void UnionAddress_RejectsNegativeCallerCountAsReadFailure(bool followingField)
    {
        var layout = new CStruct("#define count 1\nunion choice { uint8 values[count]; }; struct root { choice item; uint8 tail; };");
        var variables = new Dictionary<string, int> { ["count"] = -1, };
        using var source = new MemoryStream(new byte[2]);
        string path = followingField ? "root.tail" : "choice.values";

        // Invalid caller data has the same error category whether selecting a union member or stepping past it.
        CStructReadException error = Assert.Throws<CStructReadException>(() => layout.ResolveAddress(source, path, variables: variables));
        StringAssert.Contains(error.Message, "Array length cannot be negative");
    }

    /// <summary>A caller count overrides a valid declaration without turning input failures into layout failures.</summary>
    [TestMethod]
    public void UnionCount_RejectsNegativeCallerValueAsReadFailure()
    {
        var layout = new CStruct("#define count 1\nunion choice { uint8 values[count]; };");
        var variables = new Dictionary<string, int> { ["count"] = -1, };
        using var source = new MemoryStream(new byte[1]);
        CStructReadException error = Assert.Throws<CStructReadException>(() => layout.ReadValue(source, "choice", variables: variables));
        StringAssert.Contains(error.Message, "Array length cannot be negative");
    }
}
