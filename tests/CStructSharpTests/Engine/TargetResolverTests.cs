namespace CStructSharp.Tests;

/// <summary>
///     Holds the compiled engine's path resolution (<c>TargetResolver</c>) to the interpreter's through the differential
///     harness, path by path: every member kind the resolver walks past (read to measure it, or skipped by its size),
///     pointer accessors with their checks and limits, selected bitfields in their placed units, conditional members,
///     nested parses and their debug records, and the failures of paths that select nothing. Each case must run on the
///     engine, and each is also truncated at every length and read under every byte budget.
/// </summary>
[TestClass]
public class TargetResolverTests
{
    /// <summary>The execution paths every case runs under.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.GeneralOnly];

    /// <summary>
    ///     Members the walk measures by reading them - terminated text (read to capture and again to measure), LEB128, an
    ///     unsized character array, nested structs with counts, a terminated array - and members it skips by their size -
    ///     a union, fixed arrays - place every later path where the parse places it, packed and aligned.
    /// </summary>
    [TestMethod]
    public void MeasuredMembers_PlaceLaterPathsIdentically()
    {
        const string Definition = "struct inner { uint8 n; uint8 d[n]; }; " +
                                  "struct rec { uint8 k; cstring name; uleb128 v; char flex[]; inner in; inner arr[2]; uint16 term[]; " +
                                  "union { uint8 a; uint16 b; } u; struct { uint8 x; }; struct { uint8 q; } hdr; uint8 tail[hdr.q]; uint8 last; };";
        byte[] data = [2, 104, 105, 0, 0x81, 0x01, 97, 98, 0, 2, 5, 6, 1, 7, 0, 1, 0, 2, 0, 0, 0, 3, 4, 8, 2, 9, 10, 11];
        string[] paths =
        [
            "rec.name", "rec.v", "rec.flex", "rec.in", "rec.in.d", "rec.in.d[1]", "rec.arr[1]", "rec.arr[0].d[0]", "rec.arr", "rec.term",
            "rec.term[1]", "rec.u", "rec.u.b", "rec.x", "rec.hdr", "rec.hdr.q", "rec.tail", "rec.tail[1]", "rec.last",
        ];
        Sweep(new CStruct(Definition), data, paths);
        Sweep(new CStruct(Definition, aligned: true), data, paths);
    }

