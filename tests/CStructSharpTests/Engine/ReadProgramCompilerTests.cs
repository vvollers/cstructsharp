namespace CStructSharp.Tests;

using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;

/// <summary>
///     The read program compiler (<see cref="ReadProgramCompiler"/>): which steps a struct becomes - placement decided
///     statically where it can be, counts before placement, one read per value kind, captures exactly where the
///     interpreter captures, conditional selection with skip targets and scope steps, shared nested programs - and which
///     layouts it refuses, with a reason naming the innermost member.
/// </summary>
[TestClass]
public class ReadProgramCompilerTests
{
    /// <summary>Constructing a layout compiles nothing; a request compiles once and later requests get the same outcome.</summary>
    [TestMethod]
    public void Programs_AreCompiledLazily_AndCachedPerComposite()
    {
        var layout = new CStruct("struct leaf { uint8 k; }; struct root { leaf a; leaf b; };");
        Assert.IsFalse(layout.Compilation.HasSlotTable, "construction builds no slot table and no program");

        ReadProgramOutcome root = layout.Compilation.GetRootReadProgram("root");
        Assert.IsTrue(root.IsEligible);
        Assert.AreSame(root, layout.Compilation.GetRootReadProgram("root"));
        ReadProgramOutcome composite = layout.Compilation.GetReadProgram(Composite(layout, "root"));
        Assert.AreSame(composite.Program, root.Program!.Nested[0]);
        Assert.AreSame(composite, layout.Compilation.GetReadProgram(Composite(layout, "root")));

        ReadProgram leaf = layout.Compilation.GetReadProgram(Composite(layout, "leaf")).Program!;
        Assert.HasCount(1, composite.Program!.Nested, "both members share the leaf's one program");
        Assert.AreSame(leaf, composite.Program.Nested[0]);
    }

    /// <summary>Concurrent first requests publish one outcome that every caller then sees.</summary>
    [TestMethod]
    public void ConcurrentRequests_ShareOnePublishedOutcome()
    {
        var layout = new CStruct("struct leaf { uint8 k; uint16 n; uint8 v[n]; }; struct root { leaf a; uint8 m; leaf b[m]; };");
        var outcomes = new ReadProgramOutcome[16];
        Parallel.For(0, outcomes.Length, index => outcomes[index] = layout.Compilation.GetRootReadProgram("root"));
        Assert.IsTrue(outcomes.All(outcome => ReferenceEquals(outcome, outcomes[0])));
        Assert.IsTrue(outcomes[0].IsEligible);
    }

    /// <summary>A packed layout never pads, so no member gets a placement step and the tail is empty.</summary>
    [TestMethod]
    public void Placement_Packed_NeedsNoSteps()
    {
        string[] lines = Lines("struct root { uint8 a; uint32 b; uint8 n; uint64 v[n]; uint16 c; };", "root");
        Assert.IsFalse(lines.Any(line => line.StartsWith("Seek", StringComparison.Ordinal) || line.StartsWith("Align", StringComparison.Ordinal)), string.Join("\n", lines));
        Assert.AreEqual("FinishComposite - tail +0", lines[^1]);
    }

    /// <summary>In an aligned layout, padding before a member at a known offset is a relative seek of its exact size, and so is the tail.</summary>
    [TestMethod]
    public void Placement_Aligned_SeeksOverKnownPadding()
    {
        AssertLines(
            new[]
            {
                "ReadUInt8 a UInt8",
                "Seek b +3",
                "ReadUInt32Le b UInt32 le",
                "ReadUInt8 c UInt8",
                "Seek d +7",
                "ReadUInt64Le d UInt64 le",
                "ReadUInt16Le e UInt16 le",
                "FinishComposite - tail +6",
            },
            Lines("struct root { uint8 a; uint32 b; uint8 c; uint64 d; uint16 e; };", "root", aligned: true));
    }

