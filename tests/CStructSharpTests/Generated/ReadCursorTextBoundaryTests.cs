namespace CStructSharp.Tests.Generated;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Checks generated text decoding at capacity, encoding and chunk boundaries, including failure context.</summary>
[TestClass]
public class ReadCursorTextBoundaryTests
{
    /// <summary>The encoded-byte limit takes precedence over invalid text later in the same unterminated chunk.</summary>
    [TestMethod]
    public void TerminatedText_EnforcesItsLimitBeforeDecodingTheChunk()
    {
        byte[] source = [.. Enumerable.Repeat((byte)'a', 300), 0,];
        source[20] = 0xFF;

        // The limit is crossed at byte eleven, before the invalid ASCII byte could be decoded.
        CStructReadLimitException failure = Assert.Throws<CStructReadLimitException>(() => new ReadCursor(source, new ReadOptions { MaxStringBytes = 10, }).TakeTerminatedString(TerminatedTextEncoding.Ascii, '\0', "text", "string"));
        StringAssert.Contains(failure.Message, "configured encoded-byte limit");
        Assert.IsNull(failure.InnerException);
    }

    /// <summary>Chunk failures retain input-relative offsets and enforce the string limit before finding a later terminator.</summary>
    [TestMethod]
    public void TerminatedText_ReportsLimitsAtTheFailingChunk()
    {
        byte[] source = [99, .. Enumerable.Repeat((byte)'a', 300), 0,];
        foreach (bool totalLimit in new[] { false, true, })
        {
            var options = totalLimit ? new ReadOptions { MaxTotalBytesRead = 256, } : new ReadOptions { MaxStringBytes = 10, };

            // Complete attaches the cursor's original-input offset to the categorized failure.
            CStructReadLimitException failure = Assert.Throws<CStructReadLimitException>(() =>
            {
                var cursor = new ReadCursor(source, options, "root") { Position = 1, };
                try
                {
                    cursor.TakeTerminatedString(TerminatedTextEncoding.Ascii, '\0', "text", "string");
                }
                catch (CStructException exception)
                {
                    cursor.Complete(exception);
                    throw;
                }
            });
            Assert.AreEqual(totalLimit ? 302L : 12L, failure.Offset);
            Assert.AreEqual("text", failure.Member);
            Assert.AreEqual("root", failure.Path);
            StringAssert.Contains(failure.Message, totalLimit ? "total read-byte limit" : "configured encoded-byte limit");
        }
    }

    /// <summary>Fixed Latin-1 and wide buffers preserve or trim only trailing NUL padding according to the caller's option.</summary>
    [TestMethod]
    public void FixedBuffers_RespectEncodingAndPaddingOptions()
    {
        foreach (bool trim in new[] { false, true, })
        {
            var options = new ReadOptions { TrimFixedText = trim, };
            var narrow = new ReadCursor(new byte[] { 0xE9, 0, 65, 0, }, options);
            Assert.AreEqual(trim ? "é\0A" : "é\0A\0", narrow.TakeFixedText(4, "text", "char"));
            Assert.AreEqual(4, narrow.Position);
            foreach (bool littleEndian in new[] { false, true, })
            {
                var encoding = new UnicodeEncoding(!littleEndian, false, true);
                byte[] source = encoding.GetBytes("é\0A\0");
                var wide = new ReadCursor(source, options);
                Assert.AreEqual(trim ? "é\0A" : "é\0A\0", wide.TakeWideText(4, littleEndian, "text", "wchar"));
                Assert.AreEqual(8, wide.Position);
            }
        }

        CStructReadException invalid = Assert.Throws<CStructReadException>(() => new ReadCursor(new byte[] { 0, 0xD8, }).TakeWideText(1, true, "text", "wchar"));
        StringAssert.Contains(invalid.Message, "Wide-character buffer contains an invalid UTF-16 code-unit sequence");
        Assert.IsInstanceOfType<EncoderFallbackException>(invalid.InnerException);
    }

    /// <summary>Encoded buffers distinguish invalid bytes, declared capacity, string limits and total-byte limits.</summary>
    [TestMethod]
    public void EncodedBuffers_ReportTheirDistinctFailureCauses()
    {
        var exact = new ReadCursor(new byte[] { 0xC3, 0xA9, }, new ReadOptions { MaxStringBytes = 2, MaxTotalBytesRead = 2, });
        Assert.AreEqual("é", exact.TakeEncodedText(2, "utf8", "text", "utf8"));
        Assert.AreEqual(2, exact.Position);
        CStructReadException invalid = Assert.Throws<CStructReadException>(() => new ReadCursor(new byte[] { 0xFF, }, path: "root").TakeEncodedText(1, "utf8", "text", "utf8"));
        Assert.AreEqual("text", invalid.Member);
        Assert.AreEqual("root", invalid.Path);
        Assert.IsNotNull(invalid.InnerException);
        CStructReadException shortRead = Assert.Throws<CStructReadException>(() => new ReadCursor(new byte[2]).TakeBoundedText(3, "text", "utf8"));
        StringAssert.Contains(shortRead.Message, "Not enough bytes for the declared Encoded text buffer");
        Assert.Throws<CStructReadLimitException>(() => new ReadCursor(new byte[3], new ReadOptions { MaxStringBytes = 2, }).TakeBoundedText(3, "text", "utf8"));
        Assert.Throws<CStructReadLimitException>(() => new ReadCursor(new byte[2], new ReadOptions { MaxTotalBytesRead = 1, }).TakeBoundedText(3, "text", "utf8"));
    }

