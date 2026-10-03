namespace CStructSharp.Tests.Generated;

using System;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Checks direct text decoding against the original materialization and strict exception metadata.</summary>
[TestClass]
public class TextDecodingEquivalenceTests
{
    /// <summary>Trimming Latin-1 before decoding keeps every nonzero byte and embedded NUL, and produces owned text.</summary>
    /// <param name="trim">Whether trailing NUL padding is omitted.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FixedLatin1_PreservesBytesPaddingAndOwnership(bool trim)
    {
        foreach (int length in new[] { 0, 1, 255, 256, 257, 1024, })
        {
            byte[] bytes = new byte[length];
            for (int index = 0; index < length * 3 / 4; index++)
            {
                bytes[index] = (byte)index;
            }

            string full = Encoding.Latin1.GetString(bytes);
            string expected = trim ? full.TrimEnd('\0') : full;
            byte[] input = [99, .. bytes, 77,];
            string value = Codec.DecodeFixedText(input.AsSpan(1, length), trim);
            var cursor = new ReadCursor(input, new ReadOptions { TrimFixedText = trim, }) { Position = 1, };
            string consumed = cursor.TakeFixedText(length, "text", "char");
            Assert.AreEqual(expected, value);
            Assert.AreEqual(expected, consumed);
            Assert.AreEqual(length + 1, cursor.Position);
            Assert.AreEqual((byte)77, cursor.Take(1, "tail", "uint8")[0]);
            Array.Fill(input, (byte)42);
            Assert.AreEqual(expected, value);
            Assert.AreEqual(expected, consumed);
        }
    }

    /// <summary>Span decoding preserves BOMs, supplementary characters and NULs in every bounded encoding.</summary>
    /// <param name="encoding">The bounded primitive spelling.</param>
    [TestMethod]
    [DataRow("utf8")]
    [DataRow("utf16le")]
    [DataRow("utf16be")]
    [DataRow("latin1")]
    [DataRow("cp437")]
    public void BoundedText_MatchesArrayDecoderValues(string encoding)
    {
        foreach (string text in new[] { string.Empty, "\0\0", "é\0A\0", new string('A', 1024) + "\0\0", })
        {
            AssertBoundedValue(encoding, BoundedTextCodec.Encode(encoding, text));
        }

        if (encoding is "utf8" or "utf16le" or "utf16be")
        {
            AssertBoundedValue(encoding, BoundedTextCodec.Encode(encoding, "\uFEFFA😀\0"));
        }
        else
        {
            var allBytes = new byte[256];
            for (int index = 0; index < allBytes.Length; index++)
            {
                allBytes[index] = (byte)index;
            }

            AssertBoundedValue(encoding, allBytes);
        }
    }

    /// <summary>Malformed bounded data keeps the array overload's decoder message, byte index and rejected bytes.</summary>
    /// <param name="encoding">The strict bounded encoding.</param>
    [TestMethod]
    [DataRow("utf8")]
    [DataRow("utf16le")]
    [DataRow("utf16be")]
    public void BoundedText_MatchesArrayDecoderFailures(string encoding)
    {
        byte[][] cases = encoding == "utf8"
            ? [[0xFF,], [65, 0xC3,], [65, 0xE0, 0x80, 0x80,], [0xF4, 0x90, 0x80, 0x80,], [65, 0xED, 0xA0, 0x80, 0,], [0, 0xFF, 0,]]
            : [[0xFF,], CodeUnitBytes("A\uD800\0", encoding == "utf16le"), CodeUnitBytes("\uDC00", encoding == "utf16le"), CodeUnitBytes("A\uD800\uD801", encoding == "utf16le")];
        foreach (byte[] bytes in cases)
        {
            // The array overload is the original path, including its framework-specific malformed-input metadata.
            DecoderFallbackException expected = Assert.ThrowsExactly<DecoderFallbackException>(() => BoundedTextCodec.Decode(encoding, bytes));
            byte[] input = [99, .. bytes, 77,];
            foreach (bool trim in new[] { false, true, })
            {
                // Decode from an interior slice: its indices must still match a field-owned array starting at zero.
                CStructReadException decoded = Assert.ThrowsExactly<CStructReadException>(() => Codec.DecodeBoundedText(input.AsSpan(1, bytes.Length), encoding, trim));
                AssertDecoderFailure(expected, decoded.InnerException);

                // Cursor failures must keep consumed bytes and attach the same original-input context.
                CStructReadException consumed = Assert.ThrowsExactly<CStructReadException>(() => ReadEncoded(input, bytes.Length, encoding, new ReadOptions { TrimFixedText = trim, }));
                AssertDecoderFailure(expected, consumed.InnerException);
                Assert.AreEqual("root", consumed.Path);
                Assert.AreEqual("text", consumed.Member);
                Assert.AreEqual(encoding, consumed.MemberType);
                Assert.AreEqual((long)bytes.Length + 1, consumed.Offset);
            }
        }
    }

