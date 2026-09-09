namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Structure;

/// <summary>Exercises the byte-order and validation contract for narrow and wide character data.</summary>
[TestClass]
public class StringEncodingTests
{
    /// <summary>
    ///     No byte-order suffix is present on wchar[], so the big-endian layout setting applies.
    /// </summary>
    /// <remarks>
    ///     Bytes 00 41 form A, and 00 00 is the complete two-byte terminator. The result must be A and the stream must
    ///     advance four bytes, excluding the terminator from the string itself.
    /// </remarks>
    [TestMethod]
    public void ParseStream_BigEndianNeutralWideString_DecodesUtf16BigEndian()
    {
        const string layout = "struct root { wchar value[]; };";
        var cstruct = new CStruct(layout, isLittleEndian: false);
        using var stream = new MemoryStream(new byte[] { 0x00, 0x41, 0x00, 0x00, });

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual("A", (string)parsed.value);
        Assert.AreEqual(4, stream.Position);
    }

    /// <summary>
    ///     The text A followed by an emoji uses three UTF-16 code units, plus a zero terminator.
    /// </summary>
    /// <remarks>
    ///     Both byte orders must produce the same C# string and place tail after the encoded text. Updating A to B
    ///     keeps the encoded length unchanged and must preserve the emoji and tail.
    /// </remarks>
    [TestMethod]
    public void NeutralWideTerminatedString_AllOperationsUseLayoutEndianness()
    {
        const string layout = "struct root { wchar name[]; uint8 tail; };";
        const string original = "A😀";
        const string replacement = "B😀";

        foreach (bool littleEndian in RegressionTestSupport.Endianness)
        {
            var cstruct = new CStruct(layout, isLittleEndian: littleEndian);
            byte[] originalString = EncodeUtf16(original + '\0', littleEndian);
            byte[] originalBytes = [.. originalString, 0x7F,];

            using var parseStream = new MemoryStream((byte[])originalBytes.Clone());
            dynamic parsed = cstruct.ParseStream(parseStream, "root");
            Assert.AreEqual(original, (string)parsed.name);
            Assert.AreEqual((byte)0x7F, (byte)parsed.tail);
            Assert.AreEqual(originalBytes.Length, parseStream.Position);

            parseStream.Position = 0;
            (List<DebugData> debug, _) = cstruct.ParseStreamWithDebug(parseStream, "root");
            DebugData stringDebug = debug.Single(item => item.DebugStackString == "root.name");
            Assert.AreEqual(0, stringDebug.CurPos);
            Assert.AreEqual(originalString.Length, stringDebug.EndPos);

            parseStream.Position = 0;
            Assert.AreEqual(original.Length, cstruct.GetDynamicArrayLength(parseStream, "root.name"));
            Assert.AreEqual(0, parseStream.Position);
            Assert.AreEqual(originalString.Length, cstruct.ResolveAddress(parseStream, "root.tail"));
            Assert.AreEqual(0, parseStream.Position);

            var value = new Dictionary<string, object>
            {
                ["name"] = original,
                ["tail"] = (byte)0x7F,
            };
            CollectionAssert.AreEqual(originalBytes, cstruct.Serialize("root", value));

            using var writeStream = new MemoryStream();
            cstruct.WriteStream(writeStream, "root", value);
            CollectionAssert.AreEqual(originalBytes, writeStream.ToArray());
            Assert.AreEqual(originalBytes.Length, writeStream.Position);

            using var updateStream = new MemoryStream((byte[])originalBytes.Clone());
            cstruct.UpdateStream(updateStream, "root.name", replacement);
            byte[] replacementBytes = [.. EncodeUtf16(replacement + '\0', littleEndian), 0x7F,];
            CollectionAssert.AreEqual(replacementBytes, updateStream.ToArray());
            Assert.AreEqual(0, updateStream.Position);
        }
    }

