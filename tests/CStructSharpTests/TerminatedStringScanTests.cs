namespace CStructSharpTests;

using System.Text;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Terminated strings are scanned per chunk and decoded once per chunk (E2.9); the observable positions, values,
///     and failures must match the former byte-by-byte reader for terminators, budgets, chunk boundaries, and
///     invalid or incomplete sequences.
/// </summary>
[TestClass]
public class TerminatedStringScanTests
{
    /// <summary>Terminators found at every chunk-relative offset leave the stream exactly after the terminator.</summary>
    [TestMethod]
    public void Terminator_AtChunkBoundaries_LeavesStreamAfterTerminator()
    {
        var layout = new CStruct("struct root { utf8_string_zero text; uint8 tail; };");
        foreach (int length in new[] { 0, 1, 255, 256, 257, 511, 512, 513, 1000 })
        {
            byte[] bytes = [.. Encoding.UTF8.GetBytes(new string('a', length)), 0, 0x7E];
            using var stream = new MemoryStream(bytes);
            dynamic parsed = layout.Parse(stream, "root");
            Assert.AreEqual(length, ((string)parsed.text).Length, $"length {length}");
            Assert.AreEqual((byte)0x7E, (byte)parsed.tail, $"length {length}");
            Assert.AreEqual(bytes.Length, stream.Position, $"length {length}");
        }
    }

    /// <summary>UTF-16 terminators are matched on unit boundaries: a 0x000A unit straddling two units is not a newline.</summary>
    [TestMethod]
    public void Utf16Terminator_IsMatchedOnUnitBoundariesOnly()
    {
        var layout = new CStruct("struct root { unicode_string_newline< text; uint8 tail; };");

        // U+0A00 followed by U+000A: bytes 00 0A 0A 00 - the newline unit is the second one (0A 00), not the pair 0A 00 spanning units.
        byte[] bytes = [0x00, 0x0A, 0x0A, 0x00, 0x7E];
        using var stream = new MemoryStream(bytes);
        dynamic parsed = layout.Parse(stream, "root");
        Assert.AreEqual("਀", (string)parsed.text);
        Assert.AreEqual((byte)0x7E, (byte)parsed.tail);

        var bigEndian = new CStruct("struct root { unicode_string_newline> text; uint8 tail; };");
        byte[] be = [0x0A, 0x00, 0x00, 0x0A, 0x7E];
        dynamic parsedBe = bigEndian.Parse(be, "root");
        Assert.AreEqual("਀", (string)parsedBe.text);
    }

    /// <summary>Multi-byte characters split across the 256-byte chunk boundary decode correctly.</summary>
    [TestMethod]
    public void MultiByteSequences_AcrossChunkBoundaries_DecodeCorrectly()
    {
        var layout = new CStruct("struct root { utf8_string_zero text; };");
        for (int prefix = 250; prefix <= 258; prefix++)
        {
            string text = new string('x', prefix) + "€𝄞é";
            byte[] bytes = [.. Encoding.UTF8.GetBytes(text), 0];
            dynamic parsed = layout.Parse(bytes, "root");
            Assert.AreEqual(text, (string)parsed.text, $"prefix {prefix}");
        }
    }

    /// <summary>An incomplete multi-byte sequence directly before the terminator is rejected, as it always was.</summary>
    [TestMethod]
    public void IncompleteSequenceBeforeTerminator_IsRejected()
    {
        var layout = new CStruct("struct root { utf8_string_zero text; };");
        byte[] bytes = [0x61, 0xE2, 0x82, 0x00];
        CStructReadException failure = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(bytes, "root"));
        StringAssert.Contains(failure.Message, "invalid for its encoding");

        byte[] invalid = [0x61, 0xFF, 0x62, 0x00];
        Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(invalid, "root"));
    }

    /// <summary>Exceeding the string byte budget leaves the stream one byte past the limit, terminator bytes included.</summary>
    [TestMethod]
    public void BudgetExceeded_LeavesStreamOneBytePastTheLimit()
    {
        var layout = new CStruct("struct root { utf8_string_zero text; };");
        foreach (int limit in new[] { 1, 4, 255, 256, 300 })
        {
            byte[] bytes = [.. Encoding.UTF8.GetBytes(new string('a', 400)), 0];
            using var stream = new MemoryStream(bytes);
            Assert.ThrowsExactly<CStructReadLimitException>(
                () => layout.Parse(stream, "root", options: new ReadOptions { MaxStringBytes = limit }),
                $"limit {limit}");
            Assert.AreEqual(limit + 1, stream.Position, $"limit {limit}");
        }

        // A terminator whose own byte would be the over-budget byte is still over budget.
        byte[] exact = [0x61, 0x62, 0x63, 0];
        using var exactStream = new MemoryStream(exact);
        Assert.ThrowsExactly<CStructReadLimitException>(
            () => layout.Parse(exactStream, "root", options: new ReadOptions { MaxStringBytes = 3 }));
        Assert.AreEqual(4, exactStream.Position);

        using var fitsStream = new MemoryStream(exact);
        dynamic parsed = layout.Parse(fitsStream, "root", options: new ReadOptions { MaxStringBytes = 4 });
        Assert.AreEqual("abc", (string)parsed.text);
    }

    /// <summary>Round trip through the pooled writer for every terminated encoding, including long values.</summary>
    [TestMethod]
    public void TerminatedStrings_RoundTrip_AllEncodings()
    {
        var layout = new CStruct(
            "struct root { ascii_string_zero a; utf8_string_newline u; unicode_string_zero< l; unicode_string_newline> b; };");
        string wide = string.Concat(Enumerable.Repeat("Grüße世界𝄞", 300));
        var data = new Dictionary<string, object>
        {
            ["a"] = new string('A', 3000),
            ["u"] = wide,
            ["l"] = wide,
            ["b"] = wide,
        };
        byte[] bytes = layout.Serialize("root", data);
        dynamic parsed = layout.Parse(bytes, "root");
        Assert.AreEqual(data["a"], (string)parsed.a);
        Assert.AreEqual(wide, (string)parsed.u);
        Assert.AreEqual(wide, (string)parsed.l);
        Assert.AreEqual(wide, (string)parsed.b);
    }
}