    /// <summary>Direct wide decoding preserves code units, byte order, ownership and the public codec's ignored odd byte.</summary>
    /// <param name="littleEndian">Whether UTF-16 units use little-endian storage.</param>
    /// <param name="trim">Whether trailing NUL padding is omitted.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void WideText_PreservesValuesAndOddByteBehavior(bool littleEndian, bool trim)
    {
        var options = new ReadOptions { TrimFixedText = trim, };
        foreach (string text in new[] { string.Empty, "\0\0", "\uFEFFé\0A\0", "😀\0", new string('Ω', 512) + "\0\0", })
        {
            byte[] bytes = CodeUnitBytes(text, littleEndian);
            string expected = trim ? text.TrimEnd('\0') : text;
            byte[] input = [99, .. bytes, 77,];
            string decoded = Codec.DecodeWideText(input.AsSpan(1, bytes.Length), littleEndian, options);
            string odd = Codec.DecodeWideText(input.AsSpan(1), littleEndian, options);
            var cursor = new ReadCursor(input, options) { Position = 1, };
            string consumed = cursor.TakeWideText(text.Length, littleEndian, "text", "wchar");
            Assert.AreEqual(expected, decoded);
            Assert.AreEqual(expected, odd);
            Assert.AreEqual(expected, consumed);
            Assert.AreEqual(bytes.Length + 1, cursor.Position);
            Assert.AreEqual((byte)77, cursor.Take(1, "tail", "uint8")[0]);
            Array.Fill(input, (byte)42);
            Assert.AreEqual(expected, decoded);
            Assert.AreEqual(expected, consumed);
        }
    }

    /// <summary>Invalid wide units retain encoder failures, including the failing character and its original index.</summary>
    /// <param name="littleEndian">Whether UTF-16 units use little-endian storage.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void WideText_PreservesOriginalEncoderFailures(bool littleEndian)
    {
        Encoding encoding = new UnicodeEncoding(!littleEndian, false, true);
        foreach (string text in new[] { "A\uD800\0", "\uDC00", "\uD800\uD800\uDC00", "😀A\uD800", })
        {
            // The original reader materialized the raw code units, then validated them through GetByteCount.
            EncoderFallbackException expected = Assert.ThrowsExactly<EncoderFallbackException>(() => encoding.GetByteCount(text));
            byte[] bytes = CodeUnitBytes(text, littleEndian);
            byte[] input = [99, .. bytes, 77,];
            foreach (bool trim in new[] { false, true, })
            {
                var options = new ReadOptions { TrimFixedText = trim, };

                // The public codec continues to ignore the final unmatched byte even on the invalid path.
                CStructReadException decoded = Assert.ThrowsExactly<CStructReadException>(() => Codec.DecodeWideText(input.AsSpan(1), littleEndian, options));
                AssertEncoderFailure(expected, decoded.InnerException);

                // The cursor reports its end position after consuming every declared code unit before validation.
                CStructReadException consumed = Assert.ThrowsExactly<CStructReadException>(() => ReadWide(input, text.Length, littleEndian, options));
                AssertEncoderFailure(expected, consumed.InnerException);
                Assert.AreEqual("root", consumed.Path);
                Assert.AreEqual("text", consumed.Member);
                Assert.AreEqual("wchar", consumed.MemberType);
                Assert.AreEqual((long)bytes.Length + 1, consumed.Offset);
            }
        }
    }

    /// <summary>The cursor still rejects physical or budget shortfalls before decoding, and checks cancellation before a negative count.</summary>
    [TestMethod]
    public void WideText_PreservesFailureOrderingAndNegativeCounts()
    {
        byte[] invalid = [99, 0, 0xD8, 65, 0,];

        // A byte budget failure takes precedence over the first unit's malformed surrogate.
        CStructReadLimitException limited = Assert.ThrowsExactly<CStructReadLimitException>(() => ReadWide(invalid, 2, true, new ReadOptions { MaxTotalBytesRead = 1, }));
        Assert.IsNull(limited.InnerException);
        Assert.AreEqual(3L, limited.Offset);

        // A truncated last unit is reported before validating the earlier unit.
        CStructReadException shortRead = Assert.ThrowsExactly<CStructReadException>(() => ReadWide(invalid[..^1], 2, true, null));
        Assert.IsNull(shortRead.InnerException);
        Assert.AreEqual(4L, shortRead.Offset);
        StringAssert.StartsWith(shortRead.Message, "Not enough bytes: needed 2, available 1");

        foreach (int count in new[] { -1, int.MinValue, })
        {
            // This public advanced API historically allocated an array after TakeElements returned no bytes.
            OverflowException expected = Assert.ThrowsExactly<OverflowException>(() => _ = new char[count]);
            OverflowException actual = Assert.ThrowsExactly<OverflowException>(() => ReadWide([99,], count, true, null));
            Assert.AreEqual(expected.Message, actual.Message);
            Assert.AreEqual(expected.HResult, actual.HResult);

            // Cancellation is observed by TakeElements before that historical array-allocation failure.
            var options = new ReadOptions { CancellationToken = new System.Threading.CancellationToken(true), };
            Assert.ThrowsExactly<OperationCanceledException>(() => ReadWide([99,], count, true, options));
        }
    }