    /// <summary>
    ///     wchar&gt; reads big-endian units and wchar&lt; reads little-endian units even when the layout default
    ///     differs.
    /// </summary>
    /// <remarks>
    ///     Fixed buffers and terminated buffers must both follow those suffixes. The emoji also checks that two UTF-16
    ///     units can represent one visible character; writes and updates must preserve its encoding.
    /// </remarks>
    [TestMethod]
    public void ExplicitEndianWideBuffers_OverrideLayoutAndRoundTrip()
    {
        const string fixedLayout = """
                                   struct root
                                   {
                                       wchar> big[2];
                                       wchar< little[2];
                                       wchar neutral[2];
                                   };
                                   """;
        var fixedStruct = new CStruct(fixedLayout, aligned: true, isLittleEndian: false);
        byte[] fixedBytes =
        [
            .. EncodeUtf16("AZ", false),
            .. EncodeUtf16("AZ", true),
            .. EncodeUtf16("😀", false),
        ];

        using var fixedStream = new MemoryStream((byte[])fixedBytes.Clone());
        dynamic fixedParsed = fixedStruct.ParseStream(fixedStream, "root");
        Assert.AreEqual("AZ", (string)fixedParsed.big);
        Assert.AreEqual("AZ", (string)fixedParsed.little);
        Assert.AreEqual("😀", (string)fixedParsed.neutral);

        var fixedValue = new Dictionary<string, object>
        {
            ["big"] = "AZ",
            ["little"] = "AZ",
            ["neutral"] = "😀",
        };
        CollectionAssert.AreEqual(fixedBytes, fixedStruct.Serialize("root", fixedValue));

        fixedStream.Position = 0;
        fixedStruct.UpdateStream(fixedStream, "root.big[1]", 'B');
        byte[] updated = (byte[])fixedBytes.Clone();
        updated[2] = 0x00;
        updated[3] = 0x42;
        CollectionAssert.AreEqual(updated, fixedStream.ToArray());
        Assert.AreEqual(0, fixedStream.Position);

        const string terminatedLayout = "struct root { wchar> big[]; wchar< little[]; };";
        var terminatedStruct = new CStruct(terminatedLayout, isLittleEndian: true);
        byte[] terminatedBytes =
        [
            .. EncodeUtf16("A\0", false),
            .. EncodeUtf16("B\0", true),
        ];
        using var terminatedStream = new MemoryStream(terminatedBytes);
        dynamic terminatedParsed = terminatedStruct.ParseStream(terminatedStream, "root");
        Assert.AreEqual("A", (string)terminatedParsed.big);
        Assert.AreEqual("B", (string)terminatedParsed.little);
        Assert.AreEqual(terminatedBytes.Length, terminatedStream.Position);

        var terminatedValue = new Dictionary<string, object>
        {
            ["big"] = "A",
            ["little"] = "B",
        };
        CollectionAssert.AreEqual(
            terminatedBytes,
            terminatedStruct.Serialize("root", terminatedValue));

        using var terminatedWriteStream = new MemoryStream();
        terminatedStruct.WriteStream(terminatedWriteStream, "root", terminatedValue);
        CollectionAssert.AreEqual(terminatedBytes, terminatedWriteStream.ToArray());

        using var terminatedUpdateStream = new MemoryStream((byte[])terminatedBytes.Clone());
        terminatedStruct.UpdateStream(terminatedUpdateStream, "root.big", "C");
        byte[] updatedTerminatedBytes = [.. EncodeUtf16("C\0", false), .. EncodeUtf16("B\0", true),];
        CollectionAssert.AreEqual(
            updatedTerminatedBytes,
            terminatedUpdateStream.ToArray());
        Assert.AreEqual(0, terminatedUpdateStream.Position);
    }

