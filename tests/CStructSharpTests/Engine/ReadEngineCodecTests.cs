namespace CStructSharp.Tests;

using CStructSharp.Engine;

/// <summary>
///     The compiled read engine's codecs where the sweeps and corpora do not reach: every codec the engine reads element
///     by element at every truncation and byte budget, terminated and unsized character text, and callers' custom
///     codecs from every source and with a declared size. Each case checks its outcomes against the golden outcomes
///     (<see cref="EngineGolden"/>) under both execution paths, and pins the expected outcome.
/// </summary>
[TestClass]
public class ReadEngineCodecTests
{
    /// <summary>A packed layout with one scalar or array of every codec the engine reads element by element (<c>ReadEngine.ReadCodecValue</c>).</summary>
    private const string CodecLayout = """
        enum e16 : uint16 { A = 1, B = 4660 };
        struct rec {
          int48 a; uint48 b; int128 c; uint128 d; float16 h;
          fixed16_16 f1; ufixed16_16 f2; fixed2_30 f3; ufixed8_8 f4;
          uuid u; guid g;
          uleb128_32 l1; uleb128_64 l2; sleb128_32 l3; sleb128_64 l4;
          char ch; latin1 la; cp437 cp; utf8 u8; wchar w;
          e16 en; e16 ens[2]; int48 as[2]; wchar ws[2]; char cs[3]; utf8 text[4]; cstring s;
          uint8 tail;
        };
        """;

    /// <summary>Gets valid input for <see cref="CodecLayout"/>, field by field.</summary>
    private static byte[] CodecData =>
    [
        0xFB, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // a = -5
        0x07, 0, 0, 0, 0, 0, // b = 7
        0xF7, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // c = -9
        0x0A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // d = 10
        0x00, 0x3E, // h = 1.5
        0x00, 0x80, 0x01, 0x00, // f1 = 1.5
        0x00, 0x40, 0x02, 0x00, // f2 = 2.25
        0x00, 0x00, 0x00, 0x20, // f3 = 0.5
        0x80, 0x03, // f4 = 3.5
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, // u
        16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, // g
        0xAC, 0x02, // l1 = 300
        0xF0, 0xA2, 0x04, // l2 = 70000
        0x7D, // l3 = -3
        0xD4, 0x7D, // l4 = -300
        0x41, 0xE9, 0x80, 0x7A, // ch, la, cp, u8
        0xA9, 0x03, // w = U+03A9
        0x34, 0x12, // en = B
        0x01, 0x00, 0x34, 0x12, // ens
        1, 0, 0, 0, 0, 0, 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, // as = 1, -2
        0x41, 0x00, 0x42, 0x00, // ws = "AB"
        0x78, 0x79, 0x00, // cs
        0x68, 0x69, 0x00, 0x00, // text
        0x6F, 0x6B, 0x00, // s = "ok"
        0x09, // tail
    ];