    /// <summary>
    ///     After a member whose size the data decides, a member is aligned at run time only when what the data cannot
    ///     change is too weak: a runtime array of four-byte elements keeps four-byte alignment, a byte array keeps none,
    ///     and a nested struct is padded to its own alignment.
    /// </summary>
    [TestMethod]
    public void Placement_AfterDynamicExtent_AlignsOnlyWhenTheGuaranteeIsTooWeak()
    {
        string[] words = Lines("struct root { uint32 n; uint32 w[n]; uint32 after; uint8 tail; };", "root", aligned: true);
        Assert.IsFalse(words.Any(line => line.StartsWith("Align", StringComparison.Ordinal)), string.Join("\n", words));
        Assert.AreEqual("FinishComposite - tail +3", words[^1], "four-byte alignment survives the array, so the tail is known");

        string[] bytes = Lines("struct root { uint8 n; uint8 v[n]; uint16 half; uint32 word; };", "root", aligned: true);
        CollectionAssert.Contains(bytes, "Align half to 2");
        CollectionAssert.Contains(bytes, "Align word to 4", "two-byte alignment does not give four");
        CollectionAssert.DoesNotContain(bytes, "Align word to 2");

        string[] nested = Lines("struct inner { uint32 n; uint8 v[n]; }; struct root { uint8 a; inner i; uint32 b; };", "root", aligned: true);
        AssertLines(
            new[] { "ReadUInt8 a UInt8", "Seek i +3", "ReadStruct i inner", "ReadUInt32Le b UInt32 le", "FinishComposite - tail +0" },
            nested,
            "a four-byte-aligned struct ends at a multiple of four after its start");
    }

    /// <summary>A member after a conditional one is placed for both outcomes: statically when they agree, otherwise at run time.</summary>
    [TestMethod]
    public void Placement_AfterConditional_MergesBothOutcomes()
    {
        string[] lines = Lines("struct root { uint8 k; if (k) { uint16 x; } uint16 y; uint32 z; };", "root", aligned: true);
        AssertLines(
            new[]
            {
                "EnterConditionalScope - clear k",
                "ReadUInt8 k UInt8",
                "CaptureInteger k -> k",
                "CompleteMember k save [k] restore []",
                "SelectArm - group 0 (k) arm 1, else -> 7",
                "Seek x +1",
                "ReadUInt16Le x UInt16 le",
                "Align y to 2",
                "ReadUInt16Le y UInt16 le",
                "Align z to 4",
                "ReadUInt32Le z UInt32 le",
                "FinishComposite - tail +0",
            },
            lines,
            "the arm's own padding is static (k ends at 1); after it the position is 1 or 4");

        string[] same = Lines("struct root { uint32 k; if (k) { uint32 x; } uint32 y; };", "root", aligned: true);
        CollectionAssert.DoesNotContain(same, "Align y to 4", "both outcomes leave four-byte alignment");
    }

    /// <summary>
    ///     The step placement reproduces the runtime <see cref="PlacementCursor"/> on random members - static and
    ///     data-sized, aligned and packed, conditional and not - at random struct start positions (alignment counts from
    ///     the struct's own first byte).
    /// </summary>
    [TestMethod]
    public void Placement_MatchesThePlacementCursor_OnRandomMembers()
    {
        var random = new Random(20260929);
        int[] alignments = [1, 2, 4, 8, 16];
        for (int trial = 0; trial < 4000; trial++)
        {
            bool aligned = random.Next(4) != 0;
            int count = random.Next(1, 9);
            var members = new (int Alignment, int Kind, int Size, int Unit, bool Conditional)[count];
            for (int index = 0; index < count; index++)
            {
                int unit = alignments[random.Next(4)];
                members[index] = (alignments[random.Next(alignments.Length)], random.Next(3), random.Next(0, 13), unit, random.Next(3) == 0);
            }

            int compositeAlignment = alignments[random.Next(alignments.Length)];

            // Build the placement steps as the compiler does: kind 0 has a static size, kind 1 an extent that is a
            // multiple of its unit (an array or a padded struct), kind 2 an arbitrary extent (terminated text).
            var placement = new ReadPlacement(aligned);
            var steps = new List<ReadStep?>();
            for (int index = 0; index < count; index++)
            {
                (int alignment, int kind, int size, int unit, bool conditional) = members[index];
                ReadPlacement before = placement;
                steps.Add(placement.Place(index, alignment, out ReadStep step) ? step : null);
                if (kind == 0)
                {
                    placement.Advance(size);
                }
                else
                {
                    placement.Restart(kind == 1 ? unit : 1);
                }

                if (conditional)
                {
                    placement = ReadPlacement.Merge(before, placement);
                }
            }

            bool knownTail = placement.TryFinish(compositeAlignment, out int tail);

            // Run the same members over random data sizes and selections, against the runtime cursor.
            for (int run = 0; run < 4; run++)
            {
                long start = random.Next(0, 40);
                var cursor = new PlacementCursor(start, aligned, BitfieldPacking.SysV, false);
                long position = start;
                for (int index = 0; index < count; index++)
                {
                    (int alignment, int kind, int size, int unit, bool conditional) = members[index];
                    if (conditional && random.Next(2) == 0)
                    {
                        continue;
                    }

                    long expected = cursor.AdvanceToField(alignment)!.Value;
                    position = Apply(steps[index], position, start);
                    Assert.AreEqual(expected, position, $"trial {trial} run {run} member {index}");
                    long extent = kind == 0 ? size : kind == 1 ? (long)unit * random.Next(0, 5) : random.Next(0, 9);
                    position += extent;
                    cursor.CompleteField(position);
                }

                long end = cursor.Finish(compositeAlignment)!.Value;
                long finished = knownTail ? position + tail : start + LayoutMath.AlignUp(position - start, compositeAlignment);
                Assert.AreEqual(end, finished, $"trial {trial} run {run} tail");
            }
        }
    }

