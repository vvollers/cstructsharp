namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Structure;

/// <summary>Exercises the byte-order and validation contract for narrow and wide character data.</summary>
[TestClass]
public class StringEncodingTests
{
    /// <summary>Uses the layout byte order for a neutral terminated <c>wchar[]</c> field.</summary>
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
    ///     Applies the layout byte order consistently to parsing, debug mapping, measurement, addressing, serialization,
    ///     writing, and in-place updates of a neutral terminated wide string.
    /// </summary>
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

    /// <summary>Lets explicit <c>&gt;</c>/<c>&lt;</c> suffixes override the layout order for fixed and terminated buffers.</summary>
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

    /// <summary>Uses the same neutral and explicit-endian rules after following a character pointer.</summary>
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

    /// <summary>Supports newline-terminated UTF-16 in both explicit byte orders without consuming the following byte.</summary>
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

    /// <summary>Round-trips valid zero/newline strings through every neutral named encoding.</summary>
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

    /// <summary>Rejects malformed UTF-8/UTF-16 input instead of replacing bytes or returning invalid CLR strings.</summary>
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

    /// <summary>Rejects lossy writes, malformed surrogate input, and embedded terminators before emitting that value.</summary>
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

    /// <summary>Rejects every terminated handler as a union member because none has a fixed storage extent.</summary>
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

    /// <summary>Counts encoded bytes, including a complete wide terminator, when enforcing the read budget.</summary>
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

    private static byte[] EncodeUtf16(string value, bool littleEndian)
    {
        return new UnicodeEncoding(!littleEndian, false, true).GetBytes(value);
    }
}
