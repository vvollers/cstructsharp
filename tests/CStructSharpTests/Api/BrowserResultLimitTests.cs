namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharpWeb.Wasm;

/// <summary>
///     The browser bridge's result-length guard and its segmented growth. The bridge's JSON writer is compiled into
///     this project (the WebAssembly project itself has no managed test host), so these tests lower the writer's
///     maximum length and segment length to a few bytes. They check that an output of exactly the maximum passes, one
///     byte more fails with a read-limit error, the failure stops the writer early instead of letting the buffer grow
///     without bound, and an output spread over several segments is byte-identical to a contiguous one.
/// </summary>
[TestClass]
public class BrowserResultLimitTests
{
    /// <summary>Output of exactly the maximum length passes, and doubling stops at the limit.</summary>
    [TestMethod]
    public void OutputOfExactlyTheMaximum_PassesAndCapsTheBuffer()
    {
        var writer = new InteropJsonWriter(4, maximumLength: 16);

        writer.WriteRawBytes("0123456789abcdef"u8);
        writer.EnsureWithinLimit();

        Assert.AreEqual("0123456789abcdef", writer.ToUtf8String());
        Assert.AreEqual(16, writer.Capacity);
    }

    /// <summary>One byte past the maximum is a read-limit failure whose message states the limit.</summary>
    [TestMethod]
    public void OutputOneBytePastTheMaximum_FailsWithReadLimit()
    {
        var writer = new InteropJsonWriter(64, maximumLength: 16);
        writer.WriteRawBytes("0123456789abcdefg"u8);

        CStructReadLimitException exception = Assert.Throws<CStructReadLimitException>(() => writer.EnsureWithinLimit());

        Assert.AreEqual(CStructErrorCode.ReadLimitExceeded, exception.Code);
        StringAssert.StartsWith(exception.Message, "The result's JSON text exceeds 16 bytes");
    }

    /// <summary>Once the written output passes the maximum, the next growth fails instead of copying the buffer again.</summary>
    [TestMethod]
    public void GrowthAfterPassingTheMaximum_FailsEarly()
    {
        var writer = new InteropJsonWriter(4, maximumLength: 16);
        writer.WriteRawBytes("0123456789abcdefg"u8);

        Assert.Throws<CStructReadLimitException>(() => writer.WriteRawBytes("h"u8));
    }

    /// <summary>
    ///     A string reserves its worst-case escaped length; a reservation that crosses the limit still succeeds when
    ///     the bytes actually written fit.
    /// </summary>
    [TestMethod]
    public void ReservationPastTheMaximum_PassesWhenTheWrittenBytesFit()
    {
        var writer = new InteropJsonWriter(4, maximumLength: 16);

        writer.WriteString("abcdefghijklmn");
        writer.EnsureWithinLimit();

        Assert.AreEqual("\"abcdefghijklmn\"", writer.ToUtf8String());
    }

    /// <summary>Resetting after an oversized output makes the writer usable for the failure envelope that follows.</summary>
    [TestMethod]
    public void ResetAfterOversizedOutput_AcceptsTheNextOutput()
    {
        var writer = new InteropJsonWriter(4, maximumLength: 16);
        writer.WriteRawBytes("0123456789abcdefg"u8);

        writer.Reset();
        writer.WriteNull();
        writer.EnsureWithinLimit();

        Assert.AreEqual("null", writer.ToUtf8String());
    }

    /// <summary>
    ///     Non-ASCII text is escaped, so the output stays ASCII and its byte count is the length of the JavaScript
    ///     string it decodes to - the quantity the limit bounds.
    /// </summary>
    [TestMethod]
    public void NonAsciiText_IsWrittenAsAsciiEscapes()
    {
        var writer = new InteropJsonWriter(4, maximumLength: 64);

        writer.WriteString("café ☃ \U0001F600");

        Assert.AreEqual("\"caf\\u00e9 \\u2603 \\ud83d\\ude00\"", writer.ToUtf8String());
        Assert.IsTrue(writer.ToArray().AsSpan().IndexOfAnyInRange((byte)0x80, (byte)0xFF) < 0);
    }

