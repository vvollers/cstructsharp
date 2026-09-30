namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>
///     The compiled read engine's control flow where the sweeps and corpora do not reach: an offset assertion checked
///     before its member is placed, cancellation only at the documented boundaries, nesting limits with promoted
///     members, the conditional variable scope, qualified prefixes through static plans, the member, path and offset a
///     failure reports, and roots that are definitions, typedefs, enums or type spellings. Each case checks its
///     outcomes against the golden outcomes (<see cref="EngineGolden"/>) under both execution paths, and pins the
///     expected outcome. The codecs, arrays, shared storage and pointers have their own classes
///     (<see cref="ReadEngineCodecTests"/>, <see cref="ReadEngineArrayTests"/>, <see cref="ReadEngineSharedStorageTests"/>,
///     <see cref="ReadEnginePointerTests"/>).
/// </summary>
[TestClass]
public class ReadEngineTests
{
    /// <summary>
    ///     A runtime-checked <c>@N</c> assertion is checked before its member is placed: a failing assertion leaves the
    ///     position before the padding and names the member, and it wins over a start that lies past the input.
    /// </summary>
    [TestMethod]
    public void OffsetAssertion_IsCheckedBeforeTheMemberIsPlaced()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 d[n]; uint32 x @8; };", aligned: true);
        foreach (ExecutionPath path in ExecutionPaths.Both)
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
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            for (int trigger = 0; trigger < data.Length; trigger++)
            {
                string outcome = EngineDifferential.AssertGolden(EngineOperations.CancelledParse(elements, data, trigger), path: path);

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
            string outcome = EngineDifferential.AssertGolden(EngineOperations.CancelledParse(primitives, [1, 2, 0, 3], trigger), path: ExecutionPath.NoFastPaths);
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
        foreach (ExecutionPath path in ExecutionPaths.Both)
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
        foreach (ExecutionPath path in ExecutionPaths.Both)
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
        foreach (ExecutionPath path in ExecutionPaths.Both)
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
        foreach (ExecutionPath path in ExecutionPaths.Both)
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
        foreach (ExecutionPath path in ExecutionPaths.Both)
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
            foreach (ExecutionPath path in ExecutionPaths.Both)
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
}