    /// <summary>An <c>@N</c> assertion the build checked needs no step; one after a data-sized member is checked where the member is placed.</summary>
    [TestMethod]
    public void OffsetAssertion_IsCheckedAtRunTimeOnlyWhenTheBuildCouldNot()
    {
        AssertLines(
            new[] { "ReadUInt8 a UInt8", "ReadUInt8 b UInt8", "FinishComposite - tail +0" },
            Lines("struct root { uint8 a; uint8 b @1; };", "root"));

        string[] dynamic = Lines("struct root { uint8 n; uint8 v[n]; uint32 b @4; };", "root", aligned: true);
        int check = Array.IndexOf(dynamic, "CheckOffset b @4");
        Assert.IsGreaterThan(0, check, string.Join("\n", dynamic));
        Assert.AreEqual("Align b to 4", dynamic[check - 1], "the assertion is checked after the member is placed");
        Assert.AreEqual("ReadUInt32Le b UInt32 le", dynamic[check + 1]);

        string[] conditional = Lines("struct root { uint8 k; if (k) { uint8 x @1; } };", "root");
        CollectionAssert.DoesNotContain(conditional, "CheckOffset x @1", "the arm starts at a known offset that satisfies it");
    }

    /// <summary>A fixed count is checked against the array limit and a data count evaluated, both before the member is placed.</summary>
    [TestMethod]
    public void Counts_PrecedePlacement()
    {
        string[] lines = Lines("struct root { uint8 n; uint32 fixedArray[3]; uint32 dynamicArray[n * 2]; };", "root", aligned: true);
        AssertLines(
            new[]
            {
                "ReadUInt8 n UInt8",
                "CaptureInteger n -> n",
                "CheckFixedCount fixedArray count 3",
                "Seek fixedArray +3",
                "ReadNumericArray fixedArray UInt32 le",
                "EvaluateCount dynamicArray count = (n * 2)",
                "ReadNumericArray dynamicArray UInt32 le",
                "FinishComposite - tail +0",
            },
            lines);
        ReadProgram program = RootProgram(new CStruct("struct root { uint8 n; uint32 v[n]; };"), "root").Nested[0];
        Assert.AreEqual("array length for v", program.ExpressionContexts[program.Steps.Single(step => step.Op == ReadOpCode.EvaluateCount).A]);
    }