    /// <summary>
    ///     An output that fits one segment stays in one doubling array; the first byte past it starts a second segment
    ///     instead of doubling again, and a reset drops the earlier segments.
    /// </summary>
    [TestMethod]
    public void OutputPastOneSegment_ContinuesInANewSegment()
    {
        var writer = new InteropJsonWriter(4, maximumLength: 1024, segmentLength: 16);

        writer.WriteRawBytes("0123456789abcdef"u8);
        Assert.AreEqual(16, writer.Capacity, "one array of the segment length");

        writer.WriteRawBytes("g"u8);
        Assert.AreEqual(32, writer.Capacity, "a second 16-byte segment, not a 32-byte copy");
        Assert.AreEqual("0123456789abcdefg", writer.ToUtf8String());

        writer.Reset();
        writer.WriteNull();
        Assert.AreEqual(16, writer.Capacity, "only the current segment remains");
        Assert.AreEqual("null", writer.ToUtf8String());
    }

    /// <summary>
    ///     A segmented output is byte-identical to the contiguous output of the same writes. Short segments put many
    ///     boundaries inside strings, escapes, and numbers, and a string longer than a segment gets a segment of its
    ///     own.
    /// </summary>
    [TestMethod]
    public void SegmentedOutput_IsByteIdenticalToContiguousOutput()
    {
        var contiguous = new InteropJsonWriter(4);
        var segmented = new InteropJsonWriter(4, segmentLength: 8);

        foreach (InteropJsonWriter writer in new[] { contiguous, segmented, })
        {
            writer.WriteRawBytes("["u8);
            for (int index = 0; index < 200; index++)
            {
                writer.WriteRawBytes(index == 0 ? "{\"i\":"u8 : ",{\"i\":"u8);
                writer.WriteSafeInteger(index * 7919L);
                writer.WriteRawBytes(",\"s\":"u8);
                writer.WriteString(index % 3 == 0 ? $"plain {index}" : $"esc\"aped\n{index} café ☃");
                writer.WriteRawBytes(",\"v\":"u8);
                writer.WriteValue(index % 2 == 0 ? index / 4.0 : null);
                writer.WriteRawBytes("}"u8);
            }

            writer.WriteRawBytes(","u8);
            writer.WriteString(new string('x', 40));
            writer.WriteRawBytes("]"u8);
            writer.EnsureWithinLimit();
        }

        CollectionAssert.AreEqual(contiguous.ToArray(), segmented.ToArray());
        Assert.AreEqual(contiguous.ToUtf8String(), segmented.ToUtf8String());
        Assert.IsTrue(segmented.Capacity > 8, "the output spans several segments");
    }

    /// <summary>
    ///     The maximum length counts the bytes in every segment: output of exactly the maximum passes, one byte more is
    ///     a read-limit failure, and growth after passing it fails early.
    /// </summary>
    [TestMethod]
    public void SegmentedOutput_EnforcesTheMaximumAcrossSegments()
    {
        var writer = new InteropJsonWriter(4, maximumLength: 40, segmentLength: 8);
        for (int index = 0; index < 4; index++)
        {
            writer.WriteRawBytes("0123456789"u8);
        }

        writer.EnsureWithinLimit();
        Assert.AreEqual(40, writer.ToArray().Length);

        writer.WriteRawBytes("x"u8);
        CStructReadLimitException exception = Assert.Throws<CStructReadLimitException>(() => writer.EnsureWithinLimit());
        StringAssert.StartsWith(exception.Message, "The result's JSON text exceeds 40 bytes");
        Assert.Throws<CStructReadLimitException>(() => writer.WriteRawBytes("0123456789"u8));
    }
}