    /// <summary>Chunked strings keep decoder state across UTF-8 or UTF-16 boundaries and stop immediately after their terminator.</summary>
    /// <param name="kind">The generated reader's supported terminated encoding.</param>
    [TestMethod]
    [DataRow(TerminatedTextEncoding.Ascii)]
    [DataRow(TerminatedTextEncoding.Utf8)]
    [DataRow(TerminatedTextEncoding.Utf16LittleEndian)]
    [DataRow(TerminatedTextEncoding.Utf16BigEndian)]
    public void TerminatedText_CrossesChunksWithoutLosingDecoderState(TerminatedTextEncoding kind)
    {
        Encoding encoding = EncodingFor(kind);
        string expected = kind switch
        {
            TerminatedTextEncoding.Ascii => new string('x', 255) + "Z",
            TerminatedTextEncoding.Utf8 => new string('x', 255) + "é",
            _ => new string('x', 127) + "😀",
        };
        byte[] payload = encoding.GetBytes(expected + '\0');
        byte[] source = [99, .. payload, 77,];
        var cursor = new ReadCursor(source, new ReadOptions { MaxStringBytes = payload.Length, }) { Position = 1, };
        Assert.AreEqual(expected, cursor.TakeTerminatedString(kind, '\0', "text", "string"));
        Assert.AreEqual(1 + payload.Length, cursor.Position);
        Assert.AreEqual((byte)77, cursor.Take(1, "tail", "uint8")[0]);

        // A limit one byte short fails at the byte crossing it, in original input coordinates after the prefix.
        CStructReadLimitException failure = Assert.Throws<CStructReadLimitException>(() =>
        {
            var limited = new ReadCursor(source, new ReadOptions { MaxStringBytes = payload.Length - 1, }, "root") { Position = 1, };
            try
            {
                limited.TakeTerminatedString(kind, '\0', "text", "string");
            }
            catch (CStructException exception)
            {
                limited.Complete(exception);
                throw;
            }
        });
        Assert.AreEqual((long)payload.Length + 1, failure.Offset);
        Assert.AreEqual("text", failure.Member);
        Assert.AreEqual("root", failure.Path);
        StringAssert.Contains(failure.Message, "configured encoded-byte limit");
    }

    /// <summary>An empty string is valid, while an unfinished multibyte character at the next chunk's terminator keeps its categorized encoding failure.</summary>
    [TestMethod]
    public void TerminatedText_FlushesIncompleteCharactersAtTheTerminator()
    {
        var empty = new ReadCursor(new byte[] { 0, 9, }, new ReadOptions { MaxStringBytes = 1, });
        Assert.AreEqual(string.Empty, empty.TakeTerminatedString(TerminatedTextEncoding.Utf8, '\0', "text", "string"));
        Assert.AreEqual(1, empty.Position);
        byte[] invalid = [.. Enumerable.Repeat((byte)'x', 255), 0xC3, 0,];
        CStructReadException failure = Assert.Throws<CStructReadException>(() => new ReadCursor(invalid).TakeTerminatedString(TerminatedTextEncoding.Utf8, '\0', "text", "string"));
        StringAssert.Contains(failure.Message, "String field contains bytes that are invalid for its encoding");
        Assert.IsInstanceOfType<DecoderFallbackException>(failure.InnerException);
        CStructReadException missing = Assert.Throws<CStructReadException>(() => new ReadCursor(new byte[] { 65, }).TakeTerminatedString(TerminatedTextEncoding.Ascii, '\0', "text", "string"));
        StringAssert.Contains(missing.Message, "terminator");
        Assert.Throws<CStructReadLimitException>(() => new ReadCursor(new byte[] { 65, 0, }, new ReadOptions { MaxTotalBytesRead = 1, }).TakeTerminatedString(TerminatedTextEncoding.Ascii, '\0', "text", "string"));
    }

    /// <summary>Cancellation after constructing the cursor still ends a terminated read before consuming bytes.</summary>
    [TestMethod]
    public void TerminatedText_ObservesCancellationBeforeReading()
    {
        using var cancelled = new CancellationTokenSource();
        var cursor = new ReadCursor(new byte[] { 65, 0, }, new ReadOptions { CancellationToken = cancelled.Token, });
        cancelled.Cancel();
        try
        {
            cursor.TakeTerminatedString(TerminatedTextEncoding.Ascii, '\0', "text", "string");
            Assert.Fail("A cancelled text read must not succeed.");
        }
        catch (OperationCanceledException)
        {
            Assert.AreEqual(0, cursor.Position);
        }
    }

    /// <summary>Builds strict fixture bytes independently from the generated reader's codec table.</summary>
    /// <param name="kind">The encoding to exercise.</param>
    /// <returns>A strict BCL encoding with no byte-order mark.</returns>
    private static Encoding EncodingFor(TerminatedTextEncoding kind)
    {
        return kind switch
        {
            TerminatedTextEncoding.Ascii => Encoding.GetEncoding("us-ascii", EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            TerminatedTextEncoding.Utf8 => new UTF8Encoding(false, true),
            TerminatedTextEncoding.Utf16LittleEndian => new UnicodeEncoding(false, false, true),
            _ => new UnicodeEncoding(true, false, true),
        };
    }
}
