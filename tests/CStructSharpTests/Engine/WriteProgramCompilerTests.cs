namespace CStructSharp.Tests;

using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;

/// <summary>
///     The write program compiler (<see cref="WriteProgramCompiler"/>): the order of each member's steps (selection,
///     value, count, placement, encoding, capture), which steps a failure is attributed to a member for, the placement
///     it shares with the reader, a promoted struct's own program over its parent's value, and the layouts it refuses
///     with a reason naming the innermost member.
/// </summary>
[TestClass]
public class WriteProgramCompilerTests
{
    /// <summary>Constructing a layout compiles nothing; a request compiles once, later requests get the same outcome, and nested structs share one program.</summary>
    [TestMethod]
    public void Programs_AreCompiledLazily_AndCachedPerComposite()
    {
        var layout = new CStruct("struct leaf { uint8 k; }; struct root { leaf a; leaf b; };");
        Assert.IsFalse(layout.Compilation.HasSlotTable, "construction builds no slot table and no program");

        WriteProgramOutcome root = layout.Compilation.GetRootWriteProgram("root");
        Assert.IsTrue(root.IsEligible);
        Assert.AreSame(root, layout.Compilation.GetRootWriteProgram("root"));
        WriteProgram composite = root.Program!.Nested[0];
        Assert.AreSame(composite, layout.Compilation.GetWriteProgram(Composite(layout, "root")).Program);
        Assert.HasCount(1, composite.Nested, "both members share the leaf's one program");
        Assert.AreSame(layout.Compilation.GetWriteProgram(Composite(layout, "leaf")).Program, composite.Nested[0]);
    }

    /// <summary>
    ///     A member's steps follow the interpreter's order - its value, its count, its placement, its encoding and its
    ///     capture - and exactly those steps attribute a failure to the member; the tail padding belongs to none.
    /// </summary>
    [TestMethod]
    public void MemberSteps_FollowTheInterpretersOrder_AndNoteOnlyTheMember()
    {
        WriteProgram program = Program("struct root { uint8 n; uint32 v[n]; };", "root", aligned: true);
        Assert.AreEqual(
            "LoadMember n|WriteNumeric n|CaptureValue n|LoadMember v|EvaluateCount v|Seek v|WriteNumericArray v|FinishComposite -",
            Render(program));
        Assert.AreEqual("0,0,0,1,1,1,1,-1", string.Join(",", program.NotedMembers));
    }

    /// <summary>
    ///     A conditional member is preceded by its arm tests, which name the member (for the inactive-member check) but do
    ///     not attribute a failure to it, and skip to the step after it; the composite's scope steps surround the members.
    /// </summary>
    [TestMethod]
    public void ConditionalMembers_TestTheirArms_AndSkipPastThemselves()
    {
        WriteProgram program = Program("struct root { uint8 f; if (f == 1) { uint8 x; } uint8 t; };", "root");
        string[] steps = Render(program).Split('|');
        int select = Array.IndexOf(steps, "SelectArm x");
        Assert.IsGreaterThanOrEqualTo(0, select, Render(program));
        Assert.AreEqual(-1, program.NotedMembers[select]);
        Assert.AreEqual("LoadMember t", steps[program.Steps[select].B], "an inactive member continues at the next member");
    }

    /// <summary>
    ///     An anonymous promoted struct is written by a program of its own that looks its members up in the parent's value,
    ///     outside the member-noting context; its members are noted inside that program.
    /// </summary>
    [TestMethod]
    public void PromotedStruct_HasItsOwnProgram_OverTheParentsShape()
    {
        WriteProgram program = Program("struct root { uint8 a; struct { uint8 b; uint16 c; }; };", "root");
        int promoted = Array.FindIndex(program.Steps, step => step.Op == WriteOpCode.WritePromotedStruct);
        Assert.AreEqual(-1, program.NotedMembers[promoted]);
        WriteProgram nested = program.Nested[program.Steps[promoted].A];
        Assert.AreEqual(WriteProgramKind.Promoted, nested.Kind);
        Assert.AreSame(program.Shape, nested.Shape);
        Assert.AreEqual("LoadMember b|WriteNumeric b|LoadMember c|WriteNumeric c|FinishComposite -", Render(nested));
    }

