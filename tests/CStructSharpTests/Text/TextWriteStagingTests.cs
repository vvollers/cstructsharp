namespace CStructSharp.Tests;

using System.Buffers;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Checks complete text writes and the original failure and callback traces before and after staging changes.</summary>
[TestClass]
public class TextWriteStagingTests
{
    /// <summary>Every output form keeps complete encoded bytes across the stack/array boundary and with padding.</summary>
    /// <param name="type">The text encoding, including both wide-character byte orders.</param>
    /// <returns>A task that completes after the async output has also been verified.</returns>
    [TestMethod]
    [DataRow("utf8")]
    [DataRow("latin1")]
    [DataRow("cp437")]
    [DataRow("utf16le")]
    [DataRow("utf16be")]
    [DataRow("wchar<")]
    [DataRow("wchar>")]
    public async Task CompleteText_AllDestinations(string type)
    {
        foreach (int capacity in new[] { 0, 8, 256, 257, 511, 512, 513, 514, 4096, })
        {
            if (BoundedTextCodec.IsUtf16(type) && (capacity & 1) != 0)
            {
                continue;
            }

            foreach (string text in new[] { string.Empty, "é\0", new string('A', capacity / 2), new string('A', capacity), })
            {
                int bytes = type.StartsWith("wchar", StringComparison.Ordinal) ? capacity * 2 : capacity;
                byte[] payload = Encode(type, text);
                if (payload.Length > bytes)
                {
                    continue;
                }

                byte[] expected = new byte[bytes + 2];
                expected[0] = 99;
                expected[^1] = 77;
                payload.CopyTo(expected, 1);
                var layout = Layout(type, capacity);
                var value = new Dictionary<string, object> { ["head"] = (byte)99, ["text"] = text, ["tail"] = (byte)77, };
                byte[] owned = layout.Serialize("root", value);
                CollectionAssert.AreEqual(expected, owned);
                byte[] target = Enumerable.Repeat((byte)0xCC, expected.Length + 3).ToArray();
                Assert.AreEqual(expected.Length, layout.Serialize(target.AsSpan(0, expected.Length), "root", value));
                CollectionAssert.AreEqual(expected, target[..expected.Length]);
                CollectionAssert.AreEqual(new byte[] { 0xCC, 0xCC, 0xCC, }, target[expected.Length..]);
                var writer = new ArrayBufferWriter<byte>();
                Assert.AreEqual((long)expected.Length, layout.Serialize(writer, "root", value));
                CollectionAssert.AreEqual(expected, writer.WrittenSpan.ToArray());
                using var stream = new MemoryStream();
                stream.WriteByte(13);
                layout.Write(stream, "root", value);
                CollectionAssert.AreEqual((byte[])[13, .. expected,], stream.ToArray());
                using var asynchronous = new MemoryStream();
                await layout.WriteAsync(asynchronous, "root", value);
                CollectionAssert.AreEqual(expected, asynchronous.ToArray());
                CollectionAssert.AreEqual(expected, owned);
            }
        }
    }

    /// <summary>Preserves reviewed baseline diagnostics, mutation and callback traces across competing failures.</summary>
    /// <param name="type">The bounded or wide text type.</param>
    [TestMethod]
    [DataRow("utf8")]
    [DataRow("latin1")]
    [DataRow("cp437")]
    [DataRow("utf16le")]
    [DataRow("utf16be")]
    [DataRow("wchar<")]
    [DataRow("wchar>")]
    public void TextFailures_KeepBaselineTraces(string type)
    {
        foreach (int capacity in new[] { 0, 3, 8, })
        {
            var layout = Layout(type, capacity);
            foreach (string text in new[] { string.Empty, "é\0", "世界😀", "A\uD800", "\uDC00", "😀A\uD800", })
            {
                using IDisposable part = EngineGolden.Part($"capacity={capacity}, text={Convert.ToHexString(Encoding.Unicode.GetBytes(text))}");
                foreach (int budget in new[] { 0, 1, 4, 32, })
                {
                    foreach (int stringLimit in new[] { 0, 6, 32, })
                    {
                        var options = new WriteOptions { MaxTotalBytesWritten = budget, MaxStringBytes = stringLimit, };
                        foreach (int failure in new[] { -1, 0, 2, })
                        {
                            EngineGolden.Check($"stream budget={budget}, string={stringLimit}, failure={failure}", ObserveStream(layout, text, options, failure));
                        }

                        EngineGolden.Check($"span budget={budget}, string={stringLimit}", ObserveSpan(layout, text, options));
                        if (BoundedTextCodec.IsType(type))
                        {
                            EngineGolden.Check($"generated budget={budget}, string={stringLimit}", ObserveGenerated(type, capacity, text, options));
                        }
                    }
                }
            }
        }
    }