    /// <summary>
    ///     A one-byte pointer leads to a terminated wide string at offset 2.
    /// </summary>
    /// <remarks>
    ///     Following it must still obey the target type's explicit byte order or the neutral layout default. Address
    ///     lookup and replacement must reach the same text, and a configured read limit must still apply through the
    ///     pointer.
    /// </remarks>
    [TestMethod]
    public void WideStringPointer_ReadAddressAndUpdateUseSelectedEncoding()
    {
        foreach ((string fieldType, bool layoutLittleEndian, bool dataLittleEndian) in new[]
                 {
                     ("wchar", false, false),
                     ("wchar>", true, false),
                     ("wchar<", false, true),
                     ("string>", true, false),
                 })
        {
            string layout = $"struct root {{ {fieldType} *name; uint8 tail; }};";
            var cstruct = new CStruct(layout, pointerSize: 1, isLittleEndian: layoutLittleEndian);
            byte[] initial = [0x02, 0x7F, .. EncodeUtf16("A\0", dataLittleEndian),];
            using var stream = new MemoryStream(initial);

            dynamic parsed = cstruct.ParseStream(stream, "root");
            var pointer = (Pointer)parsed.name;
            Assert.AreEqual(2L, pointer.Address);
            Assert.AreEqual("A", (string)pointer.Value!);

            stream.Position = 0;
            Assert.AreEqual(2, cstruct.ResolveAddress(stream, "root.name.value"));
            Assert.AreEqual(0, stream.Position);

            cstruct.UpdateStream(stream, "root.name.value", "B");
            CollectionAssert.AreEqual(
                new byte[] { 0x02, 0x7F, }.Concat(EncodeUtf16("B\0", dataLittleEndian)).ToArray(),
                stream.ToArray());
            Assert.AreEqual(0, stream.Position);
        }

        var limited = new CStruct("struct root { wchar> *name; };", pointerSize: 1);
        using var limitedStream = new MemoryStream(new byte[] { 0x01, 0x00, 0x41, 0x00, 0x00, });
        Assert.Throws<CStructReadLimitException>(
            () => limited.ParseStream(
                limitedStream,
                "root",
                new Dictionary<string, Expr>(),
                new ReadOptions { MaxPointerTargetBytes = 4, }));
    }

    /// <summary>
    ///     The first field contains big-endian A followed by a UTF-16 newline; the second contains little-endian B and
    ///     a newline.
    /// </summary>
    /// <remarks>
    ///     Results must be A and B without terminators. The final byte must remain 0x7F, proving each text reader stops
    ///     after its own complete encoded newline.
    /// </remarks>
    [TestMethod]
    public void ExplicitUtf16NewlineHandlers_StopAtEncodedTerminator()
    {
        const string layout = "struct root { unicode_string_newline> big; unicode_string_newline< little; uint8 tail; };";
        var cstruct = new CStruct(layout, isLittleEndian: true);
        byte[] bytes =
        [
            .. EncodeUtf16("A\n", false),
            .. EncodeUtf16("B\n", true),
            0x7F,
        ];
        using var stream = new MemoryStream(bytes);

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual("A", (string)parsed.big);
        Assert.AreEqual("B", (string)parsed.little);
        Assert.AreEqual((byte)0x7F, (byte)parsed.tail);
        Assert.AreEqual(bytes.Length, stream.Position);
        CollectionAssert.AreEqual(
            bytes,
            cstruct.Serialize(
                "root",
                new Dictionary<string, object>
                {
                    ["big"] = "A",
                    ["little"] = "B",
                    ["tail"] = (byte)0x7F,
                }));
    }

    /// <summary>
    ///     Each case chooses ASCII, UTF-8, or UTF-16 and either a zero or newline terminator.
    /// </summary>
    /// <remarks>
    ///     Text valid for that encoding must return unchanged and serialize to exactly the original bytes. The
    ///     following 0x7F byte checks that decoding neither stops too early nor consumes the next field.
    /// </remarks>
    [TestMethod]
    public void NamedTerminatedEncodings_ValidTextRoundTripsWithoutFallback()
    {
        Encoding strictUtf8 = new UTF8Encoding(false, true);
        Encoding strictUtf16Little = new UnicodeEncoding(false, false, true);
        Encoding strictUtf16Big = new UnicodeEncoding(true, false, true);
        (string Type, Encoding Encoding, char Terminator, bool LayoutLittleEndian, string Value)[] cases =
        [
            ("ascii_string_zero", Encoding.ASCII, '\0', true, "Cafe"),
            ("ascii_string_newline", Encoding.ASCII, '\n', true, "Cafe"),
            ("utf8_string_zero", strictUtf8, '\0', true, "Grüße😀"),
            ("utf8_string_newline", strictUtf8, '\n', true, "Grüße😀"),
            ("unicode_string_zero", strictUtf16Little, '\0', true, "Grüße😀"),
            ("unicode_string_newline", strictUtf16Little, '\n', true, "Grüße😀"),
            ("unicode_string_zero", strictUtf16Big, '\0', false, "Grüße😀"),
            ("unicode_string_newline", strictUtf16Big, '\n', false, "Grüße😀"),
        ];

        foreach ((string type, Encoding encoding, char terminator, bool littleEndian, string value) in cases)
        {
            var cstruct = new CStruct(
                $"struct root {{ {type} value; uint8 tail; }};",
                isLittleEndian: littleEndian);
            byte[] stringBytes = encoding.GetBytes(value + terminator);
            byte[] expected = [.. stringBytes, 0x7F,];
            using var stream = new MemoryStream(expected);

            dynamic parsed = cstruct.ParseStream(stream, "root");
            Assert.AreEqual(value, (string)parsed.value, type);
            Assert.AreEqual((byte)0x7F, (byte)parsed.tail, type);
            Assert.AreEqual(expected.Length, stream.Position, type);

            CollectionAssert.AreEqual(
                expected,
                cstruct.Serialize(
                    "root",
                    new Dictionary<string, object>
                    {
                        ["value"] = value,
                        ["tail"] = (byte)0x7F,
                    }),
                type);
        }
    }

