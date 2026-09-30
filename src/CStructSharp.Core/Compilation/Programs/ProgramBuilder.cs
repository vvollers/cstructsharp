namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Syntax;

/// <summary>
///     The part of a program builder the read and write builders share while a compiler walks a struct: the members and
///     the value shape, the steps, the nested programs, and the side tables (<see cref="ProgramTables"/>), where codecs,
///     enums, prefixes and other entries that repeat share one index.
/// </summary>
/// <typeparam name="TStep">The builder's step type (<see cref="ReadStep"/> or <see cref="WriteStep"/>).</typeparam>
/// <typeparam name="TProgram">The builder's program type, whose nested programs it lists.</typeparam>
/// <remarks>Used by one compilation on one thread and discarded after the program is built.</remarks>
internal abstract class ProgramBuilder<TStep, TProgram> : IStructProgramBuilder
    where TStep : struct
    where TProgram : class
{
    private readonly List<TProgram> nested = [];

    /// <summary>Starts an empty program over a struct's or root's members.</summary>
    /// <param name="table">The layout's slot table, which compiles the expressions.</param>
    /// <param name="fields">The members, indexed by the steps' field.</param>
    /// <param name="shape">The layout of the value the members are stored into or read from.</param>
    /// <param name="groupCount">The number of conditional groups among the members.</param>
    protected ProgramBuilder(SlotTable table, CompiledField[] fields, StructShape shape, int groupCount)
    {
        this.Tables = new ProgramTables(table, groupCount);
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

    /// <summary>Gets the layout of the value the members are stored into (a read) or read from (a write).</summary>
    public StructShape Shape { get; }

    /// <summary>Gets each member's value slot, -1 until <see cref="SetShapeSlot"/> assigns one.</summary>
    public int[] ShapeSlots { get; }

    /// <summary>Gets or sets a value indicating whether the members are placed by a runtime placement cursor (a struct with bitfields).</summary>
    public bool UsesPlacementCursor { get; set; }

    /// <summary>Gets or sets the composite's conditional scope in slot terms.</summary>
    public ConditionalScopeSlots? Scope { get; set; }

    /// <summary>Gets the index the next step will have.</summary>
    public int Count => this.Steps.Count;

    /// <summary>Gets the slot table the expressions are compiled against.</summary>
    public SlotTable Table => this.Tables.Table;

    /// <summary>Gets the steps emitted so far.</summary>
    protected List<TStep> Steps { get; } = [];

    /// <summary>Gets the side tables the program shares with the other builder kind.</summary>
    protected ProgramTables Tables { get; }

    /// <inheritdoc/>
    public abstract int EmitStructural(StructuralStep step, int field, int a, int b);

    /// <summary>Appends the move a <see cref="Placement"/> asked for, as the builder's own <c>Seek</c> or <c>Align</c> step.</summary>
    /// <param name="step">The placement step.</param>
    public void EmitPlacement(PlacementStep step)
    {
        this.EmitStructural(step.Aligns ? StructuralStep.Align : StructuralStep.Seek, step.Field, step.Amount, 0);
    }

    /// <inheritdoc/>
    public void PatchSkipTargets(List<int> selections)
    {
        foreach (int index in selections)
        {
            this.Steps[index] = this.WithSkipTarget(this.Steps[index], this.Steps.Count);
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
    /// <param name="codecId">The field's catalog codec id: the engine finds a caller's codec, or a writer delegate, through it; -1 for none.</param>
    /// <param name="codec">The codec identity.</param>
    /// <returns>The codec's index.</returns>
    public int AddCodec(int codecId, PrimitiveCodec codec) => this.Tables.AddCodec(codecId, codec);

    /// <summary>Adds an enum type, or finds it.</summary>
    /// <param name="enm">The enum.</param>
    /// <returns>The enum's index.</returns>
    public int AddEnum(CompiledEnumType enm) => this.Tables.AddEnum(enm);

    /// <summary>Adds a nested program, or finds it.</summary>
    /// <param name="program">The program.</param>
    /// <returns>The program's index.</returns>
    public int AddNested(TProgram program) => ProgramTables.IndexOfReference(this.nested, program);

    /// <summary>Adds a qualified prefix, or finds an equal one.</summary>
    /// <param name="prefix">The prefix, with its final dot.</param>
    /// <returns>The prefix's index.</returns>
    public int AddPrefix(string prefix) => this.Tables.AddPrefix(prefix);

    /// <summary>Adds a name's publication targets.</summary>
    /// <param name="targets">The targets.</param>
    /// <returns>The targets' index.</returns>
    public int AddQualifiedTargets(QualifiedTarget[] targets) => this.Tables.AddQualifiedTargets(targets);

    /// <summary>Compiles an expression against the slot table and adds it with the context its failures name.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="context">What is evaluated (<c>array length for n</c>).</param>
    /// <returns>The expression's index.</returns>
    public int AddExpression(Expr expression, string context) => this.Tables.AddExpression(expression, context);

    /// <summary>
    ///     Adds the branch a member's arm needs, registering its decision the first time: the decision's index is its
    ///     slot in the composite's selected-arm array (<see cref="CompiledConditionalBranch.Slot"/>).
    /// </summary>
    /// <param name="branch">The compiled branch.</param>
    /// <returns>The branch's index.</returns>
    public int AddBranch(CompiledConditionalBranch branch) => this.Tables.AddBranch(branch);

    /// <summary>Gets the nested programs, in index order.</summary>
    /// <returns>A new array.</returns>
    protected TProgram[] NestedPrograms() => this.nested.ToArray();

    /// <summary>A copy of an arm step whose skip target is <paramref name="target"/>.</summary>
    /// <param name="step">The arm step.</param>
    /// <param name="target">The index of the step an unselected member continues at.</param>
    /// <returns>The patched step.</returns>
    protected abstract TStep WithSkipTarget(TStep step, int target);
}