    /// <summary>Each scalar value kind and byte order has its own read step.</summary>
    [TestMethod]
    public void Scalars_ReadThroughTheirOwnStep()
    {
        string[] lines = Lines(
            "struct root { uint8 a; int8 b; bool c; int16> d; uint16< e; int24 f; uint24> g; int32 h; uint32> i; int64 j; uint64> k; " +
            "float32 l; float64> m; int48 n; uint48 o; int128 p; uint128 q; float16 r; fixed16_16 s; uuid t; guid u; uleb128_32 v; " +
            "char w; utf8 x; wchar> y; cstring z; };",
            "root");
        AssertLines(
            new[]
            {
                "ReadUInt8", "ReadInt8", "ReadBool", "ReadInt16Be", "ReadUInt16Le", "ReadInt24Le", "ReadUInt24Be", "ReadInt32Le", "ReadUInt32Be",
                "ReadInt64Le", "ReadUInt64Be", "ReadFloat32Le", "ReadFloat64Be", "ReadInt48", "ReadUInt48", "ReadInt128", "ReadUInt128",
                "ReadFloat16", "ReadFixedPoint", "ReadIdentifier", "ReadIdentifier", "ReadLeb128", "ReadCharacter", "ReadCharacter",
                "ReadWideCharacter", "ReadTerminatedText", "FinishComposite",
            },
            lines.Select(line => line[..line.IndexOf(' ', StringComparison.Ordinal)]).ToArray());
    }

    /// <summary>Each array element kind has its own read step, chosen in the interpreter's order.</summary>
    [TestMethod]
    public void Arrays_ReadThroughTheirElementKindsStep()
    {
        string[] lines = Lines(
            "enum e : uint8 { one = 1 }; struct item { uint8 v; }; " +
            "struct root { uint32 numbers[2]; char text[3]; wchar wide[2]; utf8 bounded[4]; e kinds[2]; item items[2]; int48 wides[2]; " +
            "uint32 _[2]; char name[]; };",
            "root");
        AssertLines(
            new[]
            {
                "ReadNumericArray numbers UInt32 le", "ReadCharArray text Char", "ReadWideCharArray wide WChar le", "ReadBoundedText bounded Utf8Unit",
                "ReadEnumArray kinds UInt8 as e", "ReadStructArray items item", "ReadCodecArray wides Int48 le", "SkipElements (unnamed) UInt32 le",
                "ReadTerminatedText name TerminatedAscii",
            },
            lines.Where(line => line.StartsWith("Read", StringComparison.Ordinal) || line.StartsWith("Skip", StringComparison.Ordinal)).ToArray());
    }

    /// <summary>A root field read standalone reads a numeric array element by element, as the interpreter does without a composite cursor.</summary>
    [TestMethod]
    public void Roots_ReadStructsStandaloneFieldsAndDefinitions()
    {
        var layout = new CStruct("#define SIZE (2 * 3)\ntypedef uint16 words[4]; enum e : uint16 { one = 1 }; typedef struct { uint8 k; } aliased; struct plain { uint8 k; };");
        SlotTable table = layout.Compilation.SlotTable;
        AssertLines(new[] { "CheckFixedCount words count 4", "ReadNumericElements words UInt16 le" }, ReadProgramDump.Lines(RootProgram(layout, "words"), table));
        AssertLines(new[] { "ReadEnum e UInt16 le as e" }, ReadProgramDump.Lines(RootProgram(layout, "e"), table));

        ReadProgram aliased = RootProgram(layout, "aliased");
        Assert.AreEqual("aliased", aliased.Name, "a typedef of an inline struct stores the value under the typedef's name");
        Assert.AreEqual(ReadOpCode.ReadRootStruct, aliased.Steps.Single().Op);
        Assert.AreEqual(ReadProgramKind.Root, aliased.Kind);
        Assert.IsFalse(aliased.NotesMembers, "a root program names no member on failure");
        Assert.AreSame(layout.Compilation.ModelQueries.GetRootShape("aliased"), aliased.Shape);

        AssertLines(new[] { "ReadRootStruct - plain" }, ReadProgramDump.Lines(RootProgram(layout, "plain"), table));
        AssertLines(new[] { "EvaluateDefinition - (2 * 3) -> SIZE" }, ReadProgramDump.Lines(RootProgram(layout, "SIZE"), table));
        Assert.AreEqual("definition SIZE", RootProgram(layout, "SIZE").ExpressionContexts[0]);
    }