    /// <summary>A payload survives a reentrant serialization and preserves a destination's exact failure.</summary>
    /// <param name="fail">Whether the destination rejects the payload after the nested operation.</param>
    /// <param name="length">The payload size, covering both stack and array staging.</param>
    [TestMethod]
    [DataRow(false, 0)]
    [DataRow(true, 0)]
    [DataRow(false, 511)]
    [DataRow(true, 511)]
    [DataRow(false, 512)]
    [DataRow(true, 512)]
    [DataRow(false, 513)]
    [DataRow(true, 513)]
    [DataRow(false, 1024)]
    [DataRow(true, 1024)]
    public void BoundedPayload_SurvivesReentryAndFailure(bool fail, int length)
    {
        var layout = new CStruct($"struct root {{ utf8 text[{length}]; }};");
        var value = new Dictionary<string, object> { ["text"] = new string('A', length), };
        layout.Serialize("root", value);
        using var destination = new ReentrantStream(layout, fail);
        if (fail)
        {
            // The outer stream failure must retain its exact original cause after the nested operation.
            CStructWriteException exception = Assert.ThrowsExactly<CStructWriteException>(() => layout.Write(destination, "root", value));
            Assert.AreSame(destination.Failure, exception.InnerException);
        }
        else
        {
            layout.Write(destination, "root", value);
            CollectionAssert.AreEqual(Enumerable.Repeat((byte)'A', length).ToArray(), destination.ToArray());
        }
    }

    /// <summary>Creates a packed text field between two markers.</summary>
    /// <param name="type">The field type.</param>
    /// <param name="capacity">Declared capacity in characters or bytes.</param>
    /// <returns>The compiled layout.</returns>
    private static CStruct Layout(string type, int capacity)
        => new($"struct root {{ uint8 head; {type} text[{capacity}]; uint8 tail; }};", aligned: false);

    /// <summary>Uses the original allocating codec to construct independently owned expected bytes.</summary>
    /// <param name="type">The text type.</param>
    /// <param name="text">The complete supplied text.</param>
    /// <returns>Its encoded payload without padding.</returns>
    private static byte[] Encode(string type, string text) => type switch
    {
        "wchar<" => new UnicodeEncoding(false, false, true).GetBytes(text),
        "wchar>" => new UnicodeEncoding(true, false, true).GetBytes(text),
        _ => BoundedTextCodec.Encode(type, text),
    };

    /// <summary>Records stream writes at a nonzero origin, including partial writes and a stream-thrown encoding exception.</summary>
    /// <param name="layout">The prepared layout.</param>
    /// <param name="text">The supplied field value.</param>
    /// <param name="options">The competing limits.</param>
    /// <param name="failure">Minus one for success; otherwise bytes accepted in the second write before throwing.</param>
    /// <returns>All callback bytes, final position and full failure metadata.</returns>
    private static string ObserveStream(CStruct layout, string text, WriteOptions options, int failure)
    {
        using var stream = new TraceStream(failure);
        string result = "ok";
        try
        {
            layout.Write(stream, "root", new Dictionary<string, object> { ["head"] = (byte)99, ["text"] = text, ["tail"] = (byte)77, }, options: options);
        }
        catch (Exception exception)
        {
            result = Describe(exception);
        }

        return $"{result}|{stream.Position}|{Convert.ToHexString(stream.ToArray())}|{string.Join(';', stream.Calls)}";
    }

    /// <summary>Records caller-owned memory, including bytes left unchanged after failure.</summary>
    /// <param name="layout">The prepared layout.</param>
    /// <param name="text">The supplied text.</param>
    /// <param name="options">The competing limits.</param>
    /// <returns>The complete destination and result or failure.</returns>
    private static string ObserveSpan(CStruct layout, string text, WriteOptions options)
    {
        byte[] target = Enumerable.Repeat((byte)0xCC, 8).ToArray();
        string result;
        try
        {
            result = "ok:" + layout.Serialize(target.AsSpan(), "root", new Dictionary<string, object> { ["head"] = (byte)99, ["text"] = text, ["tail"] = (byte)77, }, options: options);
        }
        catch (Exception exception)
        {
            result = Describe(exception);
        }

        return result + "|" + Convert.ToHexString(target);
    }

