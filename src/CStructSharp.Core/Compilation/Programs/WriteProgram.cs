namespace CStructSharp.Compilation.Programs;

using CStructSharp.Expressions;

/// <summary>
///     A compiled write of one struct (or root): a flat array of <see cref="WriteStep"/>s and the side tables their
///     operands index. Built once by <see cref="WriteProgramCompiler"/> from the compiled layout model, in the order each
///     decision is taken - value lookup, element count, placement, encoding, capture - so an
///     executor re-derives nothing per field.
/// </summary>
/// <remarks>
///     <para>
///         <b>Frames.</b> Each program runs in its own executor frame: the frame holds the composite's first byte
///         (for alignment, offset assertions and the tail), its selected conditional arms (undecided at entry, so every element of a struct array selects afresh), its conditional-scope
///         locals and the value, count and enum registers.
///     </para>
///     <para>
///         <b>Values.</b> A member's value is read from slot <see cref="GetShapeSlot"/> of a <c>StructValue</c> whose layout
///         is <see cref="Shape"/> (the value a parse produces), and by name from any other data.
///     </para>
///     <para>Immutable after construction and shared by every thread; the arrays must not be modified.</para>
/// </remarks>
internal sealed class WriteProgram
{
    /// <summary>Creates a program from its compiled parts.</summary>
    /// <param name="kind">What the program writes.</param>
    /// <param name="name">The composite's name, or the root's name.</param>
    /// <param name="composite">The composite written, or <see langword="null"/> for a root program.</param>
    /// <param name="shape">The layout of the value the members are read from.</param>
    /// <param name="parts">The steps and side tables.</param>
    internal WriteProgram(WriteProgramKind kind, string name, CompiledCompositeType? composite, StructShape shape, WriteProgramParts parts)
    {
        this.Kind = kind;
        this.Name = name;
        this.Composite = composite;
        this.Shape = shape;
        this.Steps = parts.Steps;
        this.NotedMembers = parts.NotedMembers;
        this.Fields = parts.Fields;
        this.ValueFields = parts.ValueFields;
        this.ShapeSlots = parts.ShapeSlots;
        this.NotANumbers = parts.NotANumbers;
        this.Codecs = parts.Codecs;
        this.Enums = parts.Enums;
        this.Expressions = parts.Expressions;
        this.ExpressionContexts = parts.ExpressionContexts;
        this.Nested = parts.Nested;
        this.Prefixes = parts.Prefixes;
        this.QualifiedTargets = parts.QualifiedTargets;
        this.Groups = parts.Groups;
        this.Branches = parts.Branches;
        this.Scope = parts.Scope;
        this.UsesPlacementCursor = parts.UsesPlacementCursor;
        this.UnionEntries = parts.UnionEntries;
    }

    /// <summary>Gets what the program writes.</summary>
    public WriteProgramKind Kind { get; }

    /// <summary>Gets the composite's name (empty for an anonymous one), or the root's name.</summary>
    public string Name { get; }

    /// <summary>Gets the composite the program writes, or <see langword="null"/> for a root program.</summary>
    public CompiledCompositeType? Composite { get; }

    /// <summary>
    ///     Gets the layout of the value the members are read from: the composite's own shape, the shape of the nearest named
    ///     struct for a <see cref="WriteProgramKind.Promoted"/> program, or the one-member root shape.
    /// </summary>
    public StructShape Shape { get; }

    /// <summary>Gets the steps in execution order.</summary>
    public WriteStep[] Steps { get; }

    /// <summary>
    ///     Gets, per step, the member a failure of the step is attributed to (<c>CStructException.NoteMember</c>), or -1:
    ///     the steps from a named member's value lookup to its capture. Every other step belongs to no member.
    /// </summary>
    public int[] NotedMembers { get; }

    /// <summary>Gets the members, indexed by <see cref="WriteStep.Field"/>: a composite's fields in declaration order, or a root's one field.</summary>
    public CompiledField[] Fields { get; }

    /// <summary>
    ///     Gets, parallel to <see cref="Fields"/>, the field each member's value is encoded as: the member itself, or for an
    ///     unsized character array (<c>char name[]</c>) its terminated-text view, which also names the value in a conversion
    ///     failure.
    /// </summary>
    public CompiledField[] ValueFields { get; }

    /// <summary>Gets, parallel to <see cref="Fields"/>, the not-a-number value a capture of the member stores, or <see langword="null"/> for an integer member.</summary>
    public UnusableVariable?[] NotANumbers { get; }

    /// <summary>Gets the element codecs, indexed by a write step's codec operand.</summary>
    public ProgramCodec[] Codecs { get; }

    /// <summary>Gets the enum and flag types, indexed by an enum write step's <c>B</c>.</summary>
    public CompiledEnumType[] Enums { get; }

