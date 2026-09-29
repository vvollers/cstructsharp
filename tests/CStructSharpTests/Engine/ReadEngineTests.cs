namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Values;

/// <summary>
///     The compiled read engine's step semantics where the sweeps and corpora do not reach: every codec at every
///     truncation and byte budget, an offset assertion checked before its member is placed, cancellation only at the
///     documented boundaries (engine plan Appendix A), nesting limits with promoted members, the conditional variable
///     scope, qualified prefixes through static plans, and the member, path and offset a failure reports. Each case
///     compares the engine with the interpreter through the differential harness, and pins the expected outcome.
/// </summary>
[TestClass]
public class ReadEngineTests
{
    /// <summary>A packed layout with one scalar or array of every codec the engine reads through a codec reader.</summary>
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

    /// <summary>The execution paths each case runs under: the fast paths the interpreter takes, and the general path only.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.GeneralOnly];

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
    ///     units, <c>wchar</c>, enum storage, their arrays, bounded and terminated text) reads identically at every
    ///     truncation from memory and from streams that return one or seven bytes per read, and at every byte budget.
    /// </summary>
    [TestMethod]
    public void EveryCodec_ReadsIdenticallyAtEveryTruncationAndBudget()
    {
        var layout = new CStruct(CodecLayout);
        byte[] data = CodecData;
        EngineComparison complete = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), expectEngine: true);
        Assert.AreEqual(1, complete.Automatic.EngineRuns);
        StringAssert.Contains(complete.Rendering, "result.a = Int64 -5\n");
        StringAssert.Contains(complete.Rendering, "result.l4 = Int64 -300\n");
        StringAssert.Contains(complete.Rendering, "result.w = Char");
        StringAssert.Contains(complete.Rendering, "result.s = String \"ok\"\n");
        StringAssert.Contains(complete.Rendering, "result.tail = Byte 9\n");

        foreach (ExecutionPath path in Paths)
        {
            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1, EngineInput.ChunkedStream7])
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..length], input, "rec"), expectEngine: true, path: path);
                }
            }

            for (long budget = 1; budget <= data.Length + 1; budget++)
            {
                var read = new ReadOptions { MaxTotalBytesRead = budget, };
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), expectEngine: true, path: path);
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.ChunkedStream3, "rec", options: read), expectEngine: true, path: path);
            }

            // Invalid values fail inside their own codecs: an unterminated LEB128, invalid UTF-16, an invalid bounded text.
            byte[] invalid = (byte[])data.Clone();
            invalid[92] = 0x80;
            invalid[93] = 0x80;
            invalid[94] = 0x80;
            invalid[95] = 0x80;
            invalid[96] = 0x80;
            EngineDifferential.AssertSame(EngineOperations.Parse(layout, invalid, EngineInput.Span, "rec"), expectEngine: true, path: path);
            invalid = (byte[])data.Clone();
            invalid[127] = 0xD8;
            EngineDifferential.AssertSame(EngineOperations.Parse(layout, invalid, EngineInput.Stream, "rec"), expectEngine: true, path: path);
            invalid = (byte[])data.Clone();
            invalid[133] = 0xFF;
            EngineDifferential.AssertSame(EngineOperations.Parse(layout, invalid, EngineInput.Span, "rec"), expectEngine: true, path: path);
        }
    }

    /// <summary>
    ///     Terminated strings read from memory in place charge the chunks the chunked reader takes and end just after
    ///     the terminator: a string past one 256-byte chunk, UTF-16 text, every byte budget, string limits around the
    ///     text's length, every truncation, and invalid text all read identically from memory and from streams.
    /// </summary>
    [TestMethod]
    public void TerminatedStrings_ReadInPlaceLikeTheChunkedReader()
    {
        var layout = new CStruct("struct rec { uint8 a; cstring s; string w; uint8 tail; };", isLittleEndian: true);
        byte[] data = [7, .. Enumerable.Repeat((byte)'x', 300), 0, (byte)'h', 0, (byte)'i', 0, 0, 0, 9];
        foreach (ExecutionPath path in Paths)
        {
            EngineComparison complete = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(complete.Rendering, "result.w = String \"hi\"\n");
            StringAssert.Contains(complete.Rendering, "result.tail = Byte 9\n");
            for (long budget = 1; budget <= data.Length + 260; budget++)
            {
                var read = new ReadOptions { MaxTotalBytesRead = budget, };
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), expectEngine: true, path: path);
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: read), expectEngine: true, path: path);
            }

            foreach (int limit in (int[])[0, 4, 5, 6, 255, 256, 257, 300, 301, 302])
            {
                var read = new ReadOptions { MaxStringBytes = limit, };
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), expectEngine: true, path: path);
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.ChunkedStream7, "rec", options: read), expectEngine: true, path: path);
            }

            for (int length = 0; length <= data.Length; length++)
            {
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..length], EngineInput.Span, "rec"), expectEngine: true, path: path);
            }

            byte[] invalid = (byte[])data.Clone();
            invalid[270] = 0xC3;
            EngineDifferential.AssertSame(EngineOperations.Parse(layout, invalid, EngineInput.Span, "rec"), expectEngine: true, path: path);
            invalid = (byte[])data.Clone();
            invalid[304] = 0xD8;
            EngineDifferential.AssertSame(EngineOperations.Parse(layout, invalid, EngineInput.Span, "rec"), expectEngine: true, path: path);
        }
    }

    /// <summary>
    ///     A runtime-checked <c>@N</c> assertion is checked before its member is placed, as the interpreter's placement
    ///     cursor does: a failing assertion leaves the position before the padding and names the member, and it wins over
    ///     a start that lies past the input.
    /// </summary>
    [TestMethod]
    public void OffsetAssertion_IsCheckedBeforeTheMemberIsPlaced()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 d[n]; uint32 x @8; };", aligned: true);
        foreach (ExecutionPath path in Paths)
        {
            // n = 5 ends the array at 6, so x is aligned to 8 as asserted.
            EngineComparison valid = EngineDifferential.AssertSame(EngineOperations.Parse(layout, [5, 0, 0, 0, 0, 0, 0, 0, 7, 0, 0, 0], EngineInput.Stream, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(valid.Rendering, "result.x = UInt32 7\n");

            // n = 1 aligns x to 4, not 8: the stream stays after the array, before the padding.
            EngineComparison misplaced = EngineDifferential.AssertSame(EngineOperations.Parse(layout, [1, 0, 0, 0, 7, 0, 0, 0], EngineInput.Stream, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(misplaced.Rendering, "failure = failure CStructSharp.Diagnostics.CStructLayoutException\n");
            StringAssert.Contains(misplaced.Rendering, "failure.member = \"x\"\n");
            StringAssert.Contains(misplaced.Rendering, "position = 2\n");

            // The same misplaced start past the end of the input still reports the assertion.
            EngineComparison past = EngineDifferential.AssertSame(EngineOperations.Parse(layout, [1, 0], EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(past.Rendering, "failure = failure CStructSharp.Diagnostics.CStructLayoutException\n");
        }
    }

    /// <summary>
    ///     Cancellation is observed at struct entries (each element of a struct array is one) and never per primitive
    ///     (engine plan Appendix A, CONTRACT): a token cancelled while a struct's primitives are read ends the read at
    ///     the next struct entry, at the same position as the interpreter; a read with no further struct entry completes.
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
                EngineComparison comparison = EngineDifferential.AssertSame(CancelledParse(elements, data, trigger), expectEngine: true, path: path);

                // The next struct entry observes the token: the entry of the element after the one being read, or - on
                // the fast path, which stages each element's bytes before entering it - the entry of the element whose
                // bytes were just staged.
                if (trigger <= (path == ExecutionPath.GeneralOnly ? 4 : 6))
                {
                    StringAssert.Contains(comparison.Rendering, "failure = failure System.OperationCanceledException\n", "trigger " + trigger);
                }
                else
                {
                    StringAssert.Contains(comparison.Rendering, "result.tail = Byte 9\n", "trigger " + trigger);
                }
            }
        }

        // Only primitives after the root's entry: the general path reads to the end whenever the token is cancelled.
        var primitives = new CStruct("struct rec { uint8 a; uint16 b; uint8 c; };");
        for (int trigger = 0; trigger < 4; trigger++)
        {
            EngineComparison comparison = EngineDifferential.AssertSame(CancelledParse(primitives, [1, 2, 0, 3], trigger), expectEngine: true, path: ExecutionPath.GeneralOnly);
            StringAssert.Contains(comparison.Rendering, "result.c = Byte 3\n", "trigger " + trigger);
        }
    }

    /// <summary>
    ///     Every named struct claims a nesting level, a struct-array element included, and an anonymous promoted member
    ///     claims none; the limit fails with the nesting message at exactly the depth the layout needs minus one, through
    ///     the static plans and the general path alike.
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
                    EngineComparison comparison = EngineDifferential.AssertSame(
                        EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxNestingDepth = depth, }),
                        expectEngine: true,
                        path: path);
                    if (depth < 3)
                    {
                        // Depth 1 cannot enter the elements of items; depth 2 enters them but not their member x.
                        string member = depth == 1 ? "'items' (b)" : "'x' (a)";
                        StringAssert.Contains(comparison.Rendering, "failure.message = \"" + ReadFailures.NestingLimit.TrimEnd('.') + " (field " + member, "depth " + depth);
                    }
                    else
                    {
                        StringAssert.Contains(comparison.Rendering, "result.tail = Byte 9\n", "depth " + depth);
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
            EngineComparison restored = EngineDifferential.AssertSame(EngineOperations.Parse(restoring, [1, 3, 7, 8, 9], EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(restored.Rendering, "result.d = PrimitiveArray<Byte> [1]\n");
            StringAssert.Contains(restored.Rendering, "result.e = PrimitiveArray<Byte> [1]\n");

            // The caller's n is removed on entry, so with the branch not taken d's count is undefined.
            EngineComparison removed = EngineDifferential.AssertSame(EngineOperations.Parse(removing, [0, 5, 5], EngineInput.Span, "rec", caller), expectEngine: true, path: path);
            StringAssert.Contains(removed.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(removed.Rendering, "Undefined expression identifier: n");
            EngineComparison taken = EngineDifferential.AssertSame(EngineOperations.Parse(removing, [1, 2, 5, 6], EngineInput.Stream, "rec", caller), expectEngine: true, path: path);
            StringAssert.Contains(taken.Rendering, "result.d = PrimitiveArray<Byte> [2]\n");
        }
    }

    /// <summary>
    ///     Captures in nested fixed structs, read through their static plans or member by member, are published under
    ///     every qualified spelling the active prefix allows, exactly as the interpreter publishes them: <c>hdr.n</c> for
    ///     the expression of <c>mid</c>, which holds <c>hdr</c>, and <c>m.hdr.k</c> for the expression of <c>rec</c>.
    /// </summary>
    [TestMethod]
    public void QualifiedPrefixes_PublishThroughStaticPlansAndMembers()
    {
        var layout = new CStruct("struct h { uint8 n; uint8 k; }; struct mid { h hdr; uint8 v[hdr.n]; }; struct rec { mid m; uint8 w[m.hdr.k]; uint8 tail; };");
        byte[] data = [2, 1, 7, 8, 9, 4, 6];
        foreach (ExecutionPath path in Paths)
        {
            EngineComparison complete = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(complete.Rendering, "result.m.v = PrimitiveArray<Byte> [2]\n");
            StringAssert.Contains(complete.Rendering, "result.w = PrimitiveArray<Byte> [1]\n");
            StringAssert.Contains(complete.Rendering, "result.tail = Byte 4\n");

            foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.ChunkedStream1])
            {
                for (int length = 0; length <= data.Length; length++)
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..length], input, "rec"), expectEngine: true, path: path);
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
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, truncated, input, "rec"), expectEngine: true, path: path);
            }

            ReadOptions required = EngineSelections.EngineRequired() with { ExecutionPath = path, };
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
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, [1], EngineInput.Span, root, variables), expectEngine: true);
                EngineDifferential.AssertSame(EngineOperations.ReadValue(layout, [1], EngineInput.Stream, root, variables), expectEngine: true);
            }
        }

        EngineComparison live = EngineDifferential.AssertSame(EngineOperations.ReadValue(layout, [1], EngineInput.Span, "LIVE"), expectEngine: true);
        StringAssert.Contains(live.Rendering, "Undefined expression identifier: v");
    }

    /// <summary>
    ///     Typedef, enum and type-spelling roots are read standalone - element by element for arrays, never through the
    ///     block paths a placed member takes - into their typed shapes, and a count past the element limit fails before
    ///     the read. A spelling root counted by a caller's name has no slot for it and is left to the interpreter, and so
    ///     is <c>ReadValue</c> of a runtime-sized root, whose count the interpreter's path resolution evaluates first.
    /// </summary>
    [TestMethod]
    public void StandaloneRoots_ReadIdentically()
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
                    EngineDifferential.AssertSame(EngineOperations.ReadValue(layout, data[..length], EngineInput.Span, root), expectEngine: true, path: path);
                    EngineDifferential.AssertSame(EngineOperations.ReadValue(layout, data[..length], EngineInput.ChunkedStream3, root), expectEngine: true, path: path);
                }
            }

            EngineComparison limited = EngineDifferential.AssertSame(EngineOperations.ReadValue(layout, data, EngineInput.Stream, "words", options: new ReadOptions { MaxArrayElements = 2, }), expectEngine: true, path: path);
            StringAssert.Contains(limited.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n");
            StringAssert.Contains(limited.Rendering, "position = 0\n");
            EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "points"), expectEngine: true, path: path);
            EngineDifferential.AssertSame(EngineOperations.ReadValue(layout, data, EngineInput.Span, "uint16[2]"), expectEngine: true, path: path);

            // N is a definition, so the spelling root's count has a slot: the parse runs, the selected read is declined.
            EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Stream, "uint8[N]", variables), expectEngine: true, path: path);
            EngineComparison resolved = EngineDifferential.AssertSame(EngineOperations.ReadValue(layout, data, EngineInput.Span, "uint8[N]", variables), expectEngine: false, path: path);
            Assert.AreEqual(new EngineDecline(EngineOperation.RootRead, EngineSelector.ResolvedRootArray), resolved.Automatic.LastDecline);

            // M is only the caller's, and no expression of the layout names it: no slot, so no program.
            EngineComparison unslotted = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "uint8[M]", new Dictionary<string, int> { ["M"] = 3, }), expectEngine: false, path: path);
            StringAssert.Contains(unslotted.Automatic.LastDecline!.Value.Reason, Compilation.Programs.ReadProgramCompiler.UnslottedName + "M");
        }
    }

    /// <summary>
    ///     A terminated array is scanned for its all-zero element from its start, then read: the scan's bytes are charged
    ///     and the elements again when they are read (engine plan section 4.3), so the whole read of three elements costs
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
                    EngineComparison comparison = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, input, "rec", options: read), expectEngine: true, path: path);
                    string expected = budget < 16 ? "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n" : "result.tail = Byte 9\n";
                    StringAssert.Contains(comparison.Rendering, expected, "budget " + budget + " from " + input);
                    EngineDifferential.AssertSame(EngineOperations.Parse(structs, entries, input, "rec", options: read), expectEngine: true, path: path);
                }
            }

            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.ChunkedStream1])
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..length], input, "rec"), expectEngine: true, path: path);
                    EngineDifferential.AssertSame(EngineOperations.Parse(structs, entries[..Math.Min(length, entries.Length)], input, "rec"), expectEngine: true, path: path);
                }
            }

            EngineComparison unterminated = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..5], EngineInput.Stream, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(unterminated.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(unterminated.Rendering, "position = 1\n");
            EngineComparison limited = EngineDifferential.AssertSame(
                EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: new ReadOptions { MaxArrayElements = 2, }),
                expectEngine: true,
                path: path);
            StringAssert.Contains(limited.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n");
            StringAssert.Contains(limited.Rendering, "position = 1\n");
        }
    }

    /// <summary>
    ///     An <c>[EOF]</c> array is counted from its placed start to the end of the input without reading: a trailing
    ///     partial element and a start that alignment moves past the end fail there, no remaining bytes is an empty typed
    ///     array, the element limit applies, and a <c>char[EOF]</c> is trimmed text - identically from every source.
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
                            EngineDifferential.AssertSame(EngineOperations.Parse(subject, bytes[..length], input, "rec", options: new ReadOptions { TrimFixedText = trim, }), expectEngine: true, path: path);
                        }
                    }
                }
            }

            EngineComparison remainder = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..6], EngineInput.Stream, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(remainder.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(remainder.Rendering, "position = 1\n");
            EngineComparison past = EngineDifferential.AssertSame(EngineOperations.Parse(aligned, words[..1], EngineInput.Stream, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(past.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            EngineComparison empty = EngineDifferential.AssertSame(EngineOperations.Parse(aligned, words[..4], EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(empty.Rendering, "result.values = PrimitiveArray<UInt32> [0]\n");
            EngineComparison limited = EngineDifferential.AssertSame(
                EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: new ReadOptions { MaxArrayElements = 2, }),
                expectEngine: true,
                path: path);
            StringAssert.Contains(limited.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n");
            EngineComparison trimmed = EngineDifferential.AssertSame(
                EngineOperations.Parse(text, letters, EngineInput.Stream, "rec", options: new ReadOptions { TrimFixedText = true, }),
                expectEngine: true,
                path: path);
            StringAssert.Contains(trimmed.Rendering, "result.text = String \"hi\"\n");
        }
    }

    /// <summary>
    ///     Caller-supplied codecs run through the same adapter as the interpreter's (engine plan Appendix A): from memory the
    ///     codec sees the whole remainder and the position advances, and is charged, before a failure - by the whole
    ///     remainder when the codec needs more data; from a stream through a window that doubles from 256 bytes up to
    ///     <see cref="ReadOptions.MaxStringBytes"/>. Every truncation, byte budget and string limit, and a codec that decodes
    ///     no value, throws, or claims more bytes than it was given, reads identically from memory and from streams.
    /// </summary>
    [TestMethod]
    public void CustomCodecs_ReadThroughTheAdapterFromEverySource()
    {
        var options = new CStructCompilationOptions { Codecs = [VlqCodec.Instance, LengthPrefixedCodec.Instance, QuirkyCodec.Instance,], };
        var layout = new CStruct("struct rec { uint8 tag; vlq v; blob b; odd o[2]; uint8 tail; };", compilationOptions: options);
        byte[] data = [7, 0x80, 0x01, .. Blob(600), 5, 6, 9];
        foreach (ExecutionPath path in Paths)
        {
            EngineComparison complete = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(complete.Rendering, "result.v = UInt32 128\n");
            StringAssert.Contains(complete.Rendering, "result.b = Int32 600\n");
            StringAssert.Contains(complete.Rendering, "result.tail = Byte 9\n");
            foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream7])
            {
                for (int length = 0; length <= data.Length; length++)
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..length], input, "rec"), expectEngine: true, path: path);
                }

                foreach (int limit in (int[])[255, 256, 257, 300, 512, 513, 601, 602, 603, 1024])
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxStringBytes = limit, }), expectEngine: true, path: path);
                }
            }

            for (long budget = 1; budget <= data.Length + 1; budget++)
            {
                var read = new ReadOptions { MaxTotalBytesRead = budget, };
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), expectEngine: true, path: path);
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: read), expectEngine: true, path: path);
            }

            // A value that continues past the input: memory charges the whole remainder, so a small budget fails first.
            EngineComparison needsMore = EngineDifferential.AssertSame(EngineOperations.Parse(layout, [7, 0x80, 0x80, 0x80], EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(needsMore.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            EngineComparison charged = EngineDifferential.AssertSame(
                EngineOperations.Parse(layout, [7, 0x80, 0x80, 0x80], EngineInput.Span, "rec", options: new ReadOptions { MaxTotalBytesRead = 3, }),
                expectEngine: true,
                path: path);
            StringAssert.Contains(charged.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadLimitException\n");

            // odd: 0 decodes to no value, 1 throws inside the codec, 2 claims more bytes than it was given.
            foreach (byte quirk in (byte[])[0, 1, 2])
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    EngineComparison comparison = EngineDifferential.AssertSame(
                        EngineOperations.Parse(layout, [7, 0x05, 0x00, 0x00, quirk, 6, 9], input, "rec"),
                        expectEngine: true,
                        path: path);
                    StringAssert.Contains(comparison.Rendering, quirk == 0 ? "failure = failure System.InvalidOperationException\n" : "failure = failure CStructSharp.Diagnostics.CStructReadException\n", "quirk " + quirk);
                }
            }
        }
    }

    /// <summary>
    ///     A caller's codec that declares four bytes may take one: every later member, nested struct and tail is placed
    ///     from where its bytes actually end, as the interpreter's placement cursor does, not from the offsets the layout
    ///     compiled - packed and aligned, in the short and the full form, at every truncation and byte budget.
    /// </summary>
    [TestMethod]
    public void FixedSizeCustomCodec_PlacesLaterMembersWhereItsBytesEnd()
    {
        var options = new CStructCompilationOptions { Codecs = [FixedWordCodec.Instance,], };
        const string definition = "struct inner { word4 w; uint8 b; }; struct rec { word4 a; uint16 x; inner i; uint16 y; };";
        var packed = new CStruct(definition, compilationOptions: options);
        var aligned = new CStruct(definition, aligned: true, compilationOptions: options);
        (CStruct Layout, byte[] Data)[] cases =
        [
            (packed, [0xEE, 0x34, 0x12, 0xEE, 0x05, 0x78, 0x56]),
            (packed, [1, 0, 0, 0, 0x34, 0x12, 2, 0, 0, 0, 0x05, 0x78, 0x56]),
            (aligned, [0xEE, 0, 0x34, 0x12, 0xEE, 0x05, 0, 0, 0x78, 0x56, 0, 0]),
            (aligned, [1, 0, 0, 0, 0x34, 0x12, 0, 0, 2, 0, 0, 0, 0x05, 0, 0, 0, 0x78, 0x56, 0, 0]),
        ];
        foreach (ExecutionPath path in Paths)
        {
            foreach ((CStruct layout, byte[] data) in cases)
            {
                EngineComparison complete = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), expectEngine: true, path: path);
                StringAssert.Contains(complete.Rendering, "result.x = UInt16 4660\n");
                StringAssert.Contains(complete.Rendering, "result.y = UInt16 22136\n");
                for (int length = 0; length <= data.Length; length++)
                {
                    foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                    {
                        EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..length], input, "rec"), expectEngine: true, path: path);
                    }
                }

                for (long budget = 1; budget <= data.Length + 1; budget++)
                {
                    var read = new ReadOptions { MaxTotalBytesRead = budget, };
                    EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), expectEngine: true, path: path);
                    EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: read), expectEngine: true, path: path);
                }

                byte[] rejected = (byte[])data.Clone();
                rejected[0] = FixedWordCodec.Invalid;
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, rejected, EngineInput.Span, "rec"), expectEngine: true, path: path);
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, rejected, EngineInput.Stream, "rec"), expectEngine: true, path: path);
            }
        }
    }

    /// <summary>
    ///     A multidimensional array reads all its elements in row-major order under one element limit and is then nested:
    ///     lists at every level (never typed arrays), <c>wchar</c> rows as strings validated row by row after every
    ///     character was read, and three dimensions. Typedef roots read standalone; their selected reads, whose outermost
    ///     count the interpreter's path resolver checks first, are left to the interpreter.
    /// </summary>
    [TestMethod]
    public void MultidimensionalArrays_ReadFlatThenNest()
    {
        var layout = new CStruct("struct rec { uint8 tag; uint16 grid[2][3]; wchar< names[2][2]; uint8 cube[2][1][2]; uint8 tail; };");
        byte[] data = [7, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0, (byte)'a', 0, 0, 0, (byte)'b', 0, (byte)'c', 0, 1, 2, 3, 4, 9];
        var roots = new CStruct("typedef uint8 table[2][3]; typedef char rows[2][3];");
        foreach (ExecutionPath path in Paths)
        {
            EngineComparison complete = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(complete.Rendering, "result.grid = List<Object> [2]\n");
            StringAssert.Contains(complete.Rendering, "result.grid[1] = List<Object> [3]\n");
            StringAssert.Contains(complete.Rendering, "result.names[1] = String \"bc\"\n");
            StringAssert.Contains(complete.Rendering, "result.cube[1][0] = List<Object> [2]\n");
            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..length], input, "rec"), expectEngine: true, path: path);
                }
            }

            for (long budget = 1; budget <= data.Length + 1; budget++)
            {
                var read = new ReadOptions { MaxTotalBytesRead = budget, };
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), expectEngine: true, path: path);
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.ChunkedStream3, "rec", options: read), expectEngine: true, path: path);
            }

            // The limit applies to all 6 + 4 + 4 elements of a table, not its outermost count.
            foreach (int limit in (int[])[1, 2, 3, 4, 5, 6])
            {
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: new ReadOptions { MaxArrayElements = limit, }), expectEngine: true, path: path);
            }

            // A lone surrogate in the second row fails once every character was read, after the names.
            byte[] invalid = (byte[])data.Clone();
            invalid[17] = 0x00;
            invalid[18] = 0xD8;
            EngineComparison surrogate = EngineDifferential.AssertSame(EngineOperations.Parse(layout, invalid, EngineInput.Stream, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(surrogate.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(surrogate.Rendering, "position = 21\n");

            foreach (string root in (string[])["table", "rows"])
            {
                for (int length = 0; length <= 6; length++)
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(roots, data[1..(1 + length)], EngineInput.Span, root), expectEngine: true, path: path);
                }

                EngineComparison selected = EngineDifferential.AssertSame(EngineOperations.ReadValue(roots, data[1..7], EngineInput.Span, root), expectEngine: false, path: path);
                Assert.AreEqual(new EngineDecline(EngineOperation.RootRead, EngineSelector.ResolvedRootArray), selected.Automatic.LastDecline);
            }
        }
    }

    /// <summary>
    ///     Large data-sized and multidimensional arrays read in blocks of 64 KiB (engine plan Appendix A): a byte budget or
    ///     an input that ends inside the first or the second block fails at that block, cancellation is observed only
    ///     before a block, and a terminated array charges its whole scan before its blocks.
    /// </summary>
    [TestMethod]
    public void LargeArrays_FailAtTheSame64KiBBlocks()
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
                    EngineDifferential.AssertSame(EngineOperations.Parse(table, big, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }), expectEngine: true, path: path);
                }

                foreach (int length in (int[])[65536, 65537, 65538, 90000, 90001])
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(table, big[..length], input, "rec"), expectEngine: true, path: path);
                }

                foreach (long budget in (long[])[Scanned, Scanned + 1, Scanned + 65536, Scanned + 65537, Scanned + 70000, Scanned + 70001, Scanned + 70002])
                {
                    EngineComparison comparison = EngineDifferential.AssertSame(
                        EngineOperations.Parse(terminated, list, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        expectEngine: true,
                        path: path);
                    StringAssert.Contains(comparison.Rendering, budget < Scanned + 70001 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            // Cancelled inside the first block, the check before the second block ends the read; inside the second, the
            // read completes, because no block, struct entry or string chunk follows.
            EngineComparison first = EngineDifferential.AssertSame(CancelledParse(table, big, 10), expectEngine: true, path: path);
            StringAssert.Contains(first.Rendering, "failure = failure System.OperationCanceledException\n");
            EngineComparison second = EngineDifferential.AssertSame(CancelledParse(table, big, 70000), expectEngine: true, path: path);
            StringAssert.Contains(second.Rendering, "result.tail = Byte 9\n");
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
                EngineComparison valid = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, input, "rec"), expectEngine: true, path: path);
                StringAssert.Contains(valid.Rendering, "result.tail = Byte 9\n");
                EngineDifferential.AssertSame(EngineOperations.Parse(layout, invalid, input, "rec"), expectEngine: true, path: path);
                for (int length = 0; length <= data.Length; length++)
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..length], input, "rec"), expectEngine: true, path: path);
                }
            }

            EngineComparison failed = EngineDifferential.AssertSame(EngineOperations.Parse(layout, invalid, EngineInput.Stream, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(failed.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(failed.Rendering, "failure.member = \"text\"\n");
            StringAssert.Contains(failed.Rendering, "position = 7\n");
        }
    }

    /// <summary>
    ///     Every bitfield reads its whole storage unit again, and is charged for it (engine plan section 4.3): three
    ///     bitfields sharing a two-byte unit cost 6 bytes, the tail 1 more. While bits of a unit remain, the position is
    ///     back at the unit's start, so a later member that cannot be placed fails from there. A packed window whose
    ///     placed unit differs from its declared type, the MSVC and high-bit-first rules, and values without sign extension
    ///     (an <see cref="int"/> below 32 bits, a <see cref="ulong"/> from 32 on) read identically from every source.
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
                    EngineComparison comparison = EngineDifferential.AssertSame(
                        EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        expectEngine: true,
                        path: path);
                    StringAssert.Contains(comparison.Rendering, budget < 7 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            EngineComparison complete = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(complete.Rendering, "result.b = Int32 2\n");
            StringAssert.Contains(complete.Rendering, "result.c = Int32 67\n");

            // a leaves bits of its unit, so the position is back at 0 when b's aligned start (4) lies past the input.
            EngineComparison rewound = EngineDifferential.AssertSame(EngineOperations.Parse(aligned, [0x05], EngineInput.Stream, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(rewound.Rendering, "failure = failure CStructSharp.Diagnostics.CStructReadException\n");
            StringAssert.Contains(rewound.Rendering, "position = 0\n");

            EngineComparison unsigned = EngineDifferential.AssertSame(EngineOperations.Parse(signs, [0xFF, 0x01, 0x02, 0x03, 0x04, 0xFF, 0, 0, 0], EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(unsigned.Rendering, "result.s = Int32 15\n");
            StringAssert.Contains(unsigned.Rendering, "result.big = UInt64 ");
            foreach ((CStruct subject, byte[] bytes) in ((CStruct, byte[])[])[(layout, data), (window, [0xFF, 0x81, 0x02, 0x09]), (msvc, [0x05, 0x34, 0x12, 0x09]), (signs, [0xFF, 0x01, 0x02, 0x03, 0x04, 0xFF, 0, 0, 0])])
            {
                for (int length = 0; length <= bytes.Length; length++)
                {
                    foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                    {
                        EngineDifferential.AssertSame(EngineOperations.Parse(subject, bytes[..length], input, "rec"), expectEngine: true, path: path);
                    }
                }

                for (long budget = 1; budget <= (bytes.Length * 3) + 1; budget++)
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(subject, bytes, EngineInput.Span, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }), expectEngine: true, path: path);
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
            EngineComparison complete = EngineDifferential.AssertSame(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(complete.Rendering, "result.items = PrimitiveArray<Byte> [2]\n");
            StringAssert.Contains(complete.Rendering, "PrimitiveArray<Byte> [3]\n");
            for (long budget = 1; budget <= 14; budget++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    EngineComparison comparison = EngineDifferential.AssertSame(
                        EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        expectEngine: true,
                        path: path);
                    StringAssert.Contains(comparison.Rendering, budget < 13 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream3])
                {
                    EngineDifferential.AssertSame(EngineOperations.Parse(layout, data[..length], input, "rec"), expectEngine: true, path: path);
                }
            }

            EngineComparison failed = EngineDifferential.AssertSame(EngineOperations.Parse(invalid, [1, 0x00, 0xD8, 0x41, 0x00, 9], EngineInput.Stream, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(failed.Rendering, "failure.member = \"s\"\n");
            StringAssert.Contains(failed.Rendering, "position = 5\n");

            EngineComparison nested = EngineDifferential.AssertSame(
                EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: new ReadOptions { MaxNestingDepth = 1, }),
                expectEngine: true,
                path: path);
            StringAssert.Contains(nested.Rendering, "CStructReadLimitException");
            StringAssert.Contains(nested.Rendering, "position = 1\n");
            EngineComparison flat = EngineDifferential.AssertSame(
                EngineOperations.Parse(promoted, [1, 2, 3, 9], EngineInput.Span, "rec", options: new ReadOptions { MaxNestingDepth = 1, }),
                expectEngine: true,
                path: path);
            StringAssert.Contains(flat.Rendering, "result.tail = Byte 9\n");
            EngineComparison invisible = EngineDifferential.AssertSame(EngineOperations.Parse(hidden, [1, 5], EngineInput.Span, "rec"), expectEngine: true, path: path);
            StringAssert.Contains(invisible.Rendering, "Undefined expression identifier: m");
        }
    }

    /// <summary>
    ///     A parse whose variables are internal expressions is left to the interpreter (run-time CaptureAll and names
    ///     without slots move to the engine in stage 10), and so is a root the compiler cannot read yet.
    /// </summary>
    [TestMethod]
    public void IneligibleOperations_AreDeclinedBeforeReading()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 d[n]; };");
        var expressions = new Dictionary<string, Syntax.Expr> { ["m"] = new Syntax.Literal(2), };
        using (EngineRecording recording = EngineDiagnostics.Record())
        {
            object value = layout.ParseStreamCore(new MemoryStream([1, 5]), "rec", Expressions.LayoutVariableInput.FromExpressions(expressions), null);
            Assert.AreEqual((byte)1, ((StructValue)value)["n"]);
            Assert.AreEqual(new EngineDecline(EngineOperation.RootRead, EngineSelector.ExpressionInputs), recording.Diagnostics.LastDecline);
        }

        var pointers = new CStruct("struct rec { uint8 lo; uint8 *p; };", 1);
        EngineComparison comparison = EngineDifferential.AssertSame(EngineOperations.Parse(pointers, [0x21, 0], EngineInput.Stream, "rec"), expectEngine: false);
        Assert.AreEqual("rec.p: pointers are not supported yet (stage 5)", comparison.Automatic.LastDecline!.Value.Reason);
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
    private static DifferentialOperation CancelledParse(CStruct layout, byte[] data, int trigger)
    {
        return new DifferentialOperation(
            "Parse (cancelled at byte " + trigger + ")",
            (side, output) =>
            {
                using var cancellation = new CancellationTokenSource();
                using var stream = new CancellingStream(data, trigger, cancellation);
                ReadOptions read = side.Read(new ReadOptions { CancellationToken = cancellation.Token, });
                output.Capture("failure", () => output.Value("result", layout.Parse(stream, "rec", options: read)));
                output.Line("position", stream.Position.ToString(System.Globalization.CultureInfo.InvariantCulture));
            },
            true);
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