    /// <summary>Checks both direct and consuming bounded decoders against the array implementation with owned results.</summary>
    /// <param name="encoding">The bounded-text spelling.</param>
    /// <param name="bytes">The complete valid encoded field.</param>
    private static void AssertBoundedValue(string encoding, byte[] bytes)
    {
        string full = BoundedTextCodec.Decode(encoding, bytes);
        foreach (bool trim in new[] { false, true, })
        {
            string expected = trim ? full.TrimEnd('\0') : full;
            byte[] input = [99, .. bytes, 77,];
            string decoded = Codec.DecodeBoundedText(input.AsSpan(1, bytes.Length), encoding, trim);
            var cursor = new ReadCursor(input, new ReadOptions { TrimFixedText = trim, }) { Position = 1, };
            string consumed = cursor.TakeEncodedText(bytes.Length, encoding, "text", encoding);
            Assert.AreEqual(expected, decoded);
            Assert.AreEqual(expected, consumed);
            Assert.AreEqual(bytes.Length + 1, cursor.Position);
            Assert.AreEqual((byte)77, cursor.Take(1, "tail", "uint8")[0]);
            Array.Fill(input, (byte)42);
            Assert.AreEqual(expected, decoded);
            Assert.AreEqual(expected, consumed);
        }
    }

    /// <summary>Compares all decoder-specific failure properties retained by the original array overload.</summary>
    /// <param name="expected">The original decoder failure.</param>
    /// <param name="failure">The optimized read's inner failure.</param>
    private static void AssertDecoderFailure(DecoderFallbackException expected, Exception? failure)
    {
        Assert.IsInstanceOfType<DecoderFallbackException>(failure);
        var actual = (DecoderFallbackException)failure!;
        Assert.AreEqual(expected.Message, actual.Message);
        Assert.AreEqual(expected.HResult, actual.HResult);
        Assert.AreEqual(expected.Index, actual.Index);
        CollectionAssert.AreEqual(expected.BytesUnknown, actual.BytesUnknown);
    }

    /// <summary>Compares all encoder-specific failure properties retained by wide code-unit validation.</summary>
    /// <param name="expected">The original encoder failure.</param>
    /// <param name="failure">The optimized read's inner failure.</param>
    private static void AssertEncoderFailure(EncoderFallbackException expected, Exception? failure)
    {
        Assert.IsInstanceOfType<EncoderFallbackException>(failure);
        var actual = (EncoderFallbackException)failure!;
        Assert.AreEqual(expected.Message, actual.Message);
        Assert.AreEqual(expected.HResult, actual.HResult);
        Assert.AreEqual(expected.Index, actual.Index);
        Assert.AreEqual(expected.CharUnknown, actual.CharUnknown);
        Assert.AreEqual(expected.CharUnknownHigh, actual.CharUnknownHigh);
        Assert.AreEqual(expected.CharUnknownLow, actual.CharUnknownLow);
        Assert.AreEqual(expected.IsUnknownSurrogate(), actual.IsUnknownSurrogate());
    }

    /// <summary>Stores raw UTF-16 code units in either byte order without replacing invalid surrogate sequences.</summary>
    /// <param name="text">The raw code units to store.</param>
    /// <param name="littleEndian">Whether each unit's low byte comes first.</param>
    /// <returns>An owned array with exactly two bytes per code unit.</returns>
    private static byte[] CodeUnitBytes(string text, bool littleEndian)
    {
        var bytes = new byte[text.Length * 2];
        for (int index = 0; index < text.Length; index++)
        {
            bytes[(index * 2) + (littleEndian ? 0 : 1)] = (byte)text[index];
            bytes[(index * 2) + (littleEndian ? 1 : 0)] = (byte)(text[index] >> 8);
        }

        return bytes;
    }

    /// <summary>Reads one bounded field from byte one and attaches generated-operation failure context.</summary>
    /// <param name="input">The input with one leading byte.</param>
    /// <param name="count">The field extent in bytes.</param>
    /// <param name="encoding">The bounded-text spelling.</param>
    /// <param name="options">The read settings.</param>
    /// <returns>The decoded field.</returns>
    private static string ReadEncoded(byte[] input, int count, string encoding, ReadOptions options)
    {
        var cursor = new ReadCursor(input, options, "root") { Position = 1, };
        try
        {
            return cursor.TakeEncodedText(count, encoding, "text", encoding);
        }
        catch (CStructException exception)
        {
            cursor.Complete(exception);
            throw;
        }
    }

    /// <summary>Reads one wide field from byte one and attaches generated-operation failure context.</summary>
    /// <param name="input">The input with one leading byte.</param>
    /// <param name="count">The field extent in code units.</param>
    /// <param name="littleEndian">The code units' byte order.</param>
    /// <param name="options">Optional read settings.</param>
    /// <returns>The decoded field.</returns>
    private static string ReadWide(byte[] input, int count, bool littleEndian, ReadOptions? options)
    {
        var cursor = new ReadCursor(input, options, "root") { Position = 1, };
        try
        {
            return cursor.TakeWideText(count, littleEndian, "text", "wchar");
        }
        catch (CStructException exception)
        {
            cursor.Complete(exception);
            throw;
        }
    }
}
