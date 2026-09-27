namespace CStructSharp.Tests;

using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>
///     Exercises <see cref="PrimitiveCodecs"/> directly, independent of a compiled <see cref="CStruct"/> layout, and
///     checks that the primitive reader, writer and alignment tables every <see cref="CStruct"/> shares keep each
///     layout's own byte order and alias resolution. <c>ReadIntoString</c>'s chunked-read and byte-budget behavior is
///     covered through the public API in <c>StringEncodingTests.cs</c>.
/// </summary>
[TestClass]
public class PrimitiveCodecsTests
{
    private const string Layout = "struct root { int32 value; uint16 pair; };";

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
    public void Aliases_MapCStyleAndShorthandNamesToCanonicalSpellings()
    {
        Assert.AreEqual("int32", PrimitiveSpellings.Aliases["int"]);
        Assert.AreEqual("uint64", PrimitiveSpellings.Canonicalize("ulong", 64));
        Assert.AreEqual("uint32", PrimitiveSpellings.Canonicalize("ulong", 32));
        Assert.AreEqual("unicode_string_zero", PrimitiveSpellings.Aliases["string"]);
        Assert.AreEqual("ascii_string_zero", PrimitiveSpellings.Aliases["cstring"]);
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

    /// <summary>
    ///     Two instances built with opposite endianness, from the same process (so both necessarily share the
    ///     static base tables), must each read/write using their own byte order - not whichever instance
    ///     happened to populate the shared static tables first.
    /// </summary>
    [TestMethod]
    public void OppositeEndiannessInstances_ReadAndWriteIndependently()
    {
        var little = new CStruct(Layout, pointerSize: 1, isLittleEndian: true);
        var big = new CStruct(Layout, pointerSize: 1, isLittleEndian: false);

        byte[] bytes = little.Serialize("root", new Dictionary<string, object?> { ["value"] = 0x11223344, ["pair"] = (ushort)0xAABB, });

        using var littleStream = new MemoryStream(bytes);
        dynamic parsedLittle = little.Parse(littleStream, "root");
        Assert.AreEqual(0x11223344, parsedLittle.value);
        Assert.AreEqual((ushort)0xAABB, parsedLittle.pair);

        // The same bytes decoded with the opposite-endianness instance must NOT agree - proving `big` truly uses
        // its own byte order rather than one baked into a shared, first-instance-wins static table.
        using var bigStream = new MemoryStream(bytes);
        dynamic parsedBig = big.Parse(bigStream, "root");
        Assert.AreNotEqual(0x11223344, parsedBig.value);
        Assert.AreNotEqual((ushort)0xAABB, parsedBig.pair);

        byte[] bigBytes = big.Serialize("root", new Dictionary<string, object?> { ["value"] = 0x11223344, ["pair"] = (ushort)0xAABB, });
        CollectionAssert.AreNotEqual(bytes, bigBytes);
    }

    /// <summary>
    ///     Constructing many instances in immediate succession (interleaving both endiannesses) must not corrupt
    ///     any instance's own alignment/handler resolution - a stress-shaped proof that the shared static base
    ///     tables are read-only from every instance's perspective.
    /// </summary>
    [TestMethod]
    public void InterleavedConstruction_EachInstanceKeepsItsOwnEndianness()
    {
        for (int i = 0; i < 8; i++)
        {
            bool isLittleEndian = i % 2 == 0;
            var cstruct = new CStruct(Layout, pointerSize: 1, isLittleEndian: isLittleEndian);

            byte[] bytes = cstruct.Serialize("root", new Dictionary<string, object?> { ["value"] = 1, ["pair"] = (ushort)2, });
            using var stream = new MemoryStream(bytes);
            dynamic parsed = cstruct.Parse(stream, "root");

            Assert.AreEqual(1, parsed.value);
            Assert.AreEqual((ushort)2, parsed.pair);
            Assert.AreEqual(4, cstruct.GetStructSizeInBytes("root") - 2, "int32 alignment/width must stay 4 bytes regardless of construction order.");
        }
    }
}
