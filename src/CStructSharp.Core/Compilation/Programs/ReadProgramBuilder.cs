namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Syntax;

/// <summary>
///     Collects one program's steps and side tables while <see cref="ReadProgramCompiler"/> walks a struct, then freezes
///     them into a <see cref="ReadProgram"/>. Codecs, enums, nested programs and prefixes that repeat share one entry; the
///     tables a write program shares are kept by <see cref="ProgramTables"/>.
/// </summary>
/// <remarks>Used by one compilation on one thread and discarded after <see cref="Build"/>.</remarks>
internal sealed class ReadProgramBuilder
{
    private readonly List<ReadStep> steps = [];
    private readonly List<ReadProgram> nested = [];
    private readonly List<ReadPointerTarget> pointerTargets = [];
    private readonly ProgramTables tables;

    /// <summary>Starts an empty program over a struct's or root's members.</summary>
    /// <param name="table">The layout's slot table, which compiles the expressions.</param>
    /// <param name="fields">The members, indexed by the steps' <see cref="ReadStep.Field"/>.</param>
    /// <param name="shape">The layout of the value the members are stored into.</param>
    /// <param name="groupCount">The number of conditional groups among the members.</param>
    public ReadProgramBuilder(SlotTable table, CompiledField[] fields, StructShape shape, int groupCount)
    {
        this.tables = new ProgramTables(table, groupCount);
        this.Fields = fields;
        this.Shape = shape;
        this.ShapeSlots = new int[fields.Length];
        for (int index = 0; index < fields.Length; index++)
        {
            this.ShapeSlots[index] = -1;
        }
    }

    /// <summary>Gets the members.</summary>
    public CompiledField[] Fields { get; }

    /// <summary>Gets the layout of the value the members are stored into.</summary>
    public StructShape Shape { get; }

    /// <summary>Gets each member's value slot, -1 until <see cref="SetShapeSlot"/> assigns one.</summary>
    public int[] ShapeSlots { get; }

    /// <summary>Gets or sets a value indicating whether the members are placed by a runtime placement cursor (a struct with bitfields).</summary>
    public bool UsesPlacementCursor { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether a member (or a promoted member's member) defers a pointer target this
    ///     program follows after its last member.
    /// </summary>
    public bool DefersPointers { get; set; }

    /// <summary>Gets or sets a value indicating whether the members are a union's views, each read from the union's first byte.</summary>
    public bool UnionMembers { get; set; }

    /// <summary>Gets or sets the composite's conditional scope in slot terms.</summary>
    public ReadConditionalScope? Scope { get; set; }

    /// <summary>Gets the index the next step will have.</summary>
    public int Count => this.steps.Count;

    /// <summary>Appends a step.</summary>
    /// <param name="op">The operation.</param>
    /// <param name="field">The member, or -1.</param>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The step's index.</returns>
    public int Emit(ReadOpCode op, int field, int a, int b)
    {
        this.steps.Add(new ReadStep(op, field, a, b));
        return this.steps.Count - 1;
    }

    /// <summary>Appends a step built elsewhere (a placement step).</summary>
    /// <param name="step">The step.</param>
    public void Emit(ReadStep step)
    {
        this.steps.Add(step);
    }

    /// <summary>Points the <see cref="ReadOpCode.SelectArm"/> steps of a member at the step after it, where an unselected member continues.</summary>
    /// <param name="selections">The indexes of the member's selection steps.</param>
    public void PatchSkipTargets(List<int> selections)
    {
        foreach (int index in selections)
        {
            ReadStep step = this.steps[index];
            this.steps[index] = new ReadStep(step.Op, step.Field, step.A, this.steps.Count);
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
    /// <param name="codecId">The field's catalog codec id, through which the engine finds a caller's codec; -1 for none.</param>
    /// <param name="codec">The codec identity.</param>
    /// <returns>The codec's index.</returns>
    public int AddCodec(int codecId, PrimitiveCodec codec) => this.tables.AddCodec(codecId, codec);

    /// <summary>Adds a pointer target.</summary>
    /// <param name="target">The target.</param>
    /// <returns>The target's index.</returns>
    public int AddPointerTarget(ReadPointerTarget target)
    {
        this.pointerTargets.Add(target);
        return this.pointerTargets.Count - 1;
    }

    /// <summary>Adds an enum type, or finds it.</summary>
    /// <param name="enm">The enum.</param>
    /// <returns>The enum's index.</returns>
    public int AddEnum(CompiledEnumType enm) => this.tables.AddEnum(enm);

    /// <summary>Adds a nested program, or finds it.</summary>
    /// <param name="program">The program.</param>
    /// <returns>The program's index.</returns>
    public int AddNested(ReadProgram program) => ProgramTables.IndexOfReference(this.nested, program);

    /// <summary>Adds a qualified prefix, or finds an equal one.</summary>
    /// <param name="prefix">The prefix, with its final dot.</param>
    /// <returns>The prefix's index.</returns>
    public int AddPrefix(string prefix) => this.tables.AddPrefix(prefix);

    /// <summary>Adds a name's publication targets.</summary>
    /// <param name="targets">The targets.</param>
    /// <returns>The targets' index.</returns>
    public int AddQualifiedTargets(ReadProgram.QualifiedTarget[] targets) => this.tables.AddQualifiedTargets(targets);

    /// <summary>Adds the unusable value a not-a-number capture stores.</summary>
    /// <param name="reason">What the member holds, as the phrase after "is".</param>
    /// <returns>The value's index.</returns>
    public int AddUnusable(string reason) => this.tables.AddUnusable(reason);

    /// <summary>Compiles an expression against the slot table and adds it with the context its failures name.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="context">What is evaluated (<c>array length for n</c>).</param>
    /// <returns>The expression's index.</returns>
    public int AddExpression(Expr expression, string context) => this.tables.AddExpression(expression, context);

    /// <summary>
    ///     Adds the branch a member's arm needs, registering its decision the first time: the decision's index is its
    ///     slot in the composite's selected-arm array (<see cref="CompiledConditionalBranch.Slot"/>).
    /// </summary>
    /// <param name="branch">The compiled branch.</param>
    /// <returns>The branch's index.</returns>
    public int AddBranch(CompiledConditionalBranch branch) => this.tables.AddBranch(branch);

    /// <summary>Freezes the program.</summary>
    /// <param name="kind">What the program reads.</param>
    /// <param name="name">The composite's or root's name.</param>
    /// <param name="composite">The composite, or <see langword="null"/> for a root.</param>
    /// <returns>The program.</returns>
    public ReadProgram Build(ReadProgramKind kind, string name, CompiledCompositeType? composite)
    {
        return new ReadProgram(
            kind,
            name,
            composite,
            this.Shape,
            new ReadProgram.ReadProgramParts(
                this.steps.ToArray(),
                this.Fields,
                this.ShapeSlots,
                this.tables.Codecs(),
                this.tables.Enums(),
                this.tables.Expressions(),
                this.tables.Contexts(),
                this.nested.ToArray(),
                this.tables.Prefixes(),
                this.tables.QualifiedTargets(),
                this.tables.Unusables(),
                this.tables.Groups(),
                this.tables.Branches(),
                this.Scope,
                this.UsesPlacementCursor,
                this.pointerTargets.ToArray(),
                this.DefersPointers));
    }
}
