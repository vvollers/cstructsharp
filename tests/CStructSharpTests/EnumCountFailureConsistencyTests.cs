namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks that address traversal preserves the ordinary reader's failure cause for invalid enum counts.</summary>
[TestClass]
public class EnumCountFailureConsistencyTests
{
    /// <summary>An enum value outside the expression domain fails consistently in whole and selected reads.</summary>
    [TestMethod]
    public void WideEnumCount_PreservesTheWholeReadFailureCause()
    {
        var layout = new CStruct("enum count_type : uint64 { Maximum = 18446744073709551615 }; struct root { count_type count; uint8 values[count]; uint8 tail; };");
        byte[] bytes = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 42,];

        // Both operations reject the same decoded count, independently of their outer path context.
        CStructReadException whole = Assert.Throws<CStructReadException>(() => layout.Parse(bytes.AsSpan(), "root"));
        CStructReadException selected = Assert.Throws<CStructReadException>(() => layout.ResolveAddress(bytes, "root.tail"));
        Assert.IsNotNull(whole.InnerException);
        Assert.IsNotNull(selected.InnerException);
        Assert.AreEqual(whole.InnerException.Message, selected.InnerException.Message);
    }
}
