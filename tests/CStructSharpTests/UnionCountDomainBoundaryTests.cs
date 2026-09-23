namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks runtime union array counts retain read-domain diagnostics.</summary>
[TestClass]
public class UnionCountDomainBoundaryTests
{
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
