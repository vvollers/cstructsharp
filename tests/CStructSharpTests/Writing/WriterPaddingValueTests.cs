namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks zero-valued unnamed enum and pointer storage and rejected composite padding targets.</summary>
[TestClass]
public class WriterPaddingValueTests
{
    /// <summary>Unnamed primitive pointers reserve address bytes and always encode the null address.</summary>
    /// <param name="array">Whether the padding contains two pointers rather than one.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PrimitivePointerPadding_UsesZeroAddress(bool array)
    {
        string padding = array ? "_[2]" : "_";
        var layout = new CStruct("struct root { uint8 *" + padding + "; uint8 tail; };", pointerSize: 2);
        var data = new Dictionary<string, object?> { ["tail"] = (byte)7, };
        byte[] expected = new byte[(array ? 4 : 2) + 1];
        expected[^1] = 7;
        CollectionAssert.AreEqual(expected, layout.Serialize("root", data));
        using var destination = new MemoryStream(Enumerable.Repeat((byte)0xcc, expected.Length).ToArray());
        layout.Write(destination, "root", data);
        CollectionAssert.AreEqual(expected, destination.ToArray());
    }

    /// <summary>Unnamed storage cannot use a composite target, either directly or through a pointer.</summary>
    /// <param name="declaration">The unsupported unnamed composite field declarator.</param>
    [TestMethod]
    [DataRow("child _")]
    [DataRow("child *_")]
    public void CompositePaddingTarget_IsRejected(string declaration)
    {
        // Composite targets have no single primitive zero value for the padding contract.
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct child { uint8 value; }; struct root { " + declaration + "; };"));
    }

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
