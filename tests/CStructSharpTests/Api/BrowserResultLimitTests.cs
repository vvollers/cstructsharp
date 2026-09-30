namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharpWeb.Wasm;

/// <summary>
///     The browser bridge's result-length guard. The bridge's JSON writer is compiled into this project (the WebAssembly
///     project itself has no managed test host), so these tests lower the writer's maximum length to a few bytes and
///     check that an output of exactly the maximum passes, one byte more fails with a read-limit error, and the
///     failure stops the writer early instead of letting the buffer grow without bound.
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

        Assert.AreEqual("0123456789abcdef", Encoding.ASCII.GetString(writer.WrittenSpan));
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

        Assert.AreEqual("\"abcdefghijklmn\"", Encoding.ASCII.GetString(writer.WrittenSpan));
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

        Assert.AreEqual("null", Encoding.ASCII.GetString(writer.WrittenSpan));
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

        Assert.AreEqual("\"caf\\u00e9 \\u2603 \\ud83d\\ude00\"", Encoding.ASCII.GetString(writer.WrittenSpan));
        Assert.IsTrue(writer.WrittenSpan.IndexOfAnyInRange((byte)0x80, (byte)0xFF) < 0);
    }
}
