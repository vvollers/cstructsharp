namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Values;

/// <summary>
///     The compiled read engine's step semantics where the sweeps and corpora do not reach: every codec at every
///     truncation and byte budget, an offset assertion checked before its member is placed, cancellation only at the
///     documented boundaries, nesting limits with promoted members, the conditional variable
///     scope, qualified prefixes through static plans, and the member, path and offset a failure reports. Each case
///     compares the engine with the golden outcomes (<see cref="EngineGolden"/>) through the golden harness, and pins
///     the expected outcome.
/// </summary>
[TestClass]
public class ReadEngineTests
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

    /// <summary>The execution paths each case runs under: the fast paths in front of the engine, and the engine member by member only.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.NoFastPaths];

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

        foreach (ExecutionPath path in Paths)
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
        foreach (ExecutionPath path in Paths)
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
    ///     A runtime-checked <c>@N</c> assertion is checked before its member is placed: a failing assertion leaves the
    ///     position before the padding and names the member, and it wins over a start that lies past the input.
    /// </summary>
    [TestMethod]
    public void OffsetAssertion_IsCheckedBeforeTheMemberIsPlaced()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 d[n]; uint32 x @8; };", aligned: true);
        foreach (ExecutionPath path in Paths)
        {
            // n = 5 ends the array at 6, so x is aligned to 8 as asserted.
            string valid = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [5, 0, 0, 0, 0, 0, 0, 0, 7, 0, 0, 0], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(valid, "result.x = UInt32 7\n");

            // n = 1 aligns x to 4, not 8: the stream stays after the array, before the padding.
            string misplaced = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [1, 0, 0, 0, 7, 0, 0, 0], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(misplaced, "failure = failure CStructSharp.Diagnostics.CStructLayoutException\n");
            StringAssert.Contains(misplaced, "failure.member = \"x\"\n");
            StringAssert.Contains(misplaced, "position = 2\n");

            // The same misplaced start past the end of the input still reports the assertion.
            string past = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [1, 0], EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(past, "failure = failure CStructSharp.Diagnostics.CStructLayoutException\n");
        }
    }

    /// <summary>
    ///     Cancellation is observed at struct entries (each element of a struct array is one) and never per primitive:
    ///     a token cancelled while a struct's primitives are read ends the read at
    ///     the next struct entry, at the position the golden outcomes record; a read with no further struct entry completes.
    /// </summary>
    [TestMethod]
    public void Cancellation_IsObservedAtStructEntriesOnly()
    {
        var elements = new CStruct("struct inner { uint8 a; uint8 b; }; struct rec { uint8 n; inner items[n]; uint8 tail; };");
        byte[] data = [3, 1, 2, 3, 4, 5, 6, 9];
        foreach (ExecutionPath path in Paths)
        {
            for (int trigger = 0; trigger < data.Length; trigger++)
            {
                string outcome = EngineDifferential.AssertGolden(CancelledParse(elements, data, trigger), path: path);

                // The next struct entry observes the token: the entry of the element after the one being read, or - on
                // the fast path, which stages each element's bytes before entering it - the entry of the element whose
                // bytes were just staged.
                if (trigger <= (path == ExecutionPath.NoFastPaths ? 4 : 6))
                {
                    StringAssert.Contains(outcome, "failure = failure System.OperationCanceledException\n", "trigger " + trigger);
                }
                else
                {
                    StringAssert.Contains(outcome, "result.tail = Byte 9\n", "trigger " + trigger);
                }
            }
        }

        // Only primitives after the root's entry: member by member, the read runs to the end whenever the token is cancelled.
        var primitives = new CStruct("struct rec { uint8 a; uint16 b; uint8 c; };");
        for (int trigger = 0; trigger < 4; trigger++)
        {
            string outcome = EngineDifferential.AssertGolden(CancelledParse(primitives, [1, 2, 0, 3], trigger), path: ExecutionPath.NoFastPaths);
            StringAssert.Contains(outcome, "result.c = Byte 3\n", "trigger " + trigger);
        }
    }

    /// <summary>
    ///     Every named struct claims a nesting level, a struct-array element included, and an anonymous promoted member
    ///     claims none; the limit fails with the nesting message at exactly the depth the layout needs minus one, through
    ///     the static plans and member-by-member reads alike.
    /// </summary>
    [TestMethod]
    public void NestingLimit_CountsNamedStructsButNotPromotedMembers()
    {
        var layout = new CStruct("struct a { uint8 v; }; struct b { a x; uint8 w; }; struct rec { uint8 n; b items[n]; struct { a p; }; uint8 tail; };");
        byte[] data = [2, 1, 2, 3, 4, 5, 9];
        foreach (ExecutionPath path in Paths)
        {
            for (int depth = 1; depth <= 4; depth++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.ChunkedStream3])
                {
                    string outcome = EngineDifferential.AssertGolden(
                        EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxNestingDepth = depth, }),
                        path: path);
                    if (depth < 3)
                    {
                        // Depth 1 cannot enter the elements of items; depth 2 enters them but not their member x.
                        string member = depth == 1 ? "'items' (b)" : "'x' (a)";
                        StringAssert.Contains(outcome, "failure.message = \"" + ReadFailures.NestingLimit.TrimEnd('.') + " (field " + member, "depth " + depth);
                    }
                    else
                    {
                        StringAssert.Contains(outcome, "result.tail = Byte 9\n", "depth " + depth);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     A conditional composite's variable scope: its own names are removed on entry (a caller's value of such a name
    ///     disappears), and after each member the names a nested declaration replaced are restored, so a later count in
    ///     the composite reads the composite's own value.
    /// </summary>
    [TestMethod]
    public void ConditionalScope_RemovesAndRestoresTheCompositesNames()
    {
        var restoring = new CStruct("struct inner { uint8 n; }; struct rec { uint8 n; if (n == 1) { inner i; uint8 d[n]; } uint8 e[n]; };");
        var removing = new CStruct("struct rec { uint8 k; if (k == 1) { uint8 n; } uint8 d[n]; };");
        var caller = new Dictionary<string, int> { ["n"] = 2, };
        foreach (ExecutionPath path in Paths)
        {
            // inner.n = 3 leaks as n while i is read; the scope restores rec's n = 1 for d and e.
            string restored = EngineDifferential.AssertGolden(EngineOperations.Parse(restoring, [1, 3, 7, 8, 9], EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(restored, "result.d = PrimitiveArray<Byte> [1]\n");
            StringAssert.Contains(restored, "result.e = PrimitiveArray<Byte> [1]\n");

            // The caller's n is removed on entry, so with the branch not taken d's count is undefined.
            string removed = EngineDifferential.AssertGolden(EngineOperations.Parse(removing, [0, 5, 5], EngineInput.Span, "rec", caller), path: path);
            StringAssert.Contains(removed, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(removed, "Undefined expression identifier: n");
            string taken = EngineDifferential.AssertGolden(EngineOperations.Parse(removing, [1, 2, 5, 6], EngineInput.Stream, "rec", caller), path: path);
            StringAssert.Contains(taken, "result.d = PrimitiveArray<Byte> [2]\n");
        }
    }

    /// <summary>
    ///     Captures in nested fixed structs, read through their static plans or member by member, are published under
    ///     every qualified spelling the active prefix allows: <c>hdr.n</c> for
    ///     the expression of <c>mid</c>, which holds <c>hdr</c>, and <c>m.hdr.k</c> for the expression of <c>rec</c>.
    /// </summary>
    [TestMethod]
    public void QualifiedPrefixes_PublishThroughStaticPlansAndMembers()
    {
        var layout = new CStruct("struct h { uint8 n; uint8 k; }; struct mid { h hdr; uint8 v[hdr.n]; }; struct rec { mid m; uint8 w[m.hdr.k]; uint8 tail; };");
        byte[] data = [2, 1, 7, 8, 9, 4, 6];
        foreach (ExecutionPath path in Paths)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.m.v = PrimitiveArray<Byte> [2]\n");
            StringAssert.Contains(complete, "result.w = PrimitiveArray<Byte> [1]\n");
            StringAssert.Contains(complete, "result.tail = Byte 4\n");

            foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.ChunkedStream1])
            {
                for (int length = 0; length <= data.Length; length++)
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                }
            }
        }
    }

    /// <summary>
    ///     A failure deep inside a struct-array element names the innermost member and its type, and carries the root's
    ///     path and the position the failed read left, from every source.
    /// </summary>
    [TestMethod]
    public void Failure_NamesTheInnermostMember_WithPathAndOffset()
    {
        var layout = new CStruct("struct inner { uint8 a; uint16 b; }; struct rec { uint8 n; inner items[n]; uint8 tail; };");
        byte[] truncated = [2, 1, 2, 0, 3, 4];
        foreach (ExecutionPath path in Paths)
        {
            foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
            {
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, truncated, input, "rec"), path: path);
            }

            ReadOptions required = new ReadOptions() with { ExecutionPath = path, };
            CStructReadException failure = Assert.Throws<CStructReadException>(() => layout.Parse(truncated.AsSpan(), "rec", options: required));
            Assert.AreEqual("b", failure.Member);
            Assert.AreEqual("uint16", failure.MemberType);
            Assert.AreEqual("rec", failure.Path);
            Assert.AreEqual(6, failure.Offset);
        }
    }

    /// <summary>
    ///     A <c>#define</c> root is evaluated and reads nothing: <c>Parse</c> reports that it selects no composite and
    ///     <c>ReadValue</c> that it produces no value; a definition that names a field or an undefined caller variable
    ///     fails while it is evaluated, and one a caller variable cannot resolve fails before the read.
    /// </summary>
    [TestMethod]
    public void DefinitionRoot_EvaluatesAndReadsNothing()
    {
        var layout = new CStruct("#define SIZE (4 * n)\n#define LIVE (v + 1)\n#define RATIO (4 / n)\nstruct rec { uint8 v; };");
        foreach (Dictionary<string, int>? variables in (Dictionary<string, int>?[])[null, new() { ["n"] = 3, }, new() { ["n"] = 0, }])
        {
            foreach (string root in (string[])["SIZE", "LIVE", "RATIO"])
            {
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [1], EngineInput.Span, root, variables));
                EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, [1], EngineInput.Stream, root, variables));
            }
        }

        string live = EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, [1], EngineInput.Span, "LIVE"));
        StringAssert.Contains(live, "Undefined expression identifier: v");
    }

    /// <summary>
    ///     Typedef, enum and type-spelling roots are read standalone - element by element for arrays, never through the
    ///     block paths a placed member takes - into their typed shapes, and a count past the element limit fails before
    ///     the read. <c>ReadValue</c> of a runtime-sized root takes the count first, as the path resolver does, and then
    ///     reads the root. A spelling root counted by a caller's name has no slot for it, so its count reads the caller's
    ///     value through the dictionary the slots stand for.
    /// </summary>
    [TestMethod]
    public void StandaloneRoots_ReadIntoTheirTypedShapes()
    {
        var layout = new CStruct("#define N 2\nstruct p { uint8 x; uint8 y; }; typedef uint16 words[3]; typedef char name[4]; typedef p points[N]; enum kind : uint8 { A = 1 };");
        byte[] data = [1, 0, 2, 0, 3, 0, 9];
        var variables = new Dictionary<string, int> { ["N"] = 3, };
        foreach (ExecutionPath path in Paths)
        {
            foreach (string root in (string[])["words", "name", "points", "kind"])
            {
                for (int length = 0; length <= data.Length; length++)
                {
                    EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data[..length], EngineInput.Span, root), path: path);
                    EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data[..length], EngineInput.ChunkedStream3, root), path: path);
                }
            }

            string limited = EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, EngineInput.Stream, "words", options: new ReadOptions { MaxArrayElements = 2, }), path: path);
            StringAssert.Contains(limited, "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n");
            StringAssert.Contains(limited, "position = 0\n");
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "points"), path: path);
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, EngineInput.Span, "uint16[2]"), path: path);

            // N is a definition, so the spelling root's count has a slot: the parse and the selected read both run, the read
            // taking the count (checked against the element limit) before it reads the root.
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Stream, "uint8[N]", variables), path: path);
            foreach (int limit in (int[])[2, 3])
            {
                var elementLimit = new ReadOptions { MaxArrayElements = limit, };
                EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, EngineInput.Span, "uint8[N]", variables, elementLimit), path: path);
                EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, EngineInput.ChunkedStream3, "uint8[N]", variables, elementLimit), path: path);
            }

            // M is only the caller's, and no expression of the layout names it: it has no slot, and the count reads the
            // caller's value through the dictionary the slots stand for.
            var callerOnly = new Dictionary<string, int> { ["M"] = 3, };
            EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "uint8[M]", callerOnly), path: path);
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, EngineInput.ChunkedStream3, "uint8[M]", callerOnly), path: path);
            EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, EngineInput.Span, "uint8[M]"), path: path);
        }
    }

    /// <summary>
    ///     A terminated array is scanned for its all-zero element from its start, then read: the scan's bytes are charged
    ///     and the elements again when they are read, so the whole read of three elements costs
    ///     tag 1 + scan 8 + re-read 6 + tail 1 = 16 bytes of budget. Input that ends before the terminator, and more
    ///     elements than the limit allows, fail at the array's start. Arrays of scalars and of structs (through the element
    ///     struct's block path) agree at every budget, truncation and source.
    /// </summary>
    [TestMethod]
    public void TerminatedArrays_ChargeTheScanAndTheReread()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint16 values[]; uint8 tail; };");
        var structs = new CStruct("struct e { uint8 a; uint8 b; }; struct rec { e items[]; uint8 tail; };");
        byte[] data = [7, 1, 0, 2, 0, 3, 0, 0, 0, 9];
        byte[] entries = [1, 2, 3, 4, 0, 0, 9];
        foreach (ExecutionPath path in Paths)
        {
            for (long budget = 1; budget <= 17; budget++)
            {
                var read = new ReadOptions { MaxTotalBytesRead = budget, };
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1, EngineInput.ChunkedStream3])
                {
                    string outcome = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec", options: read), path: path);
                    string expected = budget < 16 ? "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n" : "result.tail = Byte 9\n";
                    StringAssert.Contains(outcome, expected, "budget " + budget + " from " + input);
                    EngineDifferential.AssertGolden(EngineOperations.Parse(structs, entries, input, "rec", options: read), path: path);
                }
            }

            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.ChunkedStream1])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                    EngineDifferential.AssertGolden(EngineOperations.Parse(structs, entries[..Math.Min(length, entries.Length)], input, "rec"), path: path);
                }
            }

            string unterminated = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..5], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(unterminated, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(unterminated, "position = 1\n");
            string limited = EngineDifferential.AssertGolden(
                EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: new ReadOptions { MaxArrayElements = 2, }),
                path: path);
            StringAssert.Contains(limited, "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n");
            StringAssert.Contains(limited, "position = 1\n");
        }
    }

    /// <summary>
    ///     An <c>[EOF]</c> array is counted from its placed start to the end of the input without reading: a trailing
    ///     partial element and a start that alignment moves past the end fail there, no remaining bytes is an empty typed
    ///     array, the element limit applies, and a <c>char[EOF]</c> is trimmed text - as the golden outcomes record, from every source.
    /// </summary>
    [TestMethod]
    public void ToEndArrays_CountWholeElementsFromTheirStart()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint16 values[EOF]; };");
        var aligned = new CStruct("struct rec { uint8 tag; uint32 values[EOF]; };", aligned: true);
        var text = new CStruct("struct rec { uint8 tag; char text[EOF]; };");
        byte[] data = [7, 1, 0, 2, 0, 3, 0];
        byte[] words = [7, 0, 0, 0, 1, 0, 0, 0];
        byte[] letters = [7, (byte)'h', (byte)'i', 0, 0];
        foreach (ExecutionPath path in Paths)
        {
            foreach ((CStruct subject, byte[] bytes) in ((CStruct, byte[])[])[(layout, data), (aligned, words), (text, letters)])
            {
                for (int length = 0; length <= bytes.Length; length++)
                {
                    foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                    {
                        foreach (bool trim in (bool[])[true, false])
                        {
                            EngineDifferential.AssertGolden(EngineOperations.Parse(subject, bytes[..length], input, "rec", options: new ReadOptions { TrimFixedText = trim, }), path: path);
                        }
                    }
                }
            }

            string remainder = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..6], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(remainder, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(remainder, "position = 1\n");
            string past = EngineDifferential.AssertGolden(EngineOperations.Parse(aligned, words[..1], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(past, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            string empty = EngineDifferential.AssertGolden(EngineOperations.Parse(aligned, words[..4], EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(empty, "result.values = PrimitiveArray<UInt32> [0]\n");
            string limited = EngineDifferential.AssertGolden(
                EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: new ReadOptions { MaxArrayElements = 2, }),
                path: path);
            StringAssert.Contains(limited, "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n");
            string trimmed = EngineDifferential.AssertGolden(
                EngineOperations.Parse(text, letters, EngineInput.Stream, "rec", options: new ReadOptions { TrimFixedText = true, }),
                path: path);
            StringAssert.Contains(trimmed, "result.text = String \"hi\"\n");
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
        foreach (ExecutionPath path in Paths)
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
        foreach (ExecutionPath path in Paths)
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

    /// <summary>
    ///     A multidimensional array reads all its elements in row-major order under one element limit and is then nested:
    ///     lists at every level (never typed arrays), <c>wchar</c> rows as strings validated row by row after every
    ///     character was read, and three dimensions. Typedef roots read standalone; their selected reads check the outermost
    ///     count against the element limit first, as the path resolver does, and then the read checks the total.
    /// </summary>
    [TestMethod]
    public void MultidimensionalArrays_ReadFlatThenNest()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint16 grid[2][3]; wchar< names[2][2]; uint8 cube[2][1][2]; uint8 tail; };");
        byte[] data = [7, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, (byte)'a', 0, 0, 0, (byte)'b', 0, (byte)'c', 0, 1, 2, 3, 4, 9];
        var roots = new CStruct("typedef uint8 table[2][3]; typedef char rows[2][3];");
        foreach (ExecutionPath path in Paths)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.grid = List<Object> [2]\n");
            StringAssert.Contains(complete, "result.grid[1] = List<Object> [3]\n");
            StringAssert.Contains(complete, "result.names[1] = String \"bc\"\n");
            StringAssert.Contains(complete, "result.cube[1][0] = List<Object> [2]\n");
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
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.ChunkedStream3, "rec", options: read), path: path);
            }

            // The limit applies to all 6 + 4 + 4 elements of a table, not its outermost count.
            foreach (int limit in (int[])[1, 2, 3, 4, 5, 6])
            {
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: new ReadOptions { MaxArrayElements = limit, }), path: path);
            }

            // A lone surrogate in the second row fails once every character was read, after the names.
            byte[] invalid = (byte[])data.Clone();
            invalid[17] = 0x00;
            invalid[18] = 0xD8;
            string surrogate = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, invalid, EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(surrogate, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(surrogate, "position = 21\n");

            foreach (string root in (string[])["table", "rows"])
            {
                for (int length = 0; length <= 6; length++)
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(roots, data[1..(1 + length)], EngineInput.Span, root), path: path);
                }

                // The outermost count (2) passes a limit of 2 before the read's total (6) fails it.
                foreach (int limit in (int[])[1, 2, 5, 6])
                {
                    var limited = new ReadOptions { MaxArrayElements = limit, };
                    EngineDifferential.AssertGolden(EngineOperations.ReadValue(roots, data[1..7], EngineInput.Span, root, options: limited), path: path);
                }

                EngineDifferential.AssertGolden(EngineOperations.ReadValue(roots, data[1..7], EngineInput.Span, root), path: path);
            }
        }
    }

    /// <summary>
    ///     Large data-sized and multidimensional arrays read in blocks of 64 KiB: a byte budget or
    ///     an input that ends inside the first or the second block fails at that block, cancellation is observed only
    ///     before a block, and a terminated array charges its whole scan before its blocks.
    /// </summary>
    [TestMethod]
    public void LargeArrays_ReadAndFailIn64KiBBlocks()
    {
        var table = new CStruct("struct rec { uint8 tag; uint8 big[300][300]; uint8 tail; };");
        var terminated = new CStruct("struct rec { uint8 tag; uint8 values[]; uint8 tail; };");
        byte[] big = [7, .. Enumerable.Range(0, 90000).Select(index => (byte)((index % 251) + 1)), 9];
        byte[] list = [7, .. Enumerable.Range(0, 70000).Select(index => (byte)((index % 251) + 1)), 0, 9];
        const long Scanned = 1 + 70001;
        foreach (ExecutionPath path in Paths)
        {
            foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream])
            {
                foreach (long budget in (long[])[65536, 65537, 65538, 90000, 90001, 90002])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(table, big, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }), path: path);
                }

                foreach (int length in (int[])[65536, 65537, 65538, 90000, 90001])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(table, big[..length], input, "rec"), path: path);
                }

                foreach (long budget in (long[])[Scanned, Scanned + 1, Scanned + 65536, Scanned + 65537, Scanned + 70000, Scanned + 70001, Scanned + 70002])
                {
                    string outcome = EngineDifferential.AssertGolden(
                        EngineOperations.Parse(terminated, list, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        path: path);
                    StringAssert.Contains(outcome, budget < Scanned + 70001 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            // Cancelled inside the first block, the check before the second block ends the read; inside the second, the
            // read completes, because no block, struct entry or string chunk follows.
            string first = EngineDifferential.AssertGolden(CancelledParse(table, big, 10), path: path);
            StringAssert.Contains(first, "failure = failure System.OperationCanceledException\n");
            string second = EngineDifferential.AssertGolden(CancelledParse(table, big, 70000), path: path);
            StringAssert.Contains(second, "result.tail = Byte 9\n");
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
        foreach (ExecutionPath path in Paths)
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
    ///     Every bitfield reads its whole storage unit again, and is charged for it: three
    ///     bitfields sharing a two-byte unit cost 6 bytes, the tail 1 more. While bits of a unit remain, the position is
    ///     back at the unit's start, so a later member that cannot be placed fails from there. A packed window whose
    ///     placed unit differs from its declared type, the MSVC and high-bit-first rules, and values without sign extension
    ///     (an <see cref="int"/> below 32 bits, a <see cref="ulong"/> from 32 on) read as the golden outcomes record, from every source.
    /// </summary>
    [TestMethod]
    public void BitfieldUnits_AreReadAgainForEveryBitfield()
    {
        var layout = new CStruct("struct rec { uint16 a : 4; uint16 b : 4; uint16 c : 8; uint8 tail; };");
        byte[] data = [0x21, 0x43, 0x09];
        var aligned = new CStruct("struct rec { uint8 a : 4; uint32 b; };", aligned: true);
        var window = new CStruct("struct rec { uint8 a : 7; uint16 b : 9; uint8 tail; };");
        var signs = new CStruct("struct rec { int8 s : 4; uint64 big : 40; };");
        var options = new CStructCompilationOptions { BitfieldPacking = BitfieldPacking.Msvc, BitfieldAllocation = BitfieldAllocation.HighBitFirst, };
        var msvc = new CStruct("struct rec { uint8 a : 3; uint16 b : 5; uint16 c : 9; uint8 tail; };", compilationOptions: options);
        foreach (ExecutionPath path in Paths)
        {
            for (long budget = 1; budget <= 8; budget++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    string outcome = EngineDifferential.AssertGolden(
                        EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        path: path);
                    StringAssert.Contains(outcome, budget < 7 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.b = Int32 2\n");
            StringAssert.Contains(complete, "result.c = Int32 67\n");

            // a leaves bits of its unit, so the position is back at 0 when b's aligned start (4) lies past the input.
            string rewound = EngineDifferential.AssertGolden(EngineOperations.Parse(aligned, [0x05], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(rewound, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(rewound, "position = 0\n");

            string unsigned = EngineDifferential.AssertGolden(EngineOperations.Parse(signs, [0xFF, 0x01, 0x02, 0x03, 0x04, 0xFF, 0, 0, 0], EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(unsigned, "result.s = Int32 15\n");
            StringAssert.Contains(unsigned, "result.big = UInt64 ");
            foreach ((CStruct subject, byte[] bytes) in ((CStruct, byte[])[])[(layout, data), (window, [0xFF, 0x81, 0x02, 0x09]), (msvc, [0x05, 0x34, 0x12, 0x09]), (signs, [0xFF, 0x01, 0x02, 0x03, 0x04, 0xFF, 0, 0, 0])])
            {
                for (int length = 0; length <= bytes.Length; length++)
                {
                    foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                    {
                        EngineDifferential.AssertGolden(EngineOperations.Parse(subject, bytes[..length], input, "rec"), path: path);
                    }
                }

                for (long budget = 1; budget <= (bytes.Length * 3) + 1; budget++)
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(subject, bytes, EngineInput.Span, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }), path: path);
                }
            }
        }
    }

    /// <summary>
    ///     A union charges its raw storage and then each member view (3 + 1 + 2 + 3 bytes here); every member starts at the
    ///     union's first byte with the variables it was entered with, and nothing a member captures is visible after the
    ///     union, so the struct's own <c>n</c> sizes the later array. A member that fails leaves the position at the union's
    ///     end, a nesting limit at its start; a promoted union claims no nesting level; numeric array views are typed arrays.
    /// </summary>
    [TestMethod]
    public void Unions_ChargeStorageAndViews_AndRestoreTheirEntryState()
    {
        var layout = new CStruct("union u { uint8 n; uint16 w; uint8 bytes[3]; }; struct rec { uint8 n; u value; uint8 items[n]; uint8 tail; };");
        byte[] data = [2, 7, 0, 0, 5, 6, 9];
        var invalid = new CStruct("union v { uint8 raw[4]; wchar< s[2]; }; struct rec { uint8 tag; v value; uint8 tail; };");
        var promoted = new CStruct("struct rec { uint8 a; union { uint8 x; uint16 y; }; uint8 tail; };");
        var hidden = new CStruct("union w { uint8 m; }; struct rec { w value; uint8 items[m]; };");
        foreach (ExecutionPath path in Paths)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.items = PrimitiveArray<Byte> [2]\n");
            StringAssert.Contains(complete, "PrimitiveArray<Byte> [3]\n");
            for (long budget = 1; budget <= 14; budget++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    string outcome = EngineDifferential.AssertGolden(
                        EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        path: path);
                    StringAssert.Contains(outcome, budget < 13 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream3])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                }
            }

            string failed = EngineDifferential.AssertGolden(EngineOperations.Parse(invalid, [1, 0x00, 0xD8, 0x41, 0x00, 9], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(failed, "failure.member = \"s\"\n");
            StringAssert.Contains(failed, "position = 5\n");

            string nested = EngineDifferential.AssertGolden(
                EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: new ReadOptions { MaxNestingDepth = 1, }),
                path: path);
            StringAssert.Contains(nested, "CStructReadLimitException");
            StringAssert.Contains(nested, "position = 1\n");
            string flat = EngineDifferential.AssertGolden(
                EngineOperations.Parse(promoted, [1, 2, 3, 9], EngineInput.Span, "rec", options: new ReadOptions { MaxNestingDepth = 1, }),
                path: path);
            StringAssert.Contains(flat, "result.tail = Byte 9\n");
            string invisible = EngineDifferential.AssertGolden(EngineOperations.Parse(hidden, [1, 5], EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(invisible, "Undefined expression identifier: m");
        }
    }

    /// <summary>
    ///     A struct follows its pointers after its last member, in declaration order, so a <c>@count</c> can name a later
    ///     field and the first pointer that fails is the one reported: a target outside the input fails naming its pointer
    ///     with the position just after that pointer's address, and so does a target that ends early (the position is
    ///     restored after the failed target read). Every byte of every target is charged once on top of the struct's own:
    ///     5 + 1 + 1 + 2 = 9 bytes here. A deferred count is checked before the pointer depth.
    /// </summary>
    [TestMethod]
    public void Pointers_FollowAfterTheStruct_InDeclarationOrder()
    {
        var layout = new CStruct("struct leaf { uint8 v; }; struct rec { uint8 *a; leaf *b; uint8 *c @count(n); uint8 n; uint8 tail; };", 1);
        byte[] data = [0x05, 0x06, 0x05, 0x02, 0x09, 0xA1, 0xB2];
        var wide = new CStruct("struct rec { uint8 *a; uint16 *w; uint8 tail; };", 1);
        var countFirst = new CStruct("struct rec { uint8 *c @count(n); uint8 n; };", 1);
        foreach (ExecutionPath path in Paths)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.tail = Byte 9\n");
            for (long budget = 1; budget <= 10; budget++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    string outcome = EngineDifferential.AssertGolden(
                        EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        path: path);
                    StringAssert.Contains(outcome, budget < 9 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream3])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                }
            }

            // a and b both point outside the input: a, declared first, fails first, after its own address.
            string outside = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [0x20, 0x21, 0x05, 0x00, 0x09, 0xA1], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(outside, "failure.member = \"a\"\n");
            StringAssert.Contains(outside, "position = 1\n");

            // w's two-byte target starts at the last byte: the read fails, and the position is back after w's address.
            string truncated = EngineDifferential.AssertGolden(EngineOperations.Parse(wide, [0x03, 0x03, 0x09, 0x07], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(truncated, "failure.member = \"w\"\n");
            StringAssert.Contains(truncated, "position = 2\n");

            // The deferred count is evaluated before the depth limit is checked.
            string counted = EngineDifferential.AssertGolden(
                EngineOperations.Parse(countFirst, [0x02, 0x02, 0xA1, 0xA2], EngineInput.Span, "rec", options: new ReadOptions { MaxArrayElements = 1, MaxPointerDepth = 0, }),
                path: path);
            StringAssert.Contains(counted, "failure.member = \"c\"\n");
            StringAssert.Contains(counted, ReadFailures.ArrayLengthLimit(2, 1).TrimEnd('.'));

            foreach (bool dereference in (bool[])[true, false])
            {
                foreach ((PointerAddressingMode mode, long origin) in ((PointerAddressingMode, long)[])[(PointerAddressingMode.Absolute, 0), (PointerAddressingMode.Relative, 0), (PointerAddressingMode.Relative, 2), (PointerAddressingMode.Relative, long.MaxValue)])
                {
                    var read = new ReadOptions { DereferencePointers = dereference, AddressingMode = mode, Origin = origin, };
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), path: path);
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.ChunkedStream1, "rec", options: read), path: path);
                }
            }
        }
    }

    /// <summary>
    ///     A target already on the active path is a cycle: a node whose pointer leads back to itself fails naming the
    ///     pointer, with the position restored after the outermost followed pointer's address. The active path is left on
    ///     unwind, so the same node read again elsewhere is no cycle. The pointer depth limit counts every level, list
    ///     links included, and a root pointer is followed in place.
    /// </summary>
    [TestMethod]
    public void Pointers_DetectCycles_AndLimitTheDepth()
    {
        var layout = new CStruct("struct node { uint8 v; node *next; }; struct rec { node head; node again; };", 1);
        byte[] data = [0x01, 0x04, 0x02, 0x04, 0x03, 0x00];
        var roots = new CStruct("typedef uint8 *byte_ptr; typedef uint8 **byte_ptr_ptr;", 1);
        byte[] rootData = [0x01, 0x02, 0x07];
        foreach (ExecutionPath path in Paths)
        {
            string fine = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(fine, "result.again.next");

            string cycle = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [0x01, 0x04, 0x02, 0x00, 0x03, 0x04], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(cycle, "Cyclic pointer target");
            StringAssert.Contains(cycle, "failure.member = \"next\"\n");
            StringAssert.Contains(cycle, "position = 2\n");

            for (int depth = 0; depth <= 3; depth++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxPointerDepth = depth, }), path: path);
                    EngineDifferential.AssertGolden(EngineOperations.Parse(roots, rootData, input, "byte_ptr_ptr", options: new ReadOptions { MaxPointerDepth = depth, }), path: path);
                }
            }

            for (int length = 0; length <= 3; length++)
            {
                EngineDifferential.AssertGolden(EngineOperations.Parse(roots, rootData[..length], EngineInput.Span, "byte_ptr"), path: path);
            }
        }
    }

    /// <summary>
    ///     A target size limit (<see cref="ReadOptions.MaxPointerTargetBytes"/>) refuses a text target, whose size is not
    ///     known, and a counted target larger than the limit, before either is read; a pointer in a union view keeps only
    ///     its address, and a <c>void *</c> is never followed.
    /// </summary>
    [TestMethod]
    public void Pointers_TargetLimits_UnionViews_AndOpaqueAddresses()
    {
        var layout = new CStruct("union u { uint8 raw; uint8 *p; }; struct rec { char *name; uint8 *bytes @count(n); u view; void *opaque; uint8 n; };", 1);
        byte[] data = [0x05, 0x07, 0x07, 0x07, 0x02, (byte)'o', 0x00, 0xA1, 0xA2];
        foreach (ExecutionPath path in Paths)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "\"o\"");
            foreach (long? limit in (long?[])[null, 0, 1, 2, 3])
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.ChunkedStream1])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxPointerTargetBytes = limit, }), path: path);
                }
            }

            for (long budget = 1; budget <= 16; budget++)
            {
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }), path: path);
            }
        }
    }

    /// <summary>
    ///     A root spelled at run time whose count names a caller variable no expression of the layout uses runs on the
    ///     engine through every operation, as the golden outcomes record: parsed, read as a value, debug-parsed, measured, written and updated,
    ///     with the variable, without it (the count names nothing), and next to an unrelated caller variable.
    /// </summary>
    [TestMethod]
    public void SpelledRootWithCallerOnlyVariable_RunsOnTheEngine()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 d[n]; };");
        const string spelled = "uint16[M]";
        byte[] data = [1, 0, 2, 0, 3, 0];
        object value = new ushort[] { 7, 8, };
        IReadOnlyDictionary<string, int>?[] variableSets = [new Dictionary<string, int> { ["M"] = 2, }, null, new Dictionary<string, int> { ["M"] = 3, ["unrelated"] = 1, }];
        foreach (IReadOnlyDictionary<string, int>? variables in variableSets)
        {
            foreach (ExecutionPath path in Paths)
            {
                EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, EngineInput.Span, spelled, variables), path: path);
                EngineDifferential.AssertGolden(EngineOperations.ReadValue(layout, data, EngineInput.Stream, spelled, variables), path: path);
                EngineDifferential.AssertGolden(EngineOperations.ReadValueWithDebug(layout, data, EngineInput.Span, spelled, variables), path: path);
                EngineDifferential.AssertGolden(EngineOperations.GetArrayLength(layout, data, EngineInput.Span, spelled, variables), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Serialize(layout, spelled, value, variables), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Write(layout, [0xAA, 0xAA], 1, spelled, value, variables), path: path);
                EngineDifferential.AssertGolden(EngineOperations.Update(layout, data, EngineInput.Span, spelled, value, variables), path: path);
            }
        }
    }

    /// <summary>Encodes a <c>blob</c> (<see cref="LengthPrefixedCodec"/>) of <paramref name="length"/> zero bytes.</summary>
    /// <param name="length">The announced length.</param>
    /// <returns>The two-byte little-endian prefix followed by the bytes.</returns>
    private static byte[] Blob(int length) => [(byte)length, (byte)(length >> 8), .. new byte[length]];

    /// <summary>
    ///     A parse over a hidden-buffer stream whose operation token is cancelled when a read first reaches byte
    ///     <paramref name="trigger"/>; each side gets its own token and stream.
    /// </summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="trigger">The byte whose read cancels the token.</param>
    /// <returns>The operation, which renders the value or failure and the final position.</returns>
    private static GoldenOperation CancelledParse(CStruct layout, byte[] data, int trigger)
    {
        return new GoldenOperation(
            "Parse (cancelled at byte " + trigger + ")",
            (execution, output) =>
            {
                using var cancellation = new CancellationTokenSource();
                using var stream = new CancellingStream(data, trigger, cancellation);
                ReadOptions read = ExecutionPaths.Read(execution, new ReadOptions { CancellationToken = cancellation.Token, });
                output.Capture("failure", () => output.Value("result", layout.Parse(stream, "rec", options: read)));
                output.Line("position", stream.Position.ToString(System.Globalization.CultureInfo.InvariantCulture));
            });
    }

    /// <summary>A seekable stream that hides its buffer and cancels a token when a read first covers a given byte.</summary>
    private sealed class CancellingStream : MemoryStream
    {
        /// <summary>The byte whose read cancels the token.</summary>
        private readonly int trigger;

        /// <summary>The token source to cancel.</summary>
        private readonly CancellationTokenSource cancellation;

        /// <summary>Creates the stream over a copy of the data.</summary>
        /// <param name="data">The bytes.</param>
        /// <param name="trigger">The byte whose read cancels the token.</param>
        /// <param name="cancellation">The token source to cancel.</param>
        public CancellingStream(byte[] data, int trigger, CancellationTokenSource cancellation)
            : base((byte[])data.Clone(), writable: false)
        {
            this.trigger = trigger;
            this.cancellation = cancellation;
        }

        /// <summary>Reads, then cancels the token when the bytes read covered the trigger byte.</summary>
        /// <param name="buffer">The destination.</param>
        /// <returns>The number of bytes read.</returns>
        public override int Read(Span<byte> buffer)
        {
            // MemoryStream's own span overload calls the array overload in a derived type, so it goes through there.
            byte[] copy = new byte[buffer.Length];
            int read = this.Read(copy, 0, copy.Length);
            copy.AsSpan(0, read).CopyTo(buffer);
            return read;
        }

        /// <summary>Reads, then cancels the token when the bytes read covered the trigger byte.</summary>
        /// <param name="buffer">The destination array.</param>
        /// <param name="offset">The first index to fill.</param>
        /// <param name="count">The largest number of bytes to read.</param>
        /// <returns>The number of bytes read.</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            long start = this.Position;
            int read = base.Read(buffer, offset, count);
            this.CancelIfCovered(start, read);
            return read;
        }

        /// <summary>Reads one byte, then cancels the token when it was the trigger byte.</summary>
        /// <returns>The byte, or -1 at the end.</returns>
        public override int ReadByte()
        {
            long start = this.Position;
            int value = base.ReadByte();
            this.CancelIfCovered(start, value < 0 ? 0 : 1);
            return value;
        }

        /// <summary>Hides the buffer, so the reader streams the bytes rather than reading them from memory.</summary>
        /// <param name="buffer">Always the default segment.</param>
        /// <returns><see langword="false"/>.</returns>
        public override bool TryGetBuffer(out ArraySegment<byte> buffer)
        {
            buffer = default;
            return false;
        }

        /// <summary>Cancels the token when the read range covered the trigger byte.</summary>
        /// <param name="start">The read's first byte.</param>
        /// <param name="read">The number of bytes read.</param>
        private void CancelIfCovered(long start, int read)
        {
            if (start <= this.trigger && this.trigger < start + read)
            {
                this.cancellation.Cancel();
            }
        }
    }
}
