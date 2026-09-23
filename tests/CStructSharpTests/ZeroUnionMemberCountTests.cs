namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks runtime count failures retain their read-domain exception while measuring a union.</summary>
[TestClass]
public class ZeroUnionMemberCountTests
{
    /// <summary>A zero outer count does not turn an invalid nested runtime count into a construction error.</summary>
    [TestMethod]
    public void NestedInvalidCount_UsesTheReadFailureDomain()
    {
        var layout = new CStruct("#define count 1\nstruct item { uint8 data[count]; }; union choice { item values[0]; }; struct root { choice prefix; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 99, });
        var variables = new Dictionary<string, int> { ["count"] = -1, };
        Assert.Throws<CStructReadException>(() => layout.ResolveAddress(source, "root.tail", variables));
    }
}