    /// <summary>
    ///     A capture stores what the interpreter's capture rule stores for the value kind: an integer literal, a
    ///     <c>uint128</c> or enum that may be too wide, or a not-a-number value; an array whose count the data decides
    ///     only when it has elements.
    /// </summary>
    [TestMethod]
    public void Captures_FollowTheValueKind()
    {
        const string definition = """
                                  enum e : uint8 { one = 1 };
                                  struct other { uint8 f; uint8 t; uint8 fixed4; uint8 fixed0; };
                                  struct root {
                                      uint8 i; uint128 w; e k; float32 f; char t[i]; uint8 fixed4[4]; uint8 fixed0[0];
                                      uint8 use[i + w + k + f + t + fixed4 + fixed0];
                                  };
                                  """;
        string[] captures = Lines(definition, "root").Where(line => line.StartsWith("Capture", StringComparison.Ordinal)).ToArray();
        AssertLines(
            new[]
            {
                "CaptureInteger i -> i",
                "CaptureUInt128 w -> w",
                "CaptureEnum k -> k",
                "CaptureNotANumber f -> f (NotANumber: a floating-point value)",
                "CaptureNotANumberIfElements t -> t (NotANumber: text)",
                "CaptureNotANumber fixed4 -> fixed4 (NotANumber: an array)",
            },
            captures,
            "an empty fixed array captures nothing");
    }

    /// <summary>Members no expression can read, structs and byte-counted text capture nothing, as in the interpreter.</summary>
    [TestMethod]
    public void Captures_OnlyWhereTheInterpreterCaptures()
    {
        const string definition = """
                                  struct other { uint8 s; uint8 b; };
                                  struct inner { uint8 x; };
                                  struct root { uint8 unused; inner s; utf8 b[2]; uint8 use[s + b]; };
                                  """;
        string[] lines = Lines(definition, "root");
        Assert.IsFalse(lines.Any(line => line.StartsWith("Capture", StringComparison.Ordinal)), string.Join("\n", lines));
    }

    /// <summary>A capture is published under every prefix an expression spells the name with, once per nested struct level.</summary>
    [TestMethod]
    public void QualifiedPublication_TargetsEveryPrefixThatSpellsTheName()
    {
        const string definition = """
                                  struct leaf { uint8 n; };
                                  struct mid { leaf b; uint8 v[b.n]; };
                                  struct root { mid a; struct { uint8 n; } hdr; uint8 w[a.b.n + hdr.n]; };
                                  """;
        var layout = new CStruct(definition);
        SlotTable table = layout.Compilation.SlotTable;
        ReadProgram root = RootProgram(layout, "root").Nested[0];
        AssertLines(new[] { "ReadStruct a mid, prefix a.", "ReadStruct hdr hdr, prefix hdr." }, ReadProgramDump.Lines(root, table).Where(line => line.StartsWith("ReadStruct", StringComparison.Ordinal)).ToArray());
        ReadProgram leaf = layout.Compilation.GetReadProgram(Composite(layout, "leaf")).Program!;
        CollectionAssert.Contains(ReadProgramDump.Lines(leaf, table), "PublishQualified n n -> a.b.*=a.b.n, b.*=b.n, hdr.*=hdr.n");
        ReadProgram mid = layout.Compilation.GetReadProgram(Composite(layout, "mid")).Program!;
        CollectionAssert.Contains(ReadProgramDump.Lines(mid, table), "ReadStruct b leaf, prefix b.");
    }

