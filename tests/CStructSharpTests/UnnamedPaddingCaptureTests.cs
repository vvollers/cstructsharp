namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks unnamed padding cannot replace a caller variable reached through a decoded text alias.</summary>
[TestClass]
public class UnnamedPaddingCaptureTests
{
    /// <summary>Unnamed padding never captures a layout variable, so it can never publish an empty name.</summary>
    /// <param name="array">Whether the padding is an array.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Padding_DoesNotPublishAnEmptyName(bool array)
    {
        string padding = array ? "uint8 _[2];" : "uint8 _;";
        var layout = new CStruct("struct root { " + padding + " uint8 key; uint8 values[key]; uint8 tail; };");
        Assert.IsFalse(layout.CompiledModel.AllFields().Single(field => field.Name.Length == 0).CapturesLayoutVariable);

        byte[] bytes = array ? [2, 2, 1, 0xAA, 0xBB,] : [2, 1, 0xAA, 0xBB,];
        StructValue result = layout.Parse(bytes.AsSpan(), "root", new Dictionary<string, int> { [string.Empty] = 1, });
        Assert.AreEqual((byte)0xBB, result["tail"]);
        Assert.IsFalse(result.ContainsKey(string.Empty));
    }
}
