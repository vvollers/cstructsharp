namespace CStructSharp.Tests;

using CStructSharp.Introspection;

/// <summary>Checks decoded text and byte constants, including escaped controls and physical line continuations.</summary>
[TestClass]
public class QuotedConstantBoundaryTests
{
    /// <summary>Quoted definitions retain each decoded character in both text and byte forms.</summary>
    /// <param name="body">The literal contents as written in the layout source.</param>
    /// <param name="expected">The decoded characters exposed by the constant.</param>
    [TestMethod]
    [DataRow("a\\nb", "a\nb")]
    [DataRow("a\\rb", "a\rb")]
    [DataRow("a\\tb", "a\tb")]
    [DataRow("a\\0b", "a\0b")]
    [DataRow("a\\\nb", "ab")]
    [DataRow("a\\\r\nb", "ab")]
    [DataRow("a\nb", "a\nb")]
    [DataRow("a\\\\b", "a\\b")]
    public void QuotedDefinitions_PreserveDecodedCharacters(string body, string expected)
    {
        var layout = new CStruct("#define TEXT \"" + body + "\"\n#define BYTES b'" + body + "'\nstruct root { uint8 value; };");

        Assert.AreEqual(LayoutConstantKind.Text, layout.Constants["TEXT"].Kind);
        Assert.AreEqual(expected, layout.Constants["TEXT"].Value);
        Assert.AreEqual(LayoutConstantKind.Bytes, layout.Constants["BYTES"].Kind);
        CollectionAssert.AreEqual(System.Text.Encoding.Latin1.GetBytes(expected), (byte[])layout.Constants["BYTES"].Value!);
        Assert.AreEqual((byte)7, layout.ReadValue<byte>(new byte[] { 7, }.AsSpan(), "root.value"));
    }
}