    /// <summary>
    ///     The inputs include invalid UTF-8, a non-ASCII byte, an unpaired UTF-16 surrogate, and incomplete wide-
    ///     character data.
    /// </summary>
    /// <remarks>
    ///     Each must raise a read error. Substituting a replacement character would hide damaged binary data and could
    ///     make later field positions misleading.
    /// </remarks>
    [TestMethod]
    public void StrictStringReaders_RejectMalformedSequencesAndOddWideInput()
    {
        var utf8 = new CStruct("struct root { utf8_string_zero value; };");
        using var malformedUtf8 = new MemoryStream(new byte[] { 0xC3, 0x28, 0x00, });
        Assert.Throws<CStructReadException>(() => utf8.ParseStream(malformedUtf8, "root"));

        var ascii = new CStruct("struct root { ascii_string_zero value; };");
        using var malformedAscii = new MemoryStream(new byte[] { 0x80, 0x00, });
        Assert.Throws<CStructReadException>(() => ascii.ParseStream(malformedAscii, "root"));

        var fixedWide = new CStruct("struct root { wchar> value[1]; };");
        using var unpairedSurrogate = new MemoryStream(new byte[] { 0xD8, 0x00, });
        Assert.Throws<CStructReadException>(() => fixedWide.ParseStream(unpairedSurrogate, "root"));

        var terminatedWide = new CStruct("struct root { wchar value[]; };", isLittleEndian: false);
        using var oddWide = new MemoryStream(new byte[] { 0x00, 0x41, 0x00, });
        Assert.Throws<CStructReadException>(() => terminatedWide.ParseStream(oddWide, "root"));
    }

    /// <summary>
    ///     A narrow char can store U+00FF but cannot store U+0100 in one byte.
    /// </summary>
    /// <remarks>
    ///     Other cases include text outside ASCII, malformed surrogate input, embedded terminators, and text too large
    ///     for a fixed wide buffer. Writes must fail instead of losing characters or producing a string that reads back
    ///     differently.
    /// </remarks>
    [TestMethod]
    public void StrictStringWriters_RejectUnrepresentableOrTruncatingValues()
    {
        var narrowScalar = new CStruct("struct root { char value; };");
        CollectionAssert.AreEqual(
            new byte[] { 0xFF, },
            narrowScalar.Serialize("root", new Dictionary<string, object> { ["value"] = '\u00FF', }));
        Assert.Throws<CStructWriteException>(
            () => narrowScalar.Serialize("root", new Dictionary<string, object> { ["value"] = '\u0100', }));
        using var directNarrowWrite = new MemoryStream();
        Assert.Throws<CStructWriteException>(
            () => narrowScalar.WriteHandlers["char"](directNarrowWrite, '\u0100'));
        Assert.AreEqual(0, directNarrowWrite.Length);

        var ascii = new CStruct("struct root { ascii_string_zero value; };");
        Assert.Throws<CStructWriteException>(
            () => ascii.Serialize("root", new Dictionary<string, object> { ["value"] = "é", }));

        var utf8 = new CStruct("struct root { utf8_string_zero value; };");
        Assert.Throws<CStructWriteException>(
            () => utf8.Serialize("root", new Dictionary<string, object> { ["value"] = "\uD800", }));
        Assert.Throws<CStructWriteException>(
            () => utf8.Serialize("root", new Dictionary<string, object> { ["value"] = "before\0after", }));

        var fixedWide = new CStruct("struct root { wchar value[1]; };");
        Assert.Throws<CStructWriteException>(
            () => fixedWide.Serialize("root", new Dictionary<string, object> { ["value"] = "\uD800", }));
    }

