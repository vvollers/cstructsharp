namespace CStructSharp.Generated.Parity;

/// <summary>Checks that generated and runtime operations share packed separator run boundaries.</summary>
[TestClass]
public class PackedSeparatorTests
{
    /// <summary>Both readers and writers reproduce the five-byte packed layout observed with GCC.</summary>
    [TestMethod]
    public void WiderFieldAfterSeparator_RoundTripsThroughBothPaths()
    {
        byte[] bytes = [99, 0, 0, 0, 1,];
        var generated = PackedSeparatorLayout.Parse(bytes);
        CollectionAssert.AreEqual(bytes, PackedSeparatorLayout.Serialize(generated));
        var runtime = new CStruct(PackedSeparatorLayout.Definition);
        dynamic parsed = runtime.Parse(bytes, "root");
        CollectionAssert.AreEqual(bytes, runtime.Serialize("root", parsed));
    }
}
