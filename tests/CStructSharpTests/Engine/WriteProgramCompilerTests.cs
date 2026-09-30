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
    ///     A member's steps follow a fixed order - its value, its count, its placement, its encoding and its
    ///     capture - and exactly those steps attribute a failure to the member; the tail padding belongs to none.
    /// </summary>
    [TestMethod]
    public void MemberSteps_FollowTheWriteOrder_AndNoteOnlyTheMember()
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

    /// <summary>
    ///     An unsized character array is one terminated string, encoded through its terminated view but placed by its
    ///     declaration, as the reader places it: a <c>wchar name[]</c> after one byte is preceded by one byte of padding.
    /// </summary>
    [TestMethod]
    public void UnsizedText_IsWrittenAsItsTerminatedView_AndPlacedAsDeclared()
    {
        WriteProgram program = Program("struct root { uint8 a; wchar< name[]; uint8 t; };", "root", aligned: true);
        int name = Array.FindIndex(program.Fields, field => field.Name == "name");
        Assert.AreNotSame(program.Fields[name], program.ValueFields[name]);
        StringAssert.Contains(Render(program), "LoadMember name|Seek name|WriteCodecValue name");
        Assert.AreEqual(1, program.Steps.Single(step => step.Op == WriteOpCode.Seek).A);
    }

    /// <summary>
    ///     Every shape of the layout language compiles: bitfields (through the runtime cursor), unions (one segment per
    ///     member), promoted unions, pointers, caller's codecs, data-sized and multidimensional arrays.
    /// </summary>
    [TestMethod]
    public void EveryShape_Compiles()
    {
        string[] definitions =
        [
            "struct root { uint8 a : 3; uint8 : 0; uint8 b : 5; };",
            "union u { uint8 a; uint16 b; }; struct root { u v; u many[2]; };",
            "struct root { uint8 t; union { uint8 a; uint16 b; }; };",
            "struct root { uint8 *p; uint8 *q[2]; };",
            "struct root { uint16 v[]; uint16 w[EOF]; };",
            "struct root { uint8 g[2][2]; char rows[2][3]; };",
        ];
        foreach (string definition in definitions)
        {
            WriteProgramOutcome outcome = new CStruct(definition).Compilation.GetRootWriteProgram("root");
            Assert.IsNull(outcome.Reason, definition);
        }

        Assert.IsTrue(new CStruct("struct root { vlq v; word4 _; };", compilationOptions: new CStructCompilationOptions { Codecs = [VlqCodec.Instance, FixedWordCodec.Instance,], }).Compilation.GetRootWriteProgram("root").IsEligible);
    }

    /// <summary>
    ///     A struct with bitfields is placed through the runtime cursor and ended by it; a union's program has one segment
    ///     per member, each written from the union's first byte and ending with a return.
    /// </summary>
    [TestMethod]
    public void BitfieldsAndUnions_HaveTheirOwnPlacement()
    {
        WriteProgram bits = Program("struct root { uint8 a : 3; uint8 : 0; uint16 b : 5; uint8 c; };", "root", aligned: true);
        Assert.IsTrue(bits.UsesPlacementCursor);
        Assert.AreEqual(
            "LoadMember a|PlaceBitfield a|WriteBitfield a|PlaceSeparator (unnamed)|LoadMember b|PlaceBitfield b|WriteBitfield b|LoadMember c|PlaceMember c|WriteNumeric c|CompletePlacement c|FinishPlaced -",
            Render(bits).Replace("PlaceSeparator |", "PlaceSeparator (unnamed)|", StringComparison.Ordinal));

        WriteProgram union = new CStruct("union u { uint8 a; uint16 w : 12; }; struct root { u v; };").Compilation.GetRootWriteProgram("root").Program!.Nested[0].Nested[0];
        Assert.AreEqual(WriteProgramKind.Union, union.Kind);
        CollectionAssert.AreEqual(new[] { 0, 4, }, union.UnionEntries);
        Assert.AreEqual("LoadRoot a|RewindToUnionStart a|WriteNumeric a|Return a|LoadRoot w|RewindToUnionStart w|OpenBitfieldUnit w|WriteBitfield w|Return w", Render(union));
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
