namespace CStructSharpTests;

using System.Numerics;
using System.Text;
using CStructSharp;

/// <summary>Uses bounded exhaustive and deterministic generated cases to check codec edge conditions.</summary>
[TestClass]
public class BinaryTypePropertyTests
{
    /// <summary>Every byte tag selects exactly one arm; changing that arm is atomic even at equal widths.</summary>
    [TestMethod]
    public void ConditionalTags_ExhaustiveSelectionAndAtomicUpdates()
    {
        var parser = new CStruct("struct root { uint8 tag; switch(tag) { case 1: { uint16 first; } case 2: { int16 second; } default: { uint8 raw[2]; } } uint8 tail; };", aligned: false);
        for (int tag = 0; tag < 256; tag++)
        {
            byte[] bytes = [(byte)tag, 52, 18, 99];
            using var stream = new MemoryStream(bytes);
            (List<DebugData> debug, dynamic parsed) = parser.ParseStreamWithDebug(stream, "root");
            var members = (IDictionary<string, object>)parsed.root;
            string active = tag == 1 ? "first" : tag == 2 ? "second" : "raw";
            Assert.AreEqual(3, members.Count);
            Assert.IsTrue(members.ContainsKey(active));
            CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed.root));
            (long, long)[] expectedRanges = active == "raw" ? [(1L, 2L), (2L, 3L)] : [(1L, 3L)];
            CollectionAssert.AreEqual(
                expectedRanges,
                debug.Where(item => item.DebugStackString == "root." + active)
                    .Select(item => (item.CurPos, item.EndPos)).ToArray());
            stream.Position = 0;
            Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.tag", tag == 1 ? 2 : 1));
            CollectionAssert.AreEqual(bytes, stream.ToArray());
            Assert.AreEqual(0L, stream.Position);
        }
    }

    /// <summary>Every cut of generated multibyte text agrees with the strict encoding boundary.</summary>
    [TestMethod]
    public void BoundedText_GeneratedCutsNeverBorrowFollowingBytes()
    {
        var random = new Random(0x555446);
        var texts = new List<string> { string.Empty, "A\0é€😀", "中文" };
        for (int sample = 0; sample < 24; sample++)
        {
            int scalar;
            do
            {
                scalar = random.Next(0x110000);
            }
            while (scalar is >= 0xD800 and <= 0xDFFF);
            texts.Add(char.ConvertFromUtf32(scalar) + "é");
        }

        foreach ((string type, Encoding encoding) in new (string, Encoding)[]
        {
            ("utf8", new UTF8Encoding(false, true)),
            ("utf16le", new UnicodeEncoding(false, false, true)),
            ("utf16be", new UnicodeEncoding(true, false, true)),
        })
        {
            foreach (string text in texts)
            {
                byte[] encoded = encoding.GetBytes(text);
                for (int count = 0; count <= encoded.Length; count++)
                {
                    byte[] prefix = encoded[..count];
                    string? expected = null;
                    try
                    {
                        expected = encoding.GetString(prefix);
                    }
                    catch (DecoderFallbackException)
                    {
                        // This exact byte cut ends inside an encoded character.
                    }

                    var parser = new CStruct($"struct root {{ uint8 lead; {type} text[{count}]; uint8 tail; }};", aligned: false);
                    using var stream = new MemoryStream([7, .. prefix, 99]);
                    if (expected is null)
                    {
                        Assert.Throws<CStructReadException>(() => parser.ParseStream(stream, "root"));
                        Assert.AreEqual(count + 1L, stream.Position);
                    }
                    else
                    {
                        dynamic parsed = parser.ParseStream(stream, "root");
                        Assert.AreEqual(expected, (string)parsed.text);
                        Assert.AreEqual((byte)99, (byte)parsed.tail);
                        CollectionAssert.AreEqual(stream.ToArray(), parser.Serialize("root", parsed));
                    }
                }
            }
        }
    }

    /// <summary>All fixed-point and identifier bit patterns have byte-stable natural-value round trips.</summary>
    [TestMethod]
    public void FixedStorage_GeneratedBitPatternsRoundTrip()
    {
        var random = new Random(0x424954);
        foreach ((string type, int width) in new[]
        {
            ("int24", 3), ("uint24", 3), ("fixed16_16", 4), ("ufixed16_16", 4),
            ("fixed2_30", 4), ("ufixed8_8", 2), ("uuid", 16), ("guid", 16),
        })
        {
            foreach (bool littleEndian in new[] { false, true })
            {
                var parser = new CStruct($"struct root {{ uint8 prefix; {type} value; uint8 tail; }};", aligned: false, isLittleEndian: littleEndian);
                for (int sample = 0; sample < 64; sample++)
                {
                    var bytes = new byte[width + 2];
                    random.NextBytes(bytes);
                    dynamic parsed = parser.Parse(bytes, "root");
                    Assert.AreEqual(bytes[^1], (byte)parsed.tail);
                    CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed), type);
                    using var stream = new MemoryStream(bytes);
                    Assert.AreEqual(1L, parser.ResolveAddress(stream, "root.value"));
                    Assert.AreEqual(width + 1L, parser.ResolveAddress(stream, "root.tail"));
                }
            }
        }
    }

    /// <summary>Every final octet is checked against mathematical signed/unsigned width bounds.</summary>
    [TestMethod]
    public void Leb128_FinalOctetsMatchIntegerDomain()
    {
        foreach (int width in new[] { 32, 64 })
        {
            foreach (bool signed in new[] { false, true })
            {
                string type = (signed ? "s" : "u") + "leb128_" + width;
                var parser = new CStruct($"struct root {{ {type} value; }};", aligned: false);
                int length = (width + 6) / 7;
                BigInteger minimum = signed ? -(BigInteger.One << (width - 1)) : BigInteger.Zero;
                BigInteger maximum = (BigInteger.One << (signed ? width - 1 : width)) - 1;
                for (int final = 0; final < 256; final++)
                {
                    byte[] bytes = [.. Enumerable.Repeat((byte)128, length - 1), (byte)final, 99];
                    BigInteger expected = new BigInteger(final & 127) << ((length - 1) * 7);
                    if (signed && (final & 64) != 0)
                    {
                        expected -= BigInteger.One << (length * 7);
                    }

                    bool valid = final < 128 && expected >= minimum && expected <= maximum;
                    using var stream = new MemoryStream(bytes);
                    if (valid)
                    {
                        object value = parser.ReadValue(stream, "root.value")!;
                        Assert.AreEqual(expected, BigInteger.Parse(value.ToString()!), type + "/" + final);
                    }
                    else
                    {
                        Assert.Throws<CStructReadException>(() => parser.ReadValue(stream, "root.value"), type + "/" + final);
                    }

                    Assert.AreEqual((long)length, stream.Position, "Decoder borrowed the following byte: " + type + "/" + final);
                }
            }
        }
    }

    /// <summary>Random full-width integers round-trip and every proper encoded prefix is rejected.</summary>
    [TestMethod]
    public void Leb128_GeneratedValuesAndTruncations()
    {
        var random = new Random(0x4C4542);
        foreach (int width in new[] { 32, 64 })
        {
            foreach (bool signed in new[] { false, true })
            {
                string type = (signed ? "s" : "u") + "leb128_" + width;
                var parser = new CStruct($"struct root {{ {type} value; }};", aligned: false);
                for (int sample = 0; sample < 128; sample++)
                {
                    var raw = new byte[width / 8];
                    random.NextBytes(raw);
                    BigInteger expected = new BigInteger(raw, isUnsigned: !signed, isBigEndian: false);
                    object input = signed ? (object)(long)expected : (ulong)expected;
                    byte[] encoded = parser.Serialize("root", new { value = input });
                    Assert.IsTrue(encoded.Length <= (width + 6) / 7);
                    object actual = parser.ReadValue(encoded.AsSpan(), "root.value")!;
                    Assert.AreEqual(expected, BigInteger.Parse(actual.ToString()!));
                    for (int prefix = 0; prefix < encoded.Length; prefix++)
                    {
                        byte[] truncated = encoded[..prefix];
                        Assert.Throws<CStructReadException>(() => parser.ReadValue(truncated.AsSpan(), "root.value"));
                    }
                }
            }
        }
    }
}