    /// <summary>
    ///     Each member selects its arms outermost first and skips to the step after itself when an arm is not selected;
    ///     a switch's arms are its case indexes and <c>default</c> is -1.
    /// </summary>
    [TestMethod]
    public void Conditionals_SelectEachArmWithASkipTarget()
    {
        const string definition = """
                                  struct root {
                                      uint8 kind;
                                      switch (kind) { case 1: { uint8 one; } case 2: { uint8 two; if (two > 1) { uint8 deep; } } default: { uint8 other; } }
                                      if (kind == 0) { uint8 zero; } else { uint8 nonzero; }
                                  };
                                  """;
        ReadProgram program = RootProgram(new CStruct(definition), "root").Nested[0];
        string[] lines = ReadProgramDump.Lines(program, new CStruct(definition).Compilation.SlotTable);
        AssertLines(
            new[]
            {
                "EnterConditionalScope - clear kind, two",
                "ReadUInt8 kind UInt8",
                "CaptureInteger kind -> kind",
                "CompleteMember kind save [kind] restore []",
                "SelectArm - group 0 (kind) arm 0, else -> 6",
                "ReadUInt8 one UInt8",
                "SelectArm - group 0 (kind) arm 1, else -> 10",
                "ReadUInt8 two UInt8",
                "CaptureInteger two -> two",
                "CompleteMember two save [two] restore []",
                "SelectArm - group 0 (kind) arm 1, else -> 13",
                "SelectArm - group 1 ((two > 1)) arm 1, else -> 13",
                "ReadUInt8 deep UInt8",
                "SelectArm - group 0 (kind) arm -1, else -> 15",
                "ReadUInt8 other UInt8",
                "SelectArm - group 2 ((kind == 0)) arm 1, else -> 17",
                "ReadUInt8 zero UInt8",
                "SelectArm - group 2 ((kind == 0)) arm 0, else -> 19",
                "ReadUInt8 nonzero UInt8",
                "FinishComposite - tail +0",
            },
            lines);
        Assert.AreEqual(3, program.GroupCount);
        Assert.AreEqual("conditional selector", program.ExpressionContexts[program.Groups[0].Selector]);
        Assert.IsTrue(program.Steps.Where(step => step.Op == ReadOpCode.SelectArm).All(step => step.Field == -1), "a selector failure names no member");
    }

    /// <summary>
    ///     A conditional composite's scope clears its slotted names at entry and, after a member that holds a nested
    ///     struct, restores the names that struct's declarations may have replaced.
    /// </summary>
    [TestMethod]
    public void ConditionalScope_ClearsSavesAndRestoresSlottedNames()
    {
        const string definition = """
                                  struct inner { uint8 n; uint8 m; };
                                  struct root { uint8 n; inner i; if (n) { uint8 v[m]; } };
                                  """;
        var layout = new CStruct(definition);
        ReadProgram program = RootProgram(layout, "root").Nested[0];
        string[] lines = ReadProgramDump.Lines(program, layout.Compilation.SlotTable);
        AssertLines(
            new[]
            {
                "EnterConditionalScope - clear n",
                "ReadUInt8 n UInt8",
                "CaptureInteger n -> n",
                "CompleteMember n save [n] restore []",
                "ReadStruct i inner",
                "CompleteMember i save [] restore [n]",
                "SelectArm - group 0 (n) arm 1, else -> 9",
                "EvaluateCount v count = m",
                "ReadNumericArray v UInt8",
                "FinishComposite - tail +0",
            },
            lines);
        Assert.AreEqual(program.Scope!.LocalCount, program.Scope.LocalSlots.Length);
        CollectionAssert.Contains(program.Scope.LocalSlots, -1, "a kept name without a slot is mapped to none");
    }

    /// <summary>
    ///     A named nested struct refers to its cached program; an anonymous promoted one is compiled into the program of
    ///     its parent and stores into the parent's value.
    /// </summary>
    [TestMethod]
    public void NestedPrograms_PromotedMembersUseTheParentShape()
    {
        var layout = new CStruct("struct root { uint8 a; struct { uint8 b; struct { uint16 c; }; }; struct { uint8 d; } named; };");
        ReadProgram root = RootProgram(layout, "root").Nested[0];
        ReadProgram promoted = root.Nested[root.Steps.Single(step => step.Op == ReadOpCode.ReadPromotedStruct).A];
        Assert.AreEqual(ReadProgramKind.Promoted, promoted.Kind);
        Assert.AreSame(root.Shape, promoted.Shape);
        ReadProgram inner = promoted.Nested.Single();
        Assert.AreEqual(ReadProgramKind.Promoted, inner.Kind);
        Assert.AreSame(root.Shape, inner.Shape, "transitive promotion stores into the nearest named struct");
        Assert.AreEqual(root.Shape.Names.ToList().IndexOf("c"), inner.GetShapeSlot(0));
        Assert.AreEqual(-1, root.GetShapeSlot(1), "the promoted member itself has no value");

        ReadProgram named = root.Nested[root.Steps.Single(step => step.Op == ReadOpCode.ReadStruct).A];
        Assert.AreEqual(ReadProgramKind.Composite, named.Kind);
        Assert.AreSame(named, layout.Compilation.GetReadProgram(named.Composite!).Program, "an inline named struct's program is cached with its composite");
        Assert.IsTrue(root.NotesMembers);
    }