    /// <summary>
    ///     Only the pointers a path names are followed: a target and its members, a pointer's own address, the levels of
    ///     a pointer to a pointer, a string target, a null target, a cycle (through a union view, which a parse does not
    ///     follow); and a <c>@count</c> target is rejected. Every
    ///     check the resolver makes is exercised through its option: following disabled, the depth limit, the target size
    ///     limit, relative addressing (an origin that overflows fails the path) and the nesting limit.
    /// </summary>
    [TestMethod]
    public void PointerPaths_FollowOnlyTheNamedPointers()
    {
        var layout = new CStruct(
            "struct rec { uint8 tag; target* p; target** pp; union { node* list; uint32 bits; } u; char* s; uint8* raw @count(tag); target* nul; uint8 tail; }; " +
            "struct node { uint8 v; node* next; }; struct target { uint16 w; };",
            pointerSize: 4);
        byte[] data =
        [
            2, 26, 0, 0, 0, 28, 0, 0, 0, 32, 0, 0, 0, 37, 0, 0, 0, 40, 0, 0, 0, 0, 0, 0, 0, 9,
            0x34, 0x12, 26, 0, 0, 0, 7, 32, 0, 0, 0, 104, 105, 0, 1, 2,
        ];
        string[] paths =
        [
            "rec.p.value", "rec.p.value.w", "rec.p.address", "rec.pp.value", "rec.pp.value.value", "rec.pp.value.value.w", "rec.pp.value.address",
            "rec.pp.address", "rec.u.list.value.v", "rec.u.list.value.next.value", "rec.u.list.value.next.value.v", "rec.s.value", "rec.raw.value", "rec.raw",
            "rec.nul.value", "rec.nul.value.w", "rec.p.w", "rec.p.address.w", "rec.pp.value.w", "rec.p.value.w.x", "rec.tail",
        ];
        Sweep(layout, data, paths);

        // The second follow of the self-referencing node is the first one's target again; a relative address past the
        // position range is a path failure, not a read failure.
        EngineComparison cycle = EngineDifferential.AssertSame(EngineOperations.ResolveAddress(layout, data, EngineInput.Span, "rec.u.list.value.next.value.v"), expectEngine: true);
        StringAssert.Contains(cycle.Rendering, "Cyclic pointer target detected at stream address 32 (path");
        var overflow = new ReadOptions { AddressingMode = PointerAddressingMode.Relative, Origin = long.MaxValue, };
        EngineComparison relative = EngineDifferential.AssertSame(EngineOperations.ReadValue(layout, data, EngineInput.Span, "rec.p.value", options: overflow), expectEngine: true);
        StringAssert.Contains(relative.Rendering, "failure = failure CStructSharp.Diagnostics.CStructPathException\n");
        StringAssert.Contains(relative.Rendering, "Relative pointer target overflowed the stream address range (path");

        ReadOptions[] options =
        [
            new ReadOptions { DereferencePointers = false, },
            new ReadOptions { MaxPointerDepth = 0, }, new ReadOptions { MaxPointerDepth = 1, }, new ReadOptions { MaxPointerDepth = 2, },
            new ReadOptions { MaxPointerTargetBytes = 0, }, new ReadOptions { MaxPointerTargetBytes = 2, }, new ReadOptions { MaxPointerTargetBytes = 4, },
            new ReadOptions { AddressingMode = PointerAddressingMode.Relative, Origin = 0, },
            new ReadOptions { AddressingMode = PointerAddressingMode.Relative, Origin = -3, },
            new ReadOptions { AddressingMode = PointerAddressingMode.Relative, Origin = long.MaxValue, },
            new ReadOptions { MaxNestingDepth = 1, }, new ReadOptions { MaxNestingDepth = 2, },
        ];
        foreach (ExecutionPath path in Paths)
        {
            foreach (ReadOptions read in options)
            {
                foreach (string selected in paths)
                {
                    Same(EngineOperations.ReadValue(layout, data, EngineInput.Span, selected, options: read), path);
                    Same(EngineOperations.ResolveAddress(layout, data, EngineInput.ChunkedStream3, selected, options: read), path);
                    Same(EngineOperations.Parse(layout, data, EngineInput.Stream, selected, options: read), path);
                }
            }
        }
    }

    /// <summary>
    ///     A selected bitfield reads the storage unit its struct placed - a packed SysV window, a shared MSVC unit, a unit
    ///     that straddles - at the bit offset the placement gave it, and one in a union reads a unit of its declared size.
    /// </summary>
    [TestMethod]
    public void SelectedBitfields_ReadTheirPlacedUnit()
    {
        const string Definition = "struct rec { uint8 a : 3; uint16 b : 9; uint8 c : 4; uint32 d : 20; uint8 tail; }; union u { uint16 bits : 3; uint8 byte; };";
        byte[] data = [0xB5, 0x6C, 0xD3, 0x7A, 0x19, 0xE4, 0x2F, 0x81, 0x44, 0x03];
        string[] paths = ["rec.a", "rec.b", "rec.c", "rec.d", "rec.tail"];
        foreach (BitfieldPacking packing in (BitfieldPacking[])[BitfieldPacking.SysV, BitfieldPacking.Msvc])
        {
            foreach (bool aligned in (bool[])[false, true])
            {
                var layout = new CStruct(Definition, aligned: aligned, compilationOptions: new CStructCompilationOptions { BitfieldPacking = packing, });
                Sweep(layout, data, paths);
                Sweep(layout, data, ["u.bits", "u.byte"]);
            }
        }
    }

    /// <summary>
    ///     Conditional members ahead of the target are placed only when their arm is selected, their names are scoped as a
    ///     parse scopes them, and a later count names the member that selects the arms.
    /// </summary>
    [TestMethod]
    public void ConditionalMembers_PlaceLaterPathsIdentically()
    {
        var layout = new CStruct(
            "struct rec { uint8 kind; if (kind == 1) { uint8 n; uint8 d[n]; } else { uint16 w; } " +
            "switch (kind) { case 1: { uint8 s1; } default: { uint16 s2; } } uint8 e[kind + 1]; uint8 tail; };");
        string[] paths = ["rec.n", "rec.d", "rec.d[1]", "rec.w", "rec.s1", "rec.s2", "rec.e", "rec.e[0]", "rec.tail"];
        Sweep(layout, [1, 2, 5, 6, 7, 8, 9, 10], paths);
        Sweep(layout, [0, 2, 0, 3, 0, 8, 9, 10], paths);
    }

