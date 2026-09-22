namespace CStructSharp.Tests;

/// <summary>Checks that exact enum values at both Int32 boundaries remain available to layout expressions.</summary>
[TestClass]
public class EnumCaptureBoundaryTests
{
    /// <summary>Both inclusive Int32 endpoints may select a later field during reading and writing.</summary>
    /// <param name="value">The signed enum value at one expression-domain endpoint.</param>
    /// <param name="hex">The little-endian enum bytes followed by the selected payload.</param>
    [TestMethod]
    [DataRow(int.MinValue, "000000802A")]
    [DataRow(int.MaxValue, "FFFFFF7F2A")]
    public void BoundaryEnumValue_RemainsAvailableToACondition(int value, string hex)
    {
        var layout = new CStruct("enum edge : int32 { Low = -2147483648, High = 2147483647 }; struct root { edge value; if (value != 0) { uint8 payload; } };");
        var data = new Dictionary<string, object?> { ["value"] = value, ["payload"] = (byte)42, };
        byte[] expected = Convert.FromHexString(hex);

        CollectionAssert.AreEqual(expected, layout.Serialize("root", data));
        Assert.AreEqual((byte)42, layout.ReadValue<byte>(expected.AsSpan(), "root.payload"));
    }
}
