namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     Collects one write program's steps and side tables while <see cref="WriteProgramCompiler"/> walks a struct, then
///     freezes them into a <see cref="WriteProgram"/>. The tables a read program has too (codecs, enums, expressions,
///     prefixes, publication targets, conditional branches) are kept by the shared <see cref="ProgramTables"/>.
/// </summary>
/// <remarks>Used by one compilation on one thread and discarded after <see cref="Build"/>.</remarks>
internal sealed class WriteProgramBuilder
{
    private readonly List<WriteStep> steps = [];
    private readonly List<int> notedMembers = [];
    private readonly List<WriteProgram> nested = [];
    private readonly ProgramTables tables;

    /// <summary>Starts an empty program over a struct's or root's members.</summary>
    /// <param name="table">The layout's slot table, which compiles the expressions.</param>
    /// <param name="fields">The members, indexed by the steps' <see cref="WriteStep.Field"/>.</param>
    /// <param name="shape">The layout of the value the members are read from.</param>
    /// <param name="groupCount">The number of conditional groups among the members.</param>
    public WriteProgramBuilder(SlotTable table, CompiledField[] fields, StructShape shape, int groupCount)
    {
        this.tables = new ProgramTables(table, groupCount);
        this.Fields = fields;
        this.Shape = shape;
        this.ValueFields = (CompiledField[])fields.Clone();
        this.NotANumbers = new UnusableVariable?[fields.Length];
        this.ShapeSlots = new int[fields.Length];
        for (int index = 0; index < fields.Length; index++)
        {
            this.ShapeSlots[index] = -1;
        }
    }

    /// <summary>Gets the members.</summary>
    public CompiledField[] Fields { get; }

    /// <summary>Gets the field each member's value is encoded as; the member itself until the compiler replaces it.</summary>
    public CompiledField[] ValueFields { get; }

    /// <summary>Gets each member's not-a-number capture value, <see langword="null"/> until the compiler sets one.</summary>
    public UnusableVariable?[] NotANumbers { get; }

    /// <summary>Gets the layout of the value the members are read from.</summary>
    public StructShape Shape { get; }

    /// <summary>Gets each member's value slot, -1 until <see cref="SetShapeSlot"/> assigns one.</summary>
    public int[] ShapeSlots { get; }

    /// <summary>Gets or sets the composite's conditional scope in slot terms.</summary>
    public ReadConditionalScope? Scope { get; set; }

    /// <summary>
    ///     Gets or sets the member the steps emitted from now on attribute a failure to, or -1 for none: set from a named
    ///     member's value lookup to its capture.
    /// </summary>
    public int NotedMember { get; set; } = -1;

    /// <summary>Gets the slot table the expressions are compiled against.</summary>
    public SlotTable Table => this.tables.Table;

    /// <summary>Appends a step, attributed to <see cref="NotedMember"/>.</summary>
    /// <param name="op">The operation.</param>
    /// <param name="field">The member, or -1.</param>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The step's index.</returns>
    public int Emit(WriteOpCode op, int field, int a, int b)
    {
        this.steps.Add(new WriteStep(op, field, a, b));
        this.notedMembers.Add(this.NotedMember);
        return this.steps.Count - 1;
    }

    /// <summary>
    ///     Appends the step a <see cref="ReadPlacement"/> asked for (a read placement's <see cref="ReadOpCode.Seek"/> or
    ///     <see cref="ReadOpCode.Align"/>), as its write counterpart.
    /// </summary>
    /// <param name="step">The placement step.</param>
    public void EmitPlacement(ReadStep step)
    {
        this.Emit(step.Op == ReadOpCode.Seek ? WriteOpCode.Seek : WriteOpCode.Align, step.Field, step.A, step.B);
    }

    /// <summary>Points the <see cref="WriteOpCode.SelectArm"/> steps of a member at the step after it, where an unselected member continues.</summary>
    /// <param name="selections">The indexes of the member's selection steps.</param>
    public void PatchSkipTargets(List<int> selections)
    {
        foreach (int index in selections)
        {
            WriteStep step = this.steps[index];
            this.steps[index] = new WriteStep(step.Op, step.Field, step.A, this.steps.Count);
        }
    }

    /// <summary>Assigns a member's value slot in <see cref="Shape"/>.</summary>
    /// <param name="field">The member's index.</param>
    /// <returns>Whether the shape has a slot for the member's name.</returns>
    public bool SetShapeSlot(int field)
    {
        if (!this.Shape.TryGetIndex(this.Fields[field].Name, out int slot))
        {
            return false;
        }

        this.ShapeSlots[field] = slot;
        return true;
    }

    /// <summary>Adds a codec, or finds an equal one.</summary>
    /// <param name="codecId">The catalog codec id of the stream writer.</param>
    /// <param name="codec">The codec identity.</param>
    /// <returns>The codec's index.</returns>
    public int AddCodec(int codecId, PrimitiveCodec codec) => this.tables.AddCodec(codecId, codec);

    /// <summary>Adds an enum type, or finds it.</summary>
    /// <param name="enm">The enum.</param>
    /// <returns>The enum's index.</returns>
    public int AddEnum(CompiledEnumType enm) => this.tables.AddEnum(enm);

    /// <summary>Adds a nested program, or finds it.</summary>
    /// <param name="program">The program.</param>
    /// <returns>The program's index.</returns>
    public int AddNested(WriteProgram program) => ProgramTables.IndexOfReference(this.nested, program);

    /// <summary>Adds a qualified prefix, or finds an equal one.</summary>
    /// <param name="prefix">The prefix, with its final dot.</param>
    /// <returns>The prefix's index.</returns>
    public int AddPrefix(string prefix) => this.tables.AddPrefix(prefix);

    /// <summary>Adds a name's publication targets.</summary>
    /// <param name="targets">The targets.</param>
    /// <returns>The targets' index.</returns>
    public int AddQualifiedTargets(ReadProgram.QualifiedTarget[] targets) => this.tables.AddQualifiedTargets(targets);

    /// <summary>Compiles an expression against the slot table and adds it with the context its failures name.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="context">What is evaluated (<c>array length for n</c>).</param>
    /// <returns>The expression's index.</returns>
    public int AddExpression(Expr expression, string context) => this.tables.AddExpression(expression, context);

    /// <summary>Adds the branch a member's arm needs, registering its decision the first time.</summary>
    /// <param name="branch">The compiled branch.</param>
    /// <returns>The branch's index.</returns>
    public int AddBranch(CompiledConditionalBranch branch) => this.tables.AddBranch(branch);

    /// <summary>Freezes the program.</summary>
    /// <param name="kind">What the program writes.</param>
    /// <param name="name">The composite's or root's name.</param>
    /// <param name="composite">The composite, or <see langword="null"/> for a root.</param>
    /// <returns>The program.</returns>
    public WriteProgram Build(WriteProgramKind kind, string name, CompiledCompositeType? composite)
    {
        return new WriteProgram(
            kind,
            name,
            composite,
            this.Shape,
            new WriteProgram.WriteProgramParts(
                this.steps.ToArray(),
                this.notedMembers.ToArray(),
                this.Fields,
                this.ValueFields,
                this.ShapeSlots,
                this.NotANumbers,
                this.tables.Codecs(),
                this.tables.Enums(),
                this.tables.Expressions(),
                this.tables.Contexts(),
                this.nested.ToArray(),
                this.tables.Prefixes(),
                this.tables.QualifiedTargets(),
                this.tables.Groups(),
                this.tables.Branches(),
                this.Scope));
    }
}