    /// <summary>
    ///     Every codec the engine reads through its reader (wide integers, float16, fixed-point, UUIDs, LEB128, character
    ///     units, <c>wchar</c>, enum storage, their arrays, bounded and terminated text) reads its golden outcome at every
    ///     truncation from memory and from streams that return one or seven bytes per read, and at every byte budget.
    /// </summary>
    [TestMethod]
    public void EveryCodec_ReadsAtEveryTruncationAndBudget()
    {
        var layout = new CStruct(CodecLayout);
        byte[] data = CodecData;
        string complete;
        using (EngineRecording recording = EngineDiagnostics.Record())
        {
            complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"));
            Assert.AreEqual(1, recording.Diagnostics.Runs, "the parse runs once on the engine");
        }

        StringAssert.Contains(complete, "result.a = Int64 -5\n");
        StringAssert.Contains(complete, "result.l4 = Int64 -300\n");
        StringAssert.Contains(complete, "result.w = Char");
        StringAssert.Contains(complete, "result.s = String \"ok\"\n");
        StringAssert.Contains(complete, "result.tail = Byte 9\n");

        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1, EngineInput.ChunkedStream7])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                }
            }

            for (long budget = 1; budget <= data.Length + 1; budget++)
            {
                var read = new ReadOptions { MaxTotalBytesRead = budget, };
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.ChunkedStream3, "rec", options: read), path: path);
            }

            // Invalid values fail inside their own codecs: an unterminated LEB128, invalid UTF-16, an invalid bounded text.
            byte[] invalid = (byte[])data.Clone();
            invalid[92] = 0x80;
            invalid[93] = 0x80;
            invalid[94] = 0x80;
            invalid[95] = 0x80;
            invalid[96] = 0x80;
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, invalid, EngineInput.Span, "rec"), path: path);
            invalid = (byte[])data.Clone();
            invalid[127] = 0xD8;
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, invalid, EngineInput.Stream, "rec"), path: path);
            invalid = (byte[])data.Clone();
            invalid[133] = 0xFF;
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, invalid, EngineInput.Span, "rec"), path: path);
        }
    }

    /// <summary>
    ///     Terminated strings read from memory in place charge the chunks the chunked reader takes and end just after
    ///     the terminator: a string past one 256-byte chunk, UTF-16 text, every byte budget, string limits around the
    ///     text's length, every truncation, and invalid text all read as the golden outcomes record, from memory and from streams.
    /// </summary>
    [TestMethod]
    public void TerminatedStrings_ReadInPlaceLikeTheChunkedReader()
    {
        var layout = new CStruct("struct rec { uint8 a; cstring s; string w; uint8 tail; };", isLittleEndian: true);
        byte[] data = [7, .. Enumerable.Repeat((byte)'x', 300), 0, (byte)'h', 0, (byte)'i', 0, 0, 0, 9];
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.w = String \"hi\"\n");
            StringAssert.Contains(complete, "result.tail = Byte 9\n");
            for (long budget = 1; budget <= data.Length + 260; budget++)
            {
                var read = new ReadOptions { MaxTotalBytesRead = budget, };
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: read), path: path);
            }

            foreach (int limit in (int[])[0, 4, 5, 6, 255, 256, 257, 300, 301, 302])
            {
                var read = new ReadOptions { MaxStringBytes = limit, };
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.ChunkedStream7, "rec", options: read), path: path);
            }

            for (int length = 0; length <= data.Length; length++)
            {
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], EngineInput.Span, "rec"), path: path);
            }

            byte[] invalid = (byte[])data.Clone();
            invalid[270] = 0xC3;
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, invalid, EngineInput.Span, "rec"), path: path);
            invalid = (byte[])data.Clone();
            invalid[304] = 0xD8;
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, invalid, EngineInput.Span, "rec"), path: path);
        }
    }

    /// <summary>
    ///     An unsized array of a <c>wchar</c> typedef is terminated text, not a terminated array (so no terminated array has
    ///     character elements): it reads up to its terminator as a string, and invalid text fails once the chunk holding
    ///     the terminator was read, from every source and at every truncation.
    /// </summary>
    [TestMethod]
    public void UnsizedTypedefCharacters_ReadAsTerminatedText()
    {
        var layout = new CStruct("typedef wchar w16; struct rec { w16 text[]; uint8 tail; };");
        byte[] data = [(byte)'h', 0, (byte)'i', 0, 0, 0, 9];
        byte[] invalid = [0x00, 0xD8, (byte)'i', 0, 0, 0, 9];
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
            {
                string valid = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec"), path: path);
                StringAssert.Contains(valid, "result.tail = Byte 9\n");
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, invalid, input, "rec"), path: path);
                for (int length = 0; length <= data.Length; length++)
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                }
            }

            string failed = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, invalid, EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(failed, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(failed, "failure.member = \"text\"\n");
            StringAssert.Contains(failed, "position = 7\n");
        }
    }

    /// <summary>
    ///     Caller-supplied codecs run through one adapter on every path: from memory the
    ///     codec sees the whole remainder and the position advances, and is charged, before a failure - by the whole
    ///     remainder when the codec needs more data; from a stream through a window that doubles from 256 bytes up to
    ///     <see cref="ReadOptions.MaxStringBytes"/>. Every truncation, byte budget and string limit, and a codec that decodes
    ///     no value, throws, or claims more bytes than it was given, reads as the golden outcomes record, from memory and from streams.
    /// </summary>
    [TestMethod]
    public void CustomCodecs_ReadThroughTheAdapterFromEverySource()
    {
        var options = new CStructCompilationOptions { Codecs = [VlqCodec.Instance, LengthPrefixedCodec.Instance, QuirkyCodec.Instance,], };
        var layout = new CStruct("struct rec { uint8 tag; vlq v; blob b; odd o[2]; uint8 tail; };", compilationOptions: options);
        byte[] data = [7, 0x80, 0x01, .. Blob(600), 5, 6, 9];
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.v = UInt32 128\n");
            StringAssert.Contains(complete, "result.b = Int32 600\n");
            StringAssert.Contains(complete, "result.tail = Byte 9\n");
            foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream7])
            {
                for (int length = 0; length <= data.Length; length++)
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                }

                foreach (int limit in (int[])[255, 256, 257, 300, 512, 513, 601, 602, 603, 1024])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxStringBytes = limit, }), path: path);
                }
            }

            for (long budget = 1; budget <= data.Length + 1; budget++)
            {
                var read = new ReadOptions { MaxTotalBytesRead = budget, };
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: read), path: path);
            }

            // A value that continues past the input: memory charges the whole remainder, so a small budget fails first.
            string needsMore = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [7, 0x80, 0x80, 0x80], EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(needsMore, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            string charged = EngineDifferential.AssertGolden(
                EngineOperations.Parse(layout, [7, 0x80, 0x80, 0x80], EngineInput.Span, "rec", options: new ReadOptions { MaxTotalBytesRead = 3, }),
                path: path);
            StringAssert.Contains(charged, "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n");

            // odd: 0 decodes to no value, 1 throws inside the codec, 2 claims more bytes than it was given.
            foreach (byte quirk in (byte[])[0, 1, 2])
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    string outcome = EngineDifferential.AssertGolden(
                        EngineOperations.Parse(layout, [7, 0x05, 0x00, 0x00, quirk, 6, 9], input, "rec"),
                        path: path);
                    StringAssert.Contains(outcome, quirk == 0 ? "failure = failure System.InvalidOperationException\n" : "failure = failure CStructSharp.Diagnostics.CStructReadException\n", "quirk " + quirk);
                }
            }
        }
    }

    /// <summary>
    ///     A caller's codec that declares four bytes and takes one still occupies four: every later member, nested struct
    ///     and tail is at the offset the layout compiled - packed and aligned, in the short and
    ///     the full form, at every truncation and byte budget.
    /// </summary>
    [TestMethod]
    public void FixedSizeCustomCodec_OccupiesItsDeclaredSize()
    {
        var options = new CStructCompilationOptions { Codecs = [FixedWordCodec.Instance,], };
        const string definition = "struct inner { word4 w; uint8 b; }; struct rec { word4 a; uint16 x; inner i; uint16 y; };";
        var packed = new CStruct(definition, compilationOptions: options);
        var aligned = new CStruct(definition, aligned: true, compilationOptions: options);
        (CStruct Layout, byte[] Data)[] cases =
        [
            (packed, [0xEE, 0xAA, 0xAA, 0xAA, 0x34, 0x12, 0xEE, 0xAA, 0xAA, 0xAA, 0x05, 0x78, 0x56]),
            (packed, [1, 0, 0, 0, 0x34, 0x12, 2, 0, 0, 0, 0x05, 0x78, 0x56]),
            (aligned, [0xEE, 0xAA, 0xAA, 0xAA, 0x34, 0x12, 0, 0, 0xEE, 0xAA, 0xAA, 0xAA, 0x05, 0, 0, 0, 0x78, 0x56, 0, 0]),
            (aligned, [1, 0, 0, 0, 0x34, 0x12, 0, 0, 2, 0, 0, 0, 0x05, 0, 0, 0, 0x78, 0x56, 0, 0]),
        ];
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            foreach ((CStruct layout, byte[] data) in cases)
            {
                string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
                StringAssert.Contains(complete, "result.x = UInt16 4660\n");
                StringAssert.Contains(complete, "result.y = UInt16 22136\n");
                for (int length = 0; length <= data.Length; length++)
                {
                    foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                    {
                        EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                    }
                }

                for (long budget = 1; budget <= data.Length + 1; budget++)
                {
                    var read = new ReadOptions { MaxTotalBytesRead = budget, };
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), path: path);
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: read), path: path);
                }

                byte[] rejected = (byte[])data.Clone();
                rejected[0] = FixedWordCodec.Invalid;
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, rejected, EngineInput.Span, "rec"), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, rejected, EngineInput.Stream, "rec"), path: path);
            }
        }
    }

    /// <summary>Encodes a <c>blob</c> (<see cref="LengthPrefixedCodec"/>) of <paramref name="length"/> zero bytes.</summary>
    /// <param name="length">The announced length.</param>
    /// <returns>The two-byte little-endian prefix followed by the bytes.</returns>
    private static byte[] Blob(int length) => [(byte)length, (byte)(length >> 8), .. new byte[length]];
}