    /// <summary>
    ///     Anything the engine cannot read yet is refused with the innermost struct and member and the stage that adds it,
    ///     and the refusal reaches every struct and root that holds it.
    /// </summary>
    [TestMethod]
    public void Reasons_NameTheInnermostUnsupportedMember()
    {
        AssertReason("struct root { uint8 low : 4; uint8 high : 4; };", "root", "root.low: " + ReadProgramCompiler.Bitfields);
        AssertReason("struct root { uint8 a; uint32 : 0; };", "root", "root.(unnamed): " + ReadProgramCompiler.Bitfields);
        AssertReason("union u { uint8 a; uint16 b; }; struct root { u value; };", "root", "root.value: " + ReadProgramCompiler.Unions);
        AssertReason("struct root { union { uint8 a; uint16 b; }; };", "root", "root.(anonymous): " + ReadProgramCompiler.Unions);
        AssertReason("union u { uint8 a; uint16 b; };", "u", "u: " + ReadProgramCompiler.Unions);
        AssertReason("struct root { uint8 *p; };", "root", "root.p: " + ReadProgramCompiler.Pointers);
        AssertReason("struct leaf { uint8 *p; }; struct mid { leaf l; }; struct root { uint8 a; mid m[2]; };", "root", "leaf.p: " + ReadProgramCompiler.Pointers);
        AssertReason("struct root { uint8 a; struct { uint8 *p; }; };", "root", "(anonymous struct).p: " + ReadProgramCompiler.Pointers);
        AssertReason("struct root { uint8 grid[2][2]; uint8 *p; };", "root", "root.p: " + ReadProgramCompiler.Pointers);
        AssertReason("struct root { uint8 a; };", "missing", "missing: the layout declares no such root");
        AssertReason("#define MAGIC \"PNG\"\nstruct root { uint8 a; };", "MAGIC", "MAGIC: " + ReadProgramCompiler.UnreadableRoot);
    }

    /// <summary>
    ///     The data-sized shapes compile: an <c>[EOF]</c> array is counted to the end after it is placed, a terminated array
    ///     is scanned there and its terminator skipped after the elements (a <c>wchar</c> array skips it inside its read,
    ///     before validating its text), a multidimensional array is limited by its total element count and nested after its
    ///     flat read (character rows inside their own step), and a caller's codec reads through its own steps.
    /// </summary>
    [TestMethod]
    public void DataSizedShapes_CompileToCountAndShapeSteps()
    {
        AssertLines(
            ["CountToEnd rest element size 1", "ReadNumericArray rest UInt8", "FinishComposite - tail +0"],
            Lines("struct root { uint8 rest[EOF]; };", "root"));
        AssertLines(
            ["CountTerminated items element size 2", "ReadNumericArray items UInt16 le", "SkipTerminator items +2", "FinishComposite - tail +0"],
            Lines("struct root { uint16 items[]; };", "root"));
        AssertLines(
            ["CheckFixedCount grid count 12", "ReadNumericList grid UInt16 le", "ReshapeTable grid dimensions 3x4", "CheckFixedCount names count 10", "ReadCharTable names Char", "FinishComposite - tail +0"],
            Lines("struct root { uint16 grid[3][4]; char names[2][5]; };", "root"));

        var custom = new CStruct("struct root { vlq value; vlq values[value]; };", compilationOptions: new CStructCompilationOptions { Codecs = [VlqCodec.Instance], });
        AssertLines(
            ["ReadCustom value Custom", "CaptureInteger value -> value", "EvaluateCount values count = value", "ReadCustomArray values Custom", "FinishComposite - tail +0"],
            ReadProgramDump.Lines(RootProgram(custom, "root").Nested[0], custom.Compilation.SlotTable));
    }