    /// <summary>Records the generated helper's own mutation and diagnostics independently of runtime failure precedence.</summary>
    /// <param name="type">The bounded encoding.</param>
    /// <param name="capacity">The byte capacity.</param>
    /// <param name="text">The immutable input.</param>
    /// <param name="options">The competing limits.</param>
    /// <returns>The final cursor, full destination and diagnostic.</returns>
    private static string ObserveGenerated(string type, int capacity, string text, WriteOptions options)
    {
        byte[] target = Enumerable.Repeat((byte)0xCC, 8).ToArray();
        var cursor = new WriteCursor(target, options, "root");
        string result = "ok";
        try
        {
            cursor.Pad(1, "head", "uint8");
            cursor.WriteBoundedText(capacity, type, text, "text", type);
            cursor.Pad(1, "tail", "uint8");
        }
        catch (Exception exception)
        {
            result = Describe(exception);
        }

        return $"{result}|{cursor.Position}|{Convert.ToHexString(target)}";
    }

    /// <summary>Includes contextual and encoding-specific fields rather than comparing only exception messages.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>Its recursively complete relevant diagnostic.</returns>
    private static string Describe(Exception exception)
    {
        string detail = exception switch
        {
            CStructException context => $"{context.Offset}|{context.Path}|{context.Member}|{context.MemberType}",
            EncoderFallbackException encoder => $"{encoder.Index}|{(int)encoder.CharUnknown}|{(int)encoder.CharUnknownHigh}|{(int)encoder.CharUnknownLow}|{encoder.IsUnknownSurrogate()}",
            _ => string.Empty,
        };
        return $"{exception.GetType().Name}|{exception.Message}|{detail}|{(exception.InnerException is null ? string.Empty : Describe(exception.InnerException))}";
    }

    /// <summary>Checks that a nested write cannot overwrite the outer payload.</summary>
    private sealed class ReentrantStream : MemoryStream
    {
        private readonly CStruct layout;
        private readonly bool fail;

        /// <summary>Stores the reusable layout and failure mode.</summary>
        /// <param name="layout">The layout for the nested serialization.</param>
        /// <param name="fail">Whether to reject the outer write.</param>
        public ReentrantStream(CStruct layout, bool fail)
        {
            this.layout = layout;
            this.fail = fail;
        }

        /// <summary>Gets the exact failure passed through the write adapter.</summary>
        public IOException Failure { get; } = new("reentrant destination failure");

        /// <summary>Reenters serialization and verifies that every outer byte survived.</summary>
        /// <param name="buffer">The complete encoded outer payload.</param>
        /// <exception cref="IOException">The configured destination failure after the reentrant call.</exception>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            int length = buffer.Length;
            byte[] nested = this.layout.Serialize("root", new Dictionary<string, object> { ["text"] = new string('Z', length), });
            CollectionAssert.AreEqual(Enumerable.Repeat((byte)'Z', length).ToArray(), nested);
            CollectionAssert.AreEqual(Enumerable.Repeat((byte)'A', length).ToArray(), buffer.ToArray());
            if (this.fail)
            {
                throw this.Failure;
            }

            base.Write(buffer);
        }
    }

    /// <summary>Observes both stream overloads and can fail during the field payload, after accepting a prefix.</summary>
    private sealed class TraceStream : MemoryStream
    {
        private readonly int failure;
        private int spans;

        /// <summary>Creates a stream with one prefix byte and a configured second-write failure.</summary>
        /// <param name="failure">Minus one disables failure; zero throws an encoding exception, two an I/O exception.</param>
        public TraceStream(int failure)
        {
            this.failure = failure;
            this.WriteByte(13);
        }

        /// <summary>Gets callback overloads and all requested bytes.</summary>
        public List<string> Calls { get; } = [];

        /// <inheritdoc/>
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            this.Calls.Add("span:" + Convert.ToHexString(buffer));
            if (++this.spans == 2 && this.failure >= 0)
            {
                base.Write(buffer[..Math.Min(this.failure, buffer.Length)]);
                if (this.failure == 0)
                {
                    throw new EncoderFallbackException("from destination");
                }

                throw new IOException("partial destination write");
            }

            base.Write(buffer);
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            this.Calls.Add("array:" + Convert.ToHexString(buffer, offset, count));
            base.Write(buffer, offset, count);
        }
    }
}
