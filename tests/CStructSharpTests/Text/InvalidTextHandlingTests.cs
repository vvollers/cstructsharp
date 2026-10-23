namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     <see cref="ReadOptions.InvalidText"/>: by default invalid text fails the read at its field; with
///     <see cref="InvalidTextHandling.Replace"/> every text type that can hold invalid data reads each bad sequence as
///     U+FFFD and the fields, offsets and budgets after it are unchanged, on every input form.
/// </summary>
[TestClass]
public class InvalidTextHandlingTests
{
    private static readonly ReadOptions Lenient = new() { InvalidText = InvalidTextHandling.Replace };

    /// <summary>The text types, a layout with one of each followed by a tail byte, the invalid bytes and the replacement text.</summary>
    private static readonly (string Type, string Declaration, byte[] Bytes, string Expected)[] Cases =
    [
        ("utf8", "utf8 name[4];", [0x61, 0xFF, 0x62, 0x63], "a�bc"),
        ("utf16le", "utf16le name[4];", [0x61, 0x00, 0x00, 0xD8], "a�"),
        ("utf16be", "utf16be name[4];", [0x00, 0x61, 0xD8, 0x00], "a�"),
        ("wchar", "wchar name[2];", [0x61, 0x00, 0x00, 0xD8], "a�"),
        ("cstring", "cstring name;", [0x61, 0x80, 0x62, 0x00], "a�b"),
        ("utf8_string_zero", "utf8_string_zero name;", [0x61, 0xFF, 0x62, 0x00], "a�b"),
        ("string<", "string< name;", [0x61, 0x00, 0x00, 0xD8, 0x00, 0x00], "a�"),
    ];

    /// <summary>Every input form: a span, a stream backed by memory, and a stream that is read piece by piece.</summary>
    /// <param name="parser">The layout.</param>
    /// <param name="bytes">The input.</param>
    /// <param name="options">The read options.</param>
    /// <returns>The reads of the root, one per form.</returns>
    private static IEnumerable<dynamic> Reads(CStruct parser, byte[] bytes, ReadOptions? options)
    {
        yield return parser.Parse(bytes, "root", options: options);
        using var memory = new MemoryStream(bytes);
        yield return parser.Parse(memory, "root", options: options);
        using var chunked = new ChunkedMemoryStream(bytes, 1, false);
        yield return parser.Parse(chunked, "root", options: options);
        yield return parser.ParseWithDebug(bytes, "root", options: options).Value;
    }

    /// <summary>The default fails at the field, and the replacing option reads the text and the tail after it, on every form.</summary>
    [TestMethod]
    public void EveryTextType_ReplacesInvalidText_OnlyWhenAsked()
    {
        foreach ((string type, string declaration, byte[] invalid, string expected) in Cases)
        {
            var parser = new CStruct($"struct root {{ {declaration} uint8 tail; }};", aligned: false);
            byte[] bytes = [.. invalid, 99];
            Assert.Throws<CStructReadException>(() => parser.Parse(bytes, "root"), type + " must fail by default");
            using var memory = new MemoryStream(bytes);
            Assert.Throws<CStructReadException>(() => parser.Parse(memory, "root"), type + " must fail by default from a stream");
            foreach (dynamic read in Reads(parser, bytes, Lenient))
            {
                Assert.AreEqual(expected, (string)read.name, type);
                Assert.AreEqual((byte)99, (byte)read.tail, type + " tail");
            }
        }
    }

    /// <summary>Valid text reads the same under both settings, and text types that cannot be invalid are untouched.</summary>
    [TestMethod]
    public void ValidText_IsTheSameUnderBothSettings()
    {
        var parser = new CStruct("struct root { utf8 a[3]; utf16le b[4]; latin1 c[2]; cp437 d[2]; char e[2]; cstring f; uint8 tail; };", aligned: false);
        byte[] bytes = [0x68, 0xC3, 0xA9, 0x68, 0x00, 0xE9, 0x00, 0xE9, 0xFF, 0x82, 0xC4, 0x80, 0xFF, 0x6F, 0x6B, 0x00, 7];
        dynamic strict = parser.Parse(bytes, "root");
        dynamic lenient = parser.Parse(bytes, "root", options: Lenient);
        foreach (string member in new[] { "a", "b", "c", "d", "e", "f" })
        {
            Assert.AreEqual((string)strict[member], (string)lenient[member], member);
        }
    }

    /// <summary>The read budget charges the same bytes, so a limit that admits the strict read of valid text admits the lenient read of invalid text.</summary>
    [TestMethod]
    public void ReplacementDoesNotChangeWhatTheBudgetCharges()
    {
        var parser = new CStruct("struct root { utf8_string_zero name; uint8 tail; };", aligned: false);
        byte[] bytes = [0x61, 0xFF, 0x62, 0x00, 5];
        var options = new ReadOptions { InvalidText = InvalidTextHandling.Replace, MaxTotalBytesRead = bytes.Length };
        dynamic read = parser.Parse(bytes, "root", options: options);
        Assert.AreEqual("a�b", (string)read.name);
        var tight = new ReadOptions { InvalidText = InvalidTextHandling.Replace, MaxTotalBytesRead = bytes.Length - 1 };
        Assert.Throws<CStructReadLimitException>(() => parser.Parse(bytes, "root", options: tight));
    }
}