    /// <summary>An unsized character array is one terminated string, encoded (and placed) as its terminated view.</summary>
    [TestMethod]
    public void UnsizedText_IsWrittenAsItsTerminatedView()
    {
        WriteProgram program = Program("struct root { uint8 a; wchar< name[]; uint8 t; };", "root", aligned: true);
        int name = Array.FindIndex(program.Fields, field => field.Name == "name");
        Assert.AreNotSame(program.Fields[name], program.ValueFields[name]);
        Assert.AreEqual(1, program.ValueFields[name].Alignment, "the view is placed unaligned, as the interpreter's writer places it");
        StringAssert.Contains(Render(program), "WriteCodecValue name");
    }

    /// <summary>The shapes a later sub-stage adds are refused with a reason naming the struct and member.</summary>
    [TestMethod]
    public void UnsupportedShapes_AreRefused_WithTheirMember()
    {
        (string Layout, string Reason)[] cases =
        [
            ("struct root { uint8 a : 3; uint8 b : 5; };", "root.a: " + WriteProgramCompiler.Bitfields),
            ("union u { uint8 a; uint16 b; }; struct root { u v; };", "root.v: " + WriteProgramCompiler.Unions),
            ("struct root { uint8 t; union { uint8 a; uint16 b; }; };", "root.(anonymous): " + WriteProgramCompiler.Unions),
            ("struct root { uint8 *p; };", "root.p: " + WriteProgramCompiler.Pointers),
            ("struct root { uint16 v[EOF]; };", "root.v: " + WriteProgramCompiler.DataSizedArrays),
            ("struct root { uint16 v[]; };", "root.v: " + WriteProgramCompiler.DataSizedArrays),
            ("struct root { uint8 g[2][2]; };", "root.g: " + WriteProgramCompiler.MultidimensionalArrays),
        ];
        foreach ((string definition, string reason) in cases)
        {
            WriteProgramOutcome outcome = new CStruct(definition).Compilation.GetRootWriteProgram("root");
            Assert.AreEqual(reason, outcome.Reason, definition);
            Assert.IsNull(outcome.Program);
        }

        Assert.AreEqual("root.v: " + WriteProgramCompiler.CustomCodecs, new CStruct("struct root { vlq v; };", compilationOptions: new CStructCompilationOptions { Codecs = [VlqCodec.Instance,], }).Compilation.GetRootWriteProgram("root").Reason);
    }

    /// <summary>Returns a root's composite.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="name">The struct's name.</param>
    /// <returns>The composite.</returns>
    private static CompiledCompositeType Composite(CStruct layout, string name) => layout.Compilation.SizeQueries.GetCompiledComposite(layout.Compilation.GetStruct(name));

    /// <summary>Compiles a root and returns the program of the struct it writes.</summary>
    /// <param name="definition">The layout.</param>
    /// <param name="root">The root struct's name.</param>
    /// <param name="aligned">Whether the layout is aligned.</param>
    /// <returns>The struct's program.</returns>
    private static WriteProgram Program(string definition, string root, bool aligned = false)
    {
        WriteProgramOutcome outcome = new CStruct(definition, aligned: aligned).Compilation.GetRootWriteProgram(root);
        Assert.IsNotNull(outcome.Program, outcome.Reason);
        return outcome.Program.Nested[0];
    }

    /// <summary>Renders a program's steps as <c>Op member</c>, separated by <c>|</c>.</summary>
    /// <param name="program">The program.</param>
    /// <returns>The rendering.</returns>
    private static string Render(WriteProgram program)
        => string.Join("|", program.Steps.Select(step => step.Op + " " + (step.Field >= 0 ? program.Fields[step.Field].Name : "-")));
}
