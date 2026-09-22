namespace CStructSharp.Tests;

/// <summary>Checks that unnamed enum arrays receive numeric zero values rather than missing values.</summary>
[TestClass]
public class WriterPaddingValueTests
{
    /// <summary>A runtime-sized record writes every unnamed enum element as its zero storage value.</summary>
    /// <param name="count">The number of fixed padding elements.</param>
    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public void EnumPaddingArray_ReceivesIntegralZero(int count)
    {
        var layout = new CStruct("enum mode : uint8 { zero = 0, one = 1 }; struct root { uint8 n; uint8 data[n]; mode _[" + count + "]; uint8 tail; };");
        var values = new Dictionary<string, object?> { ["n"] = (byte)0, ["data"] = Array.Empty<byte>(), ["tail"] = (byte)7, };
        byte[] expected = new byte[count + 2];
        expected[^1] = 7;
        CollectionAssert.AreEqual(expected, layout.Serialize("root", values));
        using var destination = new MemoryStream(Enumerable.Repeat((byte)0xcc, expected.Length).ToArray());
        layout.Write(destination, "root", values);
        CollectionAssert.AreEqual(expected, destination.ToArray());
    }
}
