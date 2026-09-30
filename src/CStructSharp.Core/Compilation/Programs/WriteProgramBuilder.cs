namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     Collects one write program's steps and side tables while <see cref="WriteProgramCompiler"/> walks a struct, then
///     freezes them into a <see cref="WriteProgram"/>. What it shares with the read builder (members, shape, nested
///     programs and the tables in <see cref="ProgramTables"/>) is in <see cref="ProgramBuilder{TStep, TProgram}"/>; a write
///     program adds the member each step's failure names, the field each value is encoded as, and its union entries.
/// </summary>
/// <remarks>Used by one compilation on one thread and discarded after <see cref="Build"/>.</remarks>
internal sealed class WriteProgramBuilder : ProgramBuilder<WriteStep, WriteProgram>
{
    private readonly List<int> notedMembers = [];

    /// <summary>Starts an empty program over a struct's or root's members.</summary>
    /// <param name="table">The layout's slot table, which compiles the expressions.</param>
    /// <param name="fields">The members, indexed by the steps' <see cref="WriteStep.Field"/>.</param>
    /// <param name="shape">The layout of the value the members are read from.</param>
    /// <param name="groupCount">The number of conditional groups among the members.</param>
    public WriteProgramBuilder(SlotTable table, CompiledField[] fields, StructShape shape, int groupCount)
        : base(table, fields, shape, groupCount)
    {
        this.ValueFields = (CompiledField[])fields.Clone();
        this.NotANumbers = new UnusableVariable?[fields.Length];
    }

    /// <summary>Gets the field each member's value is encoded as; the member itself until the compiler replaces it.</summary>
    public CompiledField[] ValueFields { get; }

    /// <summary>Gets each member's not-a-number capture value, <see langword="null"/> until the compiler sets one.</summary>
    public UnusableVariable?[] NotANumbers { get; }

    /// <summary>Gets or sets a union program's first step per member, or <see langword="null"/> for any other program.</summary>
    public int[]? UnionEntries { get; set; }

    /// <summary>
    ///     Gets or sets the member the steps emitted from now on attribute a failure to, or -1 for none: set from a named
    ///     member's value lookup to its capture.
    /// </summary>
    public int NotedMember { get; set; } = -1;

    /// <summary>Appends a step, attributed to <see cref="NotedMember"/>.</summary>
    /// <param name="op">The operation.</param>
    /// <param name="field">The member, or -1.</param>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The step's index.</returns>
    public int Emit(WriteOpCode op, int field, int a, int b)
    {
        this.Steps.Add(new WriteStep(op, field, a, b));
        this.notedMembers.Add(this.NotedMember);
        return this.Steps.Count - 1;
    }

    /// <inheritdoc/>
    public override int EmitStructural(StructuralStep step, int field, int a, int b) => step switch
    {
        StructuralStep.EnterConditionalScope => this.Emit(WriteOpCode.EnterConditionalScope, field, a, b),
        StructuralStep.SelectArm => this.Emit(WriteOpCode.SelectArm, field, a, b),
        StructuralStep.CompleteMember => this.Emit(WriteOpCode.CompleteMember, field, a, b),
        StructuralStep.PlaceMember => this.Emit(WriteOpCode.PlaceMember, field, a, b),
        StructuralStep.PlaceBitfield => this.Emit(WriteOpCode.PlaceBitfield, field, a, b),
        StructuralStep.PlaceSeparator => this.Emit(WriteOpCode.PlaceSeparator, field, a, b),
        StructuralStep.CompletePlacement => this.Emit(WriteOpCode.CompletePlacement, field, a, b),
        StructuralStep.Seek => this.Emit(WriteOpCode.Seek, field, a, b),
        StructuralStep.Align => this.Emit(WriteOpCode.Align, field, a, b),
        StructuralStep.CheckOffset => this.Emit(WriteOpCode.CheckOffset, field, a, b),
        StructuralStep.FinishPlaced => this.Emit(WriteOpCode.FinishPlaced, field, a, b),
        _ => this.Emit(WriteOpCode.FinishComposite, field, a, b),
    };

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
                this.Steps.ToArray(),
                this.notedMembers.ToArray(),
                this.Fields,
                this.ValueFields,
                this.ShapeSlots,
                this.NotANumbers,
                this.Tables.Codecs(),
                this.Tables.Enums(),
                this.Tables.Expressions(),
                this.Tables.Contexts(),
                this.NestedPrograms(),
                this.Tables.Prefixes(),
                this.Tables.QualifiedTargets(),
                this.Tables.Groups(),
                this.Tables.Branches(),
                this.Scope,
                this.UsesPlacementCursor,
                this.UnionEntries ?? []));
    }

    /// <inheritdoc/>
    protected override WriteStep WithSkipTarget(WriteStep step, int target) => new(step.Op, step.Field, step.A, target);
}