    /// <summary>
    ///     A union needs a known storage extent so all overlapping members share a defined range.
    /// </summary>
    /// <remarks>
    ///     A terminated string ends according to its data, so its size is not fixed by its type. Every listed
    ///     terminated-string union member must therefore be rejected during layout construction.
    /// </remarks>
    [TestMethod]
    public void TerminatedStringHandlers_RemainVariableLengthDuringCompilation()
    {
        string[] types =
        [
            "ascii_string_zero",
            "ascii_string_newline",
            "utf8_string_zero",
            "utf8_string_newline",
            "unicode_string_zero",
            "unicode_string_zero>",
            "unicode_string_zero<",
            "unicode_string_newline",
            "unicode_string_newline>",
            "unicode_string_newline<",
            "cstring",
            "string",
            "string>",
            "string<",
        ];

        foreach (string type in types)
        {
            Assert.Throws<CStructLayoutException>(
                () => new CStruct($"union root {{ {type} value; uint8 other; }};"),
                type);
        }
    }

    /// <summary>
    ///     Big-endian A plus its zero terminator occupies four bytes, even though the returned text has one character.
    /// </summary>
    /// <remarks>
    ///     A sufficient byte budget must succeed and leave the position at 4; a smaller budget must fail. Limits count
    ///     the complete encoded terminator as well as the text.
    /// </remarks>
    [TestMethod]
    public void WideTerminatedString_EnforcesByteBudgetWithoutSplittingCodeUnits()
    {
        const string layout = "struct root { wchar value[]; };";
        var cstruct = new CStruct(layout, isLittleEndian: false);
        byte[] bytes = EncodeUtf16("A\0", false);

        using var accepted = new MemoryStream(bytes);
        dynamic parsed = cstruct.ParseStream(
            accepted,
            "root",
            new Dictionary<string, Expr>(),
            new ReadOptions { MaxStringBytes = 4, });
        Assert.AreEqual("A", (string)parsed.value);
        Assert.AreEqual(4, accepted.Position);

        using var rejected = new MemoryStream(bytes);
        Assert.Throws<CStructReadLimitException>(
            () => cstruct.ParseStream(
                rejected,
                "root",
                new Dictionary<string, Expr>(),
                new ReadOptions { MaxStringBytes = 3, }));
    }

    /// <summary>
    ///     A 2,000-character terminated string is long enough that reading it one byte at a time would need
    ///     thousands of underlying stream reads.
    /// </summary>
    /// <remarks>
    ///     The decoded value, the trailing field, and the final stream position must exactly match a byte-by-byte
    ///     reader's result, while the number of underlying <c>Read</c> calls must stay far below the string's
    ///     length - proving the terminator scan batches its I/O instead of issuing one call per byte.
    /// </remarks>
    [TestMethod]
    public void TerminatedString_LongValue_BatchesUnderlyingReadsWithoutChangingTheResult()
    {
        const string layout = "struct root { ascii_string_zero value; uint8 tail; };";
        var cstruct = new CStruct(layout);
        string expected = new('A', 2000);
        byte[] bytes = [.. Encoding.ASCII.GetBytes(expected), 0x00, 0x7F,];

        using var stream = new ReadCallCountingStream(bytes);
        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual(expected, (string)parsed.value);
        Assert.AreEqual((byte)0x7F, (byte)parsed.tail);
        Assert.AreEqual(bytes.Length, stream.Position);
        string message = $"Expected far fewer than one read call per byte; observed {stream.ReadCallCount} " +
                          $"calls for a {expected.Length}-character string.";
        Assert.IsTrue(stream.ReadCallCount < 50, message);
    }

