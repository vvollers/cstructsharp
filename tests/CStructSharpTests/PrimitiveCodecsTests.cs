namespace CStructSharp.Tests;

using System.Text;

/// <summary>
///     Exercises <see cref="PrimitiveCodecs"/> directly, independent of a compiled <see cref="CStruct"/> layout.
///     Only reachable indirectly through the public API before this type was extracted from the God-Object
///     <c>CStruct</c> partial class. <c>ReadIntoString</c>'s chunked-read and byte-budget behavior already has
///     thorough coverage exercised through the real public API in <c>StringEncodingTests.cs</c>, so these tests
///     focus on proving each member is independently callable and on the members that had no direct coverage yet.
/// </summary>
[TestClass]
public class PrimitiveCodecsTests
{
    /// <summary>Every documented terminated-string spelling, including the C-style aliases, must report as variable-length.</summary>
    [TestMethod]
    public void IsVariableLengthType_TerminatedStringSpellings_ReturnsTrue()
    {
        string[] variableLengthNames =
        [
            "ascii_string_zero", "ascii_string_newline", "utf8_string_zero", "utf8_string_newline",
            "unicode_string_zero", "unicode_string_zero>", "unicode_string_zero<",
            "unicode_string_newline", "unicode_string_newline>", "unicode_string_newline<",
            "cstring", "string", "string>", "string<",
        ];

        foreach (string name in variableLengthNames)
        {
            Assert.IsTrue(PrimitiveCodecs.IsVariableLengthType(name), name);
        }
    }

    /// <summary>A fixed-width primitive spelling must not be reported as variable-length.</summary>
    [TestMethod]
    public void IsVariableLengthType_FixedWidthSpellings_ReturnsFalse()
    {
        Assert.IsFalse(PrimitiveCodecs.IsVariableLengthType("uint32"));
        Assert.IsFalse(PrimitiveCodecs.IsVariableLengthType("byte"));
        Assert.IsFalse(PrimitiveCodecs.IsVariableLengthType("char"));
    }

    /// <summary>The C-style and shorthand aliases must resolve to their documented canonical spelling.</summary>
    [TestMethod]
    public void FieldTypeAliasses_MapsCStyleAndShorthandNamesToCanonicalSpellings()
    {
        Assert.AreEqual("int32", PrimitiveCodecs.FieldTypeAliasses["int"]);
        Assert.AreEqual("uint64", PrimitiveCodecs.FieldTypeAliasses["ulong"]);
        Assert.AreEqual("unicode_string_zero", PrimitiveCodecs.FieldTypeAliasses["string"]);
        Assert.AreEqual("ascii_string_zero", PrimitiveCodecs.FieldTypeAliasses["cstring"]);
    }

    /// <summary>A character within the one-byte domain converts directly.</summary>
    [TestMethod]
    public void ConvertToNarrowCharacter_WithinOneByteDomain_ConvertsDirectly()
    {
        Assert.AreEqual((byte)'A', PrimitiveCodecs.ConvertToNarrowCharacter('A'));
        Assert.AreEqual(byte.MaxValue, PrimitiveCodecs.ConvertToNarrowCharacter((char)byte.MaxValue));
    }

    /// <summary>A character outside the one-byte domain cannot be represented by the narrow char type.</summary>
    [TestMethod]
    public void ConvertToNarrowCharacter_OutsideOneByteDomain_Throws()
    {
        Assert.Throws<CStructWriteException>(() => PrimitiveCodecs.ConvertToNarrowCharacter((char)(byte.MaxValue + 1)));
    }

    /// <summary>A terminated string writes its encoded bytes followed immediately by the encoded terminator.</summary>
    [TestMethod]
    public void WriteTerminatedString_WritesEncodedValueThenTerminator()
    {
        using var stream = new MemoryStream();

        PrimitiveCodecs.WriteTerminatedString(stream, PrimitiveCodecs.StrictAsciiEncoding, "AB", '\0');

        CollectionAssert.AreEqual(new byte[] { 0x41, 0x42, 0x00, }, stream.ToArray());
    }

    /// <summary>A value that already contains the terminator character cannot be encoded unambiguously.</summary>
    [TestMethod]
    public void WriteTerminatedString_ValueContainsTheTerminator_Throws()
    {
        using var stream = new MemoryStream();

        Assert.Throws<CStructWriteException>(
            () => PrimitiveCodecs.WriteTerminatedString(stream, PrimitiveCodecs.StrictAsciiEncoding, "A\0B", '\0'));
    }

    /// <summary>A character outside the strict encoding's domain must fail instead of silently substituting one.</summary>
    [TestMethod]
    public void WriteTerminatedString_CharacterOutsideEncodingDomain_Throws()
    {
        using var stream = new MemoryStream();

        Assert.Throws<CStructWriteException>(
            () => PrimitiveCodecs.WriteTerminatedString(stream, PrimitiveCodecs.StrictAsciiEncoding, "café", '\0'));
    }

    /// <summary>A budget-tracking destination stream must reject a string whose encoded size exceeds its configured budget.</summary>
    [TestMethod]
    public void WriteTerminatedString_ExceedsTheDestinationStreamsBudget_Throws()
    {
        using var destination = new MemoryStream();
        using var budgeted = new WriteBudgetStream(destination, new WriteOptions { MaxStringBytes = 2, });

        Assert.Throws<CStructWriteLimitException>(
            () => PrimitiveCodecs.WriteTerminatedString(budgeted, PrimitiveCodecs.StrictAsciiEncoding, "AB", '\0'));
    }

    /// <summary>A basic read finds the terminator and leaves the stream immediately after it, excluding it from the value.</summary>
    [TestMethod]
    public void ReadIntoString_FindsTheTerminator_ExcludesItFromTheResultAndStopsAfterIt()
    {
        using var stream = new MemoryStream([0x41, 0x42, 0x00, 0x7F,]);

        string value = PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictAsciiEncoding, '\0');

        Assert.AreEqual("AB", value);
        Assert.AreEqual(3L, stream.Position);
    }

    /// <summary>A stream that ends before the terminator appears cannot yield a complete string.</summary>
    [TestMethod]
    public void ReadIntoString_StreamEndsBeforeTheTerminator_Throws()
    {
        using var stream = new MemoryStream([0x41, 0x42,]);

        Assert.Throws<CStructReadException>(() => PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictAsciiEncoding, '\0'));
    }

    /// <summary>Bytes that are invalid for the strict encoding must fail instead of silently substituting a replacement character.</summary>
    [TestMethod]
    public void ReadIntoString_InvalidBytesForTheEncoding_Throws()
    {
        using var stream = new MemoryStream([0x80, 0x00,]);

        Assert.Throws<CStructReadException>(() => PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictAsciiEncoding, '\0'));
    }

    /// <summary>Writing then reading the same value back through the strict UTF-8 encoding round-trips exactly.</summary>
    [TestMethod]
    public void WriteThenReadIntoString_Utf8RoundTrip_ProducesTheOriginalValue()
    {
        using var stream = new MemoryStream();
        PrimitiveCodecs.WriteTerminatedString(stream, PrimitiveCodecs.StrictUtf8Encoding, "héllo", '\0');
        stream.Position = 0;

        string value = PrimitiveCodecs.ReadIntoString(stream, PrimitiveCodecs.StrictUtf8Encoding, '\0');

        Assert.AreEqual("héllo", value);
    }
}
