namespace CStructSharp.Tests;

/// <summary>Checks empty textual conversions through each terminated-string codec.</summary>
[TestClass]
public class TerminatedStringConversionTests
{
    /// <summary>A non-null object's null textual representation is normalized to empty text before adding the terminator.</summary>
    /// <param name="type">The terminated-string primitive, including explicit UTF-16 byte order where applicable.</param>
    /// <param name="hex">The complete expected encoding of the empty string.</param>
    [TestMethod]
    [DataRow("ascii_string_zero", "00")]
    [DataRow("ascii_string_newline", "0A")]
    [DataRow("utf8_string_zero", "00")]
    [DataRow("utf8_string_newline", "0A")]
    [DataRow("unicode_string_zero>", "0000")]
    [DataRow("unicode_string_zero<", "0000")]
    [DataRow("unicode_string_newline>", "000A")]
    [DataRow("unicode_string_newline<", "0A00")]
    public void NullTextConversion_WritesOnlyTheTerminator(string type, string hex)
    {
        var layout = new CStruct("struct root { " + type + " text; uint8 tail; };");
        var data = new Dictionary<string, object?> { ["text"] = new EmptyTextValue(), ["tail"] = (byte)99, };
        byte[] expected = [.. Convert.FromHexString(hex), 99,];
        CollectionAssert.AreEqual(expected, layout.Serialize("root", data));
    }

    /// <summary>Models the nullable result allowed by Object.ToString without passing a null field value.</summary>
    private sealed class EmptyTextValue
    {
        /// <summary>Returns no textual content for conversion to an empty encoded string.</summary>
        /// <returns>Null, which the terminated-string writer normalizes to empty text.</returns>
        public override string? ToString() => null;
    }
}