    /// <summary>
    ///     <c>Parse</c> and the debug parses of a nested path read the struct or union it selects at its address, recorded
    ///     under the path's names; a path to an array of structs parses its first element, a <c>.value</c> of a null pointer
    ///     parses at position 0, and anything that is not a struct or union fails.
    /// </summary>
    [TestMethod]
    public void NestedParses_ReadTheSelectedComposite()
    {
        var layout = new CStruct(
            "struct inner { uint8 a; uint16 b; }; union both { uint16 w; uint8 lo; }; " +
            "struct rec { uint8 n; inner items[n]; both u; inner* p; inner* nul; struct { uint8 x; } hdr; uint8 tail; };",
            pointerSize: 2);
        byte[] data = [2, 1, 2, 0, 3, 4, 0, 0x34, 0x12, 16, 0, 0, 0, 7, 9, 0, 5, 6, 0];
        AssertEligible(layout, "rec");
        string[] paths = ["rec.items[1]", "rec.items", "rec.u", "rec.p.value", "rec.nul.value", "rec.hdr", "rec.tail", "rec.p", "rec.p.address"];
        foreach (ExecutionPath path in Paths)
        {
            foreach (string selected in paths)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Memory, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    Same(EngineOperations.Parse(layout, data, input, selected), path);
                    Same(EngineOperations.ParseWithDebug(layout, data, input, selected), path);
                    Same(EngineOperations.ReadValueWithDebug(layout, data, input, selected), path);
                }

                Same(EngineOperations.ParseAsync(layout, data, EngineInput.ChunkedStream3, selected), path);
                Same(EngineOperations.ParseWithDebugAsync(layout, data, EngineInput.ExposedStream, selected), path);
                for (int length = 0; length < data.Length; length++)
                {
                    Same(EngineOperations.ParseWithDebug(layout, data[..length], EngineInput.Span, selected), path);
                }
            }
        }
    }

    /// <summary>
    ///     A path that selects nothing fails identically: an unknown member or root, an index on a scalar, too many
    ///     indexes, an index out of range, traversal through a scalar or an unindexed array, and a child of a root that is
    ///     not a struct.
    /// </summary>
    [TestMethod]
    public void PathFailures_AreReportedIdentically()
    {
        var layout = new CStruct("typedef uint8 bytes[2]; struct inner { uint8 a; }; struct rec { uint8 n; inner items[n]; uint8 grid[2][2]; uint8 tail; };");
        byte[] data = [1, 5, 1, 2, 3, 4, 9];
        string[] paths =
        [
            "rec.missing", "rec.tail[0]", "rec.grid[0][1][0]", "rec.items[1]", "rec.grid[2]", "rec.grid[1][2]", "rec.tail.x", "rec.items.a",
            "bytes.x", "bytes", "rec.grid[1]", "rec.grid[1][1]", "nothing.x",
        ];
        Sweep(layout, data, paths);
    }

    /// <summary>
    ///     A union ahead of the target is skipped by its size after the counts inside it are checked, whatever arm or view
    ///     they sit in: every element and nesting limit around the needed value fails or succeeds identically.
    /// </summary>
    [TestMethod]
    public void UnionMeasures_CheckTheirCountsAgainstTheLimits()
    {
        var layout = new CStruct(
            "#define N 3\nstruct pair { uint8 v[2]; }; " +
            "struct rec { uint8 k; union { uint8 raw[N]; pair twins; struct { uint8 z[1]; }; } u; uint8 tail; };");
        byte[] data = [1, 2, 3, 4, 9];
        AssertEligible(layout, "rec");
        foreach (ExecutionPath path in Paths)
        {
            for (int limit = 0; limit <= 4; limit++)
            {
                var elements = new ReadOptions { MaxArrayElements = limit, };
                Same(EngineOperations.ReadValue(layout, data, EngineInput.Span, "rec.tail", options: elements), path);
                Same(EngineOperations.ResolveAddress(layout, data, EngineInput.Stream, "rec.tail", options: elements), path);
                Same(EngineOperations.ResolveAddress(layout, data, EngineInput.Stream, "rec.tail", new Dictionary<string, int> { ["N"] = limit, }), path);
                if (limit > 0)
                {
                    Same(EngineOperations.ResolveAddress(layout, data, EngineInput.Span, "rec.tail", options: new ReadOptions { MaxNestingDepth = limit, }), path);
                }
            }
        }
    }

    /// <summary>
    ///     An address is resolved over a stream that holds no data when nothing before the target must be read (as the
    ///     memory schema checks its compiled offsets), and fails where a value must be read.
    /// </summary>
    [TestMethod]
    public void AddressResolution_NeedsNoDataForFixedPlacement()
    {
        var layout = new CStruct("union view { uint8 raw[8]; struct { uint8 _[3]; uint8 value[2]; } f0; }; struct rec { uint8 n; uint8 items[n]; uint8 tail; };");
        AssertEligible(layout, "view", "rec");
        foreach (string path in (string[])["view.f0.value", "view.raw", "rec.items", "rec.tail"])
        {
            var operation = new DifferentialOperation(
                "ResolveAddress(Stream.Null) " + path,
                (side, output) => output.Capture("failure", () => output.Value("result", layout.ResolveAddress(Stream.Null, path, options: side.Read(null)))),
                Engine: true);
            EngineDifferential.AssertSame(operation);
        }

        Assert.AreEqual(3L, layout.ResolveAddress(Stream.Null, "view.f0.value"));
        Assert.ThrowsExactly<Diagnostics.CStructReadException>(() => layout.ResolveAddress(Stream.Null, "rec.items"));
    }

    /// <summary>
    ///     Further member shapes resolve identically: anonymous unions and structs whose members are addressed as their
    ///     parent's (in a struct and in a union), enum and enum-bitfield counts, offset assertions, arrays of pointers,
    ///     unions and multidimensional arrays of structs (whose elements are stepped over or measured), read-to-end and
    ///     terminated arrays of structs, a count named through a nested struct (<c>hdr.n</c>), a typedef root, and pointers to
    ///     pointers to <c>void</c>.
    /// </summary>
    [TestMethod]
    public void MemberShapes_ResolveIdentically()
    {
        (string Definition, byte[] Data, string[] Paths)[] cases =
        [
            ("struct r { uint8 a; union { uint8 x; uint16 y; }; uint8 t; };", [1, 2, 3, 4], ["r.x", "r.y", "r.t"]),
            ("union u { struct { uint8 a; uint8 b; }; uint16 w; };", [1, 2], ["u.a", "u.b", "u.w"]),
            ("enum e : uint8 { A = 1, B = 2 }; struct r { e k; uint8 d[k]; uint8 t; };", [2, 5, 6, 7], ["r.k", "r.d[1]", "r.t"]),
            ("enum e : uint8 { A = 1, B = 2 }; struct r { e k : 3; uint8 pad : 5; uint8 d[k]; uint8 t; };", [2, 5, 6, 7], ["r.k", "r.pad", "r.d[1]", "r.t"]),
            ("struct r { uint8 n; uint8 d[n]; uint8 t @3; };", [2, 5, 6, 7], ["r.t", "r.d"]),
            ("struct node { uint8 v; }; struct r { node* ptrs[2]; uint8 t; };", [3, 4, 7, 8, 9], ["r.ptrs[1].value.v", "r.ptrs[0].address", "r.ptrs", "r.t"]),
            ("union u { uint8 x; uint16 y; }; struct r { u items[2]; uint8 t; };", [1, 2, 3, 4, 5], ["r.items[1].x", "r.items[1]", "r.t"]),
            ("struct e { uint8 n; uint8 d[n]; }; struct r { e grid[2][2]; uint8 t; };", [1, 5, 0, 2, 6, 7, 0, 9], ["r.grid[1][0].d[1]", "r.grid[1]", "r.grid[0][1]", "r.t"]),
            ("struct e { uint8 a; uint8 b; }; struct r { uint8 k; e list[]; uint8 t; };", [1, 2, 3, 4, 5, 0, 0, 9], ["r.list[1].b", "r.list", "r.t"]),
            ("struct r { uint8 k; uint16 rest[EOF]; };", [1, 2, 0, 3, 0], ["r.rest[1]", "r.rest"]),
            ("struct h { uint8 n; }; struct r { h hdr; uint8 d[hdr.n]; uint8 t; };", [2, 5, 6, 9], ["r.hdr.n", "r.d[1]", "r.t"]),
            ("typedef struct { uint8 a; uint8 b; } T; struct r { T one; uint8 t; };", [1, 2, 3], ["T.b", "T", "r.one.b", "r.t"]),
            ("struct r { void** pp; uint8 t; };", [2, 0, 0, 0], ["r.pp.value", "r.pp.value.value", "r.pp.value.address", "r.t"]),
            ("struct r { wchar names[2][2]; char rows[2][3]; uint8 t; };", [65, 0, 66, 0, 67, 0, 68, 0, 97, 98, 99, 100, 101, 102, 9], ["r.names[1]", "r.rows[0]", "r.rows[1][2]", "r.t"]),
        ];
        foreach ((string definition, byte[] data, string[] paths) in cases)
        {
            Sweep(new CStruct(definition, pointerSize: 1), data, paths);
        }
    }

    /// <summary>
    ///     Runs every path operation for each path over several sources, then over every truncation of the input and every
    ///     byte budget up to twice its length, comparing the engine with the interpreter.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="data">The input.</param>
    /// <param name="paths">The paths.</param>
    private static void Sweep(CStruct layout, byte[] data, string[] paths)
    {
        foreach (string selected in paths)
        {
            string root = selected.Split('.')[0];
            if (root != "nothing")
            {
                AssertEligible(layout, root);
            }
        }

        foreach (ExecutionPath path in Paths)
        {
            foreach (string selected in paths)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream3, EngineInput.Sequence])
                {
                    Same(EngineOperations.ReadValue(layout, data, input, selected), path);
                    Same(EngineOperations.ResolveAddress(layout, data, input, selected), path);
                }

                Same(EngineOperations.ReadValue<string>(layout, data, EngineInput.Memory, selected), path);
                Same(EngineOperations.GetArrayLength(layout, data, EngineInput.Span, selected), path);
                Same(EngineOperations.GetArrayLength(layout, data, EngineInput.ChunkedStream1, selected), path);
                Same(EngineOperations.Parse(layout, data, EngineInput.Span, selected), path);
                Same(EngineOperations.ReadValueWithDebug(layout, data, EngineInput.Stream, selected), path);
                for (int length = 0; length < data.Length; length++)
                {
                    Same(EngineOperations.ReadValue(layout, data[..length], EngineInput.Span, selected), path);
                    Same(EngineOperations.ResolveAddress(layout, data[..length], EngineInput.ChunkedStream1, selected), path);
                    Same(EngineOperations.GetArrayLength(layout, data[..length], EngineInput.Stream, selected), path);
                }

                for (long budget = 1; budget <= (2 * data.Length) + 2; budget++)
                {
                    var read = new ReadOptions { MaxTotalBytesRead = budget, };
                    Same(EngineOperations.ReadValue(layout, data, EngineInput.Span, selected, options: read), path);
                    Same(EngineOperations.ReadValue(layout, data, EngineInput.ChunkedStream3, selected, options: read), path);
                    Same(EngineOperations.ResolveAddress(layout, data, EngineInput.Span, selected, options: read), path);
                }
            }
        }
    }

    /// <summary>
    ///     Compares one operation, requiring the engine to run it exactly when the operation expects it: a path whose root's
    ///     program is eligible (every root here but an unknown one, <see cref="AssertEligible"/>).
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="path">The execution path both sides use.</param>
    private static void Same(DifferentialOperation operation, ExecutionPath path)
        => EngineDifferential.AssertSame(operation, path: path);

    /// <summary>Asserts that a root's program is eligible, so the comparisons of its paths hold the engine to the interpreter.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="roots">The roots.</param>
    private static void AssertEligible(CStruct layout, params string[] roots)
    {
        foreach (string root in roots)
        {
            Assert.IsTrue(layout.Compilation.GetRootReadProgram(root).IsEligible, root + ": " + layout.Compilation.GetRootReadProgram(root).Reason);
        }
    }
}