    /// <summary>
    ///     A caller's codec may take fewer bytes than the size it declares, so the position after it (or after a struct
    ///     holding it) is not known when the program is built: a later member is aligned at run time even though the layout
    ///     compiled its offset, and the struct's tail is aligned at run time too.
    /// </summary>
    [TestMethod]
    public void FixedSizeCustomCodec_PlacesLaterMembersAtRunTime()
    {
        var options = new CStructCompilationOptions { Codecs = [FixedWordCodec.Instance], };
        var layout = new CStruct("struct inner { word4 w; uint8 b; }; struct root { word4 a; uint32 x; inner i; uint16 y; };", aligned: true, compilationOptions: options);
        ReadProgram root = RootProgram(layout, "root").Nested[0];

        // i ends a multiple of its alignment (4) after its start, so y needs no run-time step; i itself ends aligned at run time.
        AssertLines(
            ["ReadCustom a Custom", "Align x to 4", "ReadUInt32Le x UInt32 le", "ReadStruct i inner", "ReadUInt16Le y UInt16 le", "FinishComposite - tail +2"],
            ReadProgramDump.Lines(root, layout.Compilation.SlotTable));
        AssertLines(
            ["ReadCustom w Custom", "ReadUInt8 b UInt8", "FinishComposite - tail to 4"],
            ReadProgramDump.Lines(root.Nested.Single(), layout.Compilation.SlotTable));
    }

    /// <summary>Applies one placement step to a simulated position.</summary>
    /// <param name="step">The step, or <see langword="null"/> when the member needs none.</param>
    /// <param name="position">The position before the step.</param>
    /// <param name="start">The struct's first byte.</param>
    /// <returns>The position after the step.</returns>
    private static long Apply(ReadStep? step, long position, long start) => step switch
    {
        { Op: ReadOpCode.Seek, } seek => position + seek.A,
        { Op: ReadOpCode.Align, } align => start + LayoutMath.AlignUp(position - start, align.A),
        _ => position,
    };

    /// <summary>Asserts that step lines are exactly the expected ones, showing both as text on failure.</summary>
    /// <param name="expected">The expected lines.</param>
    /// <param name="actual">The actual lines.</param>
    /// <param name="message">What the lines show.</param>
    private static void AssertLines(IEnumerable<string> expected, IEnumerable<string> actual, string? message = null)
        => Assert.AreEqual(string.Join("\n", expected), string.Join("\n", actual), message);

    /// <summary>Asserts that a root is refused with an exact reason.</summary>
    /// <param name="definition">The layout.</param>
    /// <param name="root">The root.</param>
    /// <param name="reason">The expected reason.</param>
    private static void AssertReason(string definition, string root, string reason)
    {
        ReadProgramOutcome outcome = new CStruct(definition).Compilation.GetRootReadProgram(root);
        Assert.IsFalse(outcome.IsEligible, definition);
        Assert.AreEqual(reason, outcome.Reason, definition);
    }

    /// <summary>Returns a composite of a layout by name.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="name">The composite's name.</param>
    /// <returns>The composite.</returns>
    private static CompiledCompositeType Composite(CStruct layout, string name) => (CompiledCompositeType)layout.CompiledModel.Symbols[name].Symbol.Definition!;

    /// <summary>Returns the program of an eligible root.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="root">The root.</param>
    /// <returns>The root program.</returns>
    private static ReadProgram RootProgram(CStruct layout, string root)
    {
        ReadProgramOutcome outcome = layout.Compilation.GetRootReadProgram(root);
        Assert.IsNotNull(outcome.Program, outcome.Reason);
        return outcome.Program;
    }

    /// <summary>Returns the compact step lines of a struct root's composite program.</summary>
    /// <param name="definition">The layout.</param>
    /// <param name="root">The struct root.</param>
    /// <param name="aligned">Whether the layout is aligned.</param>
    /// <returns>The lines.</returns>
    private static string[] Lines(string definition, string root, bool aligned = false)
    {
        var layout = new CStruct(definition, aligned: aligned);
        return ReadProgramDump.Lines(RootProgram(layout, root).Nested[0], layout.Compilation.SlotTable);
    }
}
