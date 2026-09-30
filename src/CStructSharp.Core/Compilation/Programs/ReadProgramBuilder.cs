namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Syntax;

/// <summary>
///     Collects one read program's steps and side tables while <see cref="ReadProgramCompiler"/> walks a struct, then
///     freezes them into a <see cref="ReadProgram"/>. What it shares with the write builder (members, shape, nested
///     programs and the tables in <see cref="ProgramTables"/>) is in <see cref="ProgramBuilder{TStep, TProgram}"/>; a read
///     program adds its pointer targets.
/// </summary>
/// <remarks>Used by one compilation on one thread and discarded after <see cref="Build"/>.</remarks>
internal sealed class ReadProgramBuilder : ProgramBuilder<ReadStep, ReadProgram>
{
    private readonly List<ReadPointerTarget> pointerTargets = [];

    /// <summary>Starts an empty program over a struct's or root's members.</summary>
    /// <param name="table">The layout's slot table, which compiles the expressions.</param>
    /// <param name="fields">The members, indexed by the steps' <see cref="ReadStep.Field"/>.</param>
    /// <param name="shape">The layout of the value the members are stored into.</param>
    /// <param name="groupCount">The number of conditional groups among the members.</param>
    public ReadProgramBuilder(SlotTable table, CompiledField[] fields, StructShape shape, int groupCount)
        : base(table, fields, shape, groupCount)
    {
    }

    /// <summary>
    ///     Gets or sets a value indicating whether a member (or a promoted member's member) defers a pointer target this
    ///     program follows after its last member.
    /// </summary>
    public bool DefersPointers { get; set; }

    /// <summary>Gets or sets a value indicating whether the members are a union's views, each read from the union's first byte.</summary>
    public bool UnionMembers { get; set; }

    /// <summary>Appends a step.</summary>
    /// <param name="op">The operation.</param>
    /// <param name="field">The member, or -1.</param>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The step's index.</returns>
    public int Emit(ReadOpCode op, int field, int a, int b)
    {
        this.Steps.Add(new ReadStep(op, field, a, b));
        return this.Steps.Count - 1;
    }

    /// <inheritdoc/>
    /// <remarks>A read program's arm steps belong to no member: their field is -1.</remarks>
    public override int EmitStructural(StructuralStep step, int field, int a, int b) => step switch
    {
        StructuralStep.EnterConditionalScope => this.Emit(ReadOpCode.EnterConditionalScope, field, a, b),
        StructuralStep.SelectArm => this.Emit(ReadOpCode.SelectArm, -1, a, b),
        StructuralStep.CompleteMember => this.Emit(ReadOpCode.CompleteMember, field, a, b),
        StructuralStep.PlaceMember => this.Emit(ReadOpCode.PlaceMember, field, a, b),
        StructuralStep.PlaceBitfield => this.Emit(ReadOpCode.PlaceBitfield, field, a, b),
        StructuralStep.PlaceSeparator => this.Emit(ReadOpCode.PlaceSeparator, field, a, b),
        StructuralStep.CompletePlacement => this.Emit(ReadOpCode.CompletePlacement, field, a, b),
        StructuralStep.Seek => this.Emit(ReadOpCode.Seek, field, a, b),
        StructuralStep.Align => this.Emit(ReadOpCode.Align, field, a, b),
        StructuralStep.CheckOffset => this.Emit(ReadOpCode.CheckOffset, field, a, b),
        StructuralStep.FinishPlaced => this.Emit(ReadOpCode.FinishPlaced, field, a, b),
        _ => this.Emit(ReadOpCode.FinishComposite, field, a, b),
    };

    /// <summary>Adds a pointer target.</summary>
    /// <param name="target">The target.</param>
    /// <returns>The target's index.</returns>
    public int AddPointerTarget(ReadPointerTarget target)
    {
        this.pointerTargets.Add(target);
        return this.pointerTargets.Count - 1;
    }

    /// <summary>Adds the unusable value a not-a-number capture stores.</summary>
    /// <param name="reason">What the member holds, as the phrase after "is".</param>
    /// <returns>The value's index.</returns>
    public int AddUnusable(string reason) => this.Tables.AddUnusable(reason);

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
                this.Steps.ToArray(),
                this.Fields,
                this.ShapeSlots,
                this.Tables.Codecs(),
                this.Tables.Enums(),
                this.Tables.Expressions(),
                this.Tables.Contexts(),
                this.NestedPrograms(),
                this.Tables.Prefixes(),
                this.Tables.QualifiedTargets(),
                this.Tables.Unusables(),
                this.Tables.Groups(),
                this.Tables.Branches(),
                this.Scope,
                this.UsesPlacementCursor,
                this.pointerTargets.ToArray(),
                this.DefersPointers));
    }

    /// <inheritdoc/>
    protected override ReadStep WithSkipTarget(ReadStep step, int target) => new(step.Op, step.Field, step.A, target);
}