    /// <summary>
    ///     The underlying stream never returns more than three bytes per physical read, forcing the terminator scan
    ///     to issue several reads to fill even its own internal chunk buffer.
    /// </summary>
    /// <remarks>
    ///     The decoded value and final position must still be exact regardless of how the physical reads happen to
    ///     be fragmented, proving the terminator scan correctly loops on a short read instead of assuming its
    ///     request size is always satisfied in one call.
    /// </remarks>
    [TestMethod]
    public void TerminatedString_UnderlyingStreamFragmentsEveryRead_StillDecodesExactly()
    {
        const string layout = "struct root { utf8_string_zero value; uint8 tail; };";
        var cstruct = new CStruct(layout);
        const string expected = "Hello, world! This text is long enough to span many fragmented reads.";
        byte[] bytes = [.. Encoding.UTF8.GetBytes(expected), 0x00, 0x7F,];

        using var stream = new ChunkedMemoryStream(bytes, maximumReadSize: 3, writable: false);
        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual(expected, (string)parsed.value);
        Assert.AreEqual((byte)0x7F, (byte)parsed.tail);
        Assert.AreEqual(bytes.Length, stream.Position);
    }

    /// <summary>
    ///     ABCDEFGH plus its terminator needs nine bytes, but the budget allows only five.
    /// </summary>
    /// <remarks>
    ///     A byte-by-byte reader consumes one byte, then checks the budget, so it always leaves the stream one byte
    ///     past the limit rather than exactly at it. The chunked reader must leave the stream at that same position
    ///     (six, not five and not the full ten-byte record) and must not touch the unrelated trailing field.
    /// </remarks>
    [TestMethod]
    public void TerminatedString_BudgetExceeded_LeavesStreamOneBytePastTheLimitLikeAByteByByteReader()
    {
        const string layout = "struct root { ascii_string_zero value; uint8 tail; };";
        var cstruct = new CStruct(layout);
        byte[] bytes = [.. Encoding.ASCII.GetBytes("ABCDEFGH"), 0x00, 0x7F,];
        using var stream = new MemoryStream(bytes);

        Assert.Throws<CStructReadLimitException>(
            () => cstruct.ParseStream(
                stream,
                "root",
                new Dictionary<string, Expr>(),
                new ReadOptions { MaxStringBytes = 5, }));
        Assert.AreEqual(6, stream.Position);
    }

    /// <summary>
    ///     Regression coverage for the architecture-review optimization (docs/architecture-improvement-plan.md,
    ///     AP-0.5) that rents <c>ReadIntoString</c>'s chunk buffer from a shared <see cref="System.Buffers.ArrayPool{T}"/>
    ///     instead of allocating a fresh one per call. Reading many terminated strings of varying lengths in
    ///     immediate succession - including a short string immediately after a long one - must decode each one
    ///     exactly, proving a rented (and possibly reused, larger, or previously dirty) buffer never leaks a
    ///     stale tail byte from an earlier rental into a later, shorter read.
    /// </summary>
    [TestMethod]
    public void TerminatedString_ReadManyBackToBack_EachDecodesExactlyDespiteBufferReuse()
    {
        const string layout = "struct root { ascii_string_zero value; };";
        var cstruct = new CStruct(layout);
        string[] values = ["ABCDEFGHIJKLMNOPQRSTUVWXYZ", "hi", new string('Q', 500), "x", "mid-length-value",];

        foreach (string expected in values)
        {
            byte[] bytes = [.. Encoding.ASCII.GetBytes(expected), 0x00,];
            using var stream = new MemoryStream(bytes);

            dynamic parsed = cstruct.ParseStream(stream, "root");

            Assert.AreEqual(expected, (string)parsed.value);
            Assert.AreEqual(bytes.Length, stream.Position);
        }
    }

    private static byte[] EncodeUtf16(string value, bool littleEndian)
    {
        return new UnicodeEncoding(!littleEndian, false, true).GetBytes(value);
    }
}
