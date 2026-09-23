namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks unnamed padding cannot replace a caller variable reached through a decoded text alias.</summary>
[TestClass]
public class UnnamedPaddingCaptureTests
{
    /// <summary>An empty text alias keeps its caller-supplied count instead of capturing unnamed padding bytes.</summary>
    /// <param name="array">Whether padding is a numeric array rather than one scalar.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Padding_DoesNotPublishAnEmptyName(bool array)
    {
        string padding = array ? "uint8 _[2];" : "uint8 _;";
        var layout = new CStruct("struct root { " + padding + " cstring key; uint8 values[key]; uint8 tail; };");
        byte[] bytes = array ? [2, 2, 0, 0xAA, 0xBB,] : [2, 0, 0xAA, 0xBB,];
        var variables = new Dictionary<string, int> { [string.Empty] = 1, };
        StructValue result = layout.Parse(bytes.AsSpan(), "root", variables);
        Assert.AreEqual((byte)0xBB, result["tail"]);
        Assert.IsFalse(result.ContainsKey(string.Empty));
    }
}
