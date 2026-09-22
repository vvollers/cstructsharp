namespace CStructSharp.Tests;

/// <summary>Checks canonical codec byte order outside the fixed-layout record fast path.</summary>
[TestClass]
public class DynamicPrimitiveByteOrderTests
{
    /// <summary>Runtime-sized records read and write independently specified asymmetric primitive bytes.</summary>
    /// <param name="type">The canonical primitive spelling, including its explicit byte order.</param>
    /// <param name="hex">The expected primitive bytes, excluding the surrounding record fields.</param>
    [TestMethod]
    [DataRow("ufixed8_8>", "FF80")]
    [DataRow("ufixed8_8<", "80FF")]
    [DataRow("uint24>", "010203")]
    [DataRow("int48>", "FFFFFFFEFDFD")]
    [DataRow("int48<", "FDFDFEFFFFFF")]
    [DataRow("int128>", "FFFFFFFFFFFFFFFFFFFFFFFFFFFEFDFD")]
    [DataRow("int128<", "FDFDFEFFFFFFFFFFFFFFFFFFFFFFFFFF")]
    [DataRow("uint128>", "00000000000000000000000000010203")]
    [DataRow("float16>", "3E00")]
    [DataRow("uint48>", "010203040506")]
    [DataRow("uint64>", "0102030405060708")]
    public void DynamicRecord_PreservesExplicitPrimitiveByteOrder(string type, string hex)
    {
        object expectedValue = type switch
        {
            "ufixed8_8>" or "ufixed8_8<" => 255.5,
            "uint24>" => 66051U,
            "int48>" or "int48<" => -66051L,
            "int128>" or "int128<" => (Int128)(-66051),
            "uint128>" => (UInt128)66051,
            "float16>" => (Half)1.5,
            "uint48>" => 0x010203040506UL,
            "uint64>" => 0x0102030405060708UL,
            _ => throw new ArgumentException("Unknown test primitive.", nameof(type)),
        };
        var layout = new CStruct("struct root { uint8 count; uint8 prefix[count]; " + type + " value; uint8 tail; };");
        byte[] expectedBytes = [1, 91, .. Convert.FromHexString(hex), 99,];
        using var input = new MemoryStream(expectedBytes);
        dynamic parsed = layout.Parse(input, "root");
        Assert.AreEqual(expectedValue, (object)parsed.value);
        Assert.AreEqual((byte)99, (byte)parsed.tail);

        var data = new Dictionary<string, object?>
        {
            ["count"] = (byte)1,
            ["prefix"] = new byte[] { 91, },
            ["value"] = expectedValue,
            ["tail"] = (byte)99,
        };
        CollectionAssert.AreEqual(expectedBytes, layout.Serialize("root", data));
    }

    /// <summary>Neutral wide-character arrays use the layout byte order through the general writer.</summary>
    /// <param name="littleEndian">Whether low-order bytes precede high-order bytes in each UTF-16 unit.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DynamicRecord_WideCharactersUseLayoutByteOrder(bool littleEndian)
    {
        var layout = new CStruct("struct root { uint8 count; uint8 prefix[count]; wchar text[2]; uint8 tail; };", isLittleEndian: littleEndian);
        byte[] expectedBytes = littleEndian ? [1, 91, 0x41, 0, 0xA9, 3, 99,] : [1, 91, 0, 0x41, 3, 0xA9, 99,];
        var data = new Dictionary<string, object?>
        {
            ["count"] = (byte)1,
            ["prefix"] = new byte[] { 91, },
            ["text"] = "AΩ",
            ["tail"] = (byte)99,
        };
        CollectionAssert.AreEqual(expectedBytes, layout.Serialize("root", data));
        using var input = new MemoryStream(expectedBytes);
        dynamic parsed = layout.Parse(input, "root");
        Assert.AreEqual("AΩ", (string)parsed.text);
    }
}