    /// <summary>Gets the slot-indexed expressions: counts, selectors and a definition root's value.</summary>
    public ProgramExpression[] Expressions { get; }

    /// <summary>Gets what each expression evaluates, for its failure message.</summary>
    public string[] ExpressionContexts { get; }

    /// <summary>Gets the programs of nested structs, indexed by a struct write's <c>A</c>.</summary>
    public WriteProgram[] Nested { get; }

    /// <summary>Gets the qualified prefixes (<c>hdr.</c>) nested struct writes activate, indexed by a <see cref="WriteOpCode.WriteStruct"/> step's <c>B</c>.</summary>
    public string[] Prefixes { get; }

    /// <summary>Gets, per capture step's <c>B</c>, the slots a bare name is published to under each qualified prefix.</summary>
    public QualifiedTarget[][] QualifiedTargets { get; }

    /// <summary>Gets the conditional groups of the composite, indexed by <see cref="ProgramBranch.Group"/>.</summary>
    public ReadProgram.ConditionalGroup[] Groups { get; }

    /// <summary>Gets the conditional branches <see cref="WriteOpCode.SelectArm"/> steps test, indexed by their <c>A</c>.</summary>
    public ProgramBranch[] Branches { get; }

    /// <summary>Gets the number of selected-arm entries a frame holds: the composite's conditional group count.</summary>
    public int GroupCount => this.Groups.Length;

    /// <summary>Gets the composite's conditional variable scope in slot terms, or <see langword="null"/> when it has no conditional member.</summary>
    public ConditionalScopeSlots? Scope { get; }

    /// <summary>
    ///     Gets a value indicating whether the members are placed at run time by a <see cref="PlacementCursor"/> the frame
    ///     starts at its first byte: a struct with bitfields, whose storage units the
    ///     layout's packing rule shares. Other structs place their members with steps decided when the program was built.
    /// </summary>
    public bool UsesPlacementCursor { get; }

    /// <summary>
    ///     Gets, for a <see cref="WriteProgramKind.Union"/> program, the index of each member's first step, parallel to
    ///     <see cref="Fields"/>; each segment ends with <see cref="WriteOpCode.Return"/>. Empty for every other program.
    /// </summary>
    public int[] UnionEntries { get; }

    /// <summary>Gets the member slots, parallel to <see cref="Fields"/>.</summary>
    internal int[] ShapeSlots { get; }

    /// <summary>Returns the slot of a member's value in <see cref="Shape"/>.</summary>
    /// <param name="field">The member's index.</param>
    /// <returns>The slot, or -1 for a member without one (unnamed padding, a promoted member).</returns>
    public int GetShapeSlot(int field) => this.ShapeSlots[field];

    /// <summary>The steps and side tables of a program under construction, handed to the constructor at once.</summary>
    /// <param name="Steps">The steps.</param>
    /// <param name="NotedMembers">The member each step's failure is attributed to, or -1.</param>
    /// <param name="Fields">The members.</param>
    /// <param name="ValueFields">The field each member's value is encoded as.</param>
    /// <param name="ShapeSlots">Each member's value slot, or -1.</param>
    /// <param name="NotANumbers">Each member's not-a-number capture value, or <see langword="null"/>.</param>
    /// <param name="Codecs">The element codecs.</param>
    /// <param name="Enums">The enum types.</param>
    /// <param name="Expressions">The expressions.</param>
    /// <param name="ExpressionContexts">Each expression's failure context.</param>
    /// <param name="Nested">The nested programs.</param>
    /// <param name="Prefixes">The qualified prefixes.</param>
    /// <param name="QualifiedTargets">The publication targets.</param>
    /// <param name="Groups">The conditional groups.</param>
    /// <param name="Branches">The conditional branches.</param>
    /// <param name="Scope">The conditional scope, or <see langword="null"/>.</param>
    /// <param name="UsesPlacementCursor">Whether the members are placed by a runtime placement cursor.</param>
    /// <param name="UnionEntries">A union program's first step per member, or an empty array.</param>
    internal sealed record WriteProgramParts(
        WriteStep[] Steps,
        int[] NotedMembers,
        CompiledField[] Fields,
        CompiledField[] ValueFields,
        int[] ShapeSlots,
        UnusableVariable?[] NotANumbers,
        ProgramCodec[] Codecs,
        CompiledEnumType[] Enums,
        ProgramExpression[] Expressions,
        string[] ExpressionContexts,
        WriteProgram[] Nested,
        string[] Prefixes,
        QualifiedTarget[][] QualifiedTargets,
        ReadProgram.ConditionalGroup[] Groups,
        ProgramBranch[] Branches,
        ConditionalScopeSlots? Scope,
        bool UsesPlacementCursor,
        int[] UnionEntries);
}
