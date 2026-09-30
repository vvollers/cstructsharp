namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Expressions;

/// <summary>
///     A compiled read of one struct (or root): a flat array of <see cref="ReadStep"/>s and the side tables their
///     operands index. Built once by <see cref="ReadProgramCompiler"/> from the compiled layout model, so an executor
///     re-derives nothing per field: placement, count evaluation, value kind, capture and conditional selection are all
///     decided here.
/// </summary>
/// <remarks>
///     <para>
///         <b>Frames.</b> Each program runs in its own executor frame: the frame holds the composite's first byte
///         (for <see cref="ReadOpCode.Align"/>, offset assertions and the tail), its selected conditional arms (<see cref="GroupCount"/> of them, undecided at entry, so every element of
///         a struct array selects afresh), its conditional scope locals, and the count register.
///     </para>
///     <para>
///         <b>Values.</b> A member's value is stored in slot <see cref="GetShapeSlot"/> of the frame's value, whose layout is
///         <see cref="Shape"/>; a member without a slot (unnamed padding) is read and dropped.
///     </para>
///     <para>Immutable after construction and shared by every thread; the arrays must not be modified.</para>
/// </remarks>
internal sealed class ReadProgram
{
    /// <summary>Creates a program from its compiled parts.</summary>
    /// <param name="kind">What the program reads.</param>
    /// <param name="name">The composite's name, or the root's name.</param>
    /// <param name="composite">The composite read, or <see langword="null"/> for a root program.</param>
    /// <param name="shape">The layout of the value the members are stored into.</param>
    /// <param name="parts">The steps and side tables.</param>
    internal ReadProgram(ReadProgramKind kind, string name, CompiledCompositeType? composite, StructShape shape, ReadProgramParts parts)
    {
        this.Kind = kind;
        this.Name = name;
        this.Composite = composite;
        this.Shape = shape;
        this.Steps = parts.Steps;
        this.Fields = parts.Fields;
        this.ShapeSlots = parts.ShapeSlots;
        this.Codecs = parts.Codecs;
        this.Enums = parts.Enums;
        this.Expressions = parts.Expressions;
        this.ExpressionContexts = parts.ExpressionContexts;
        this.Nested = parts.Nested;
        this.Prefixes = parts.Prefixes;
        this.QualifiedTargets = parts.QualifiedTargets;
        this.Unusables = parts.Unusables;
        this.Groups = parts.Groups;
        this.Branches = parts.Branches;
        this.Scope = parts.Scope;
        this.UsesPlacementCursor = parts.UsesPlacementCursor;
        this.PointerTargets = parts.PointerTargets;
        this.DefersPointers = parts.DefersPointers;
        (this.ScalarRunLengths, this.ScalarRunBytes) = FindScalarRuns(parts.Steps, parts.Codecs);
    }

    /// <summary>Gets what the program reads.</summary>
    public ReadProgramKind Kind { get; }

    /// <summary>Gets the composite's name (empty for an anonymous one), or the name a root is stored under.</summary>
    public string Name { get; }

    /// <summary>Gets the composite the program reads, or <see langword="null"/> for a root program.</summary>
    public CompiledCompositeType? Composite { get; }

    /// <summary>
    ///     Gets the layout of the value the members are stored into: the composite's own shape, the shape of the nearest
    ///     named struct for a <see cref="ReadProgramKind.Promoted"/> program, or the one-member root shape.
    /// </summary>
    public StructShape Shape { get; }

    /// <summary>
    ///     Gets a value indicating whether a composite places this program's members (a struct, or a promoted struct inside
    ///     one): the engine takes its block paths (a <c>char[N]</c> or a fixed struct array as one span) only there, never
    ///     for a root field or a union member view.
    /// </summary>
    public bool PlacesMembers => this.Kind is ReadProgramKind.Composite or ReadProgramKind.Promoted;

    /// <summary>
    ///     Gets a value indicating whether the members are placed at run time by a <see cref="PlacementCursor"/> the frame
    ///     starts at its first byte (<see cref="ReadOpCode.PlaceMember"/> and its siblings):
    ///     a struct with bitfields, whose storage units the layout's packing rule shares. Other structs place their members
    ///     with steps decided when the program was built.
    /// </summary>
    public bool UsesPlacementCursor { get; }

    /// <summary>Gets the pointer targets <see cref="ReadOpCode.ReadPointer"/> and <see cref="ReadOpCode.ReadPointerArray"/> steps read, indexed by their <c>A</c>.</summary>
    public ReadPointerTarget[] PointerTargets { get; }

    /// <summary>
    ///     Gets a value indicating whether the program defers pointer targets, its own or a promoted member's: a struct
    ///     follows them after its last member (<see cref="ReadOpCode.FollowPendingPointers"/>), a promoted member leaves
    ///     them to the struct it is promoted into.
    /// </summary>
    public bool DefersPointers { get; }

    /// <summary>Gets a value indicating whether a failure inside a member names that member; a root program names none.</summary>
    public bool NotesMembers => this.Kind != ReadProgramKind.Root;

    /// <summary>Gets the steps in execution order.</summary>
    public ReadStep[] Steps { get; }

    /// <summary>Gets the members, indexed by <see cref="ReadStep.Field"/>: a composite's fields in declaration order, or a root's one field.</summary>
    public CompiledField[] Fields { get; }

    /// <summary>Gets the element codecs, indexed by a read step's codec operand.</summary>
    public Codec[] Codecs { get; }

    /// <summary>Gets the enum and flag types, indexed by a <see cref="ReadOpCode.ReadEnum"/> or <see cref="ReadOpCode.ReadEnumArray"/> step's <c>B</c>.</summary>
    public CompiledEnumType[] Enums { get; }

    /// <summary>Gets the slot-indexed expressions: counts, selectors and a definition root's value.</summary>
    public ProgramExpression[] Expressions { get; }

    /// <summary>Gets what each expression evaluates, for its failure message (<c>array length for n</c>, <c>conditional selector</c>).</summary>
    public string[] ExpressionContexts { get; }

    /// <summary>Gets the programs of nested structs, indexed by a struct read's <c>A</c>.</summary>
    public ReadProgram[] Nested { get; }

    /// <summary>Gets the qualified prefixes (<c>hdr.</c>) nested struct reads activate, indexed by a <see cref="ReadOpCode.ReadStruct"/> step's <c>B</c>.</summary>
    public string[] Prefixes { get; }

    /// <summary>
    ///     Gets, per <see cref="ReadOpCode.PublishQualified"/> step's <c>B</c>, the slots a bare name is published to: one
    ///     entry per prefix under which some expression spells the name (<c>hdr.</c> for <c>hdr.n</c>, <c>a.b.</c> for <c>a.b.n</c>).
    /// </summary>
    public QualifiedTarget[][] QualifiedTargets { get; }

    /// <summary>Gets the unusable values not-a-number captures store, one per capturing member, indexed by a capture's <c>B</c>.</summary>
    public UnusableVariable[] Unusables { get; }

    /// <summary>Gets the conditional groups of the composite, indexed by <see cref="ConditionalBranch.Group"/>.</summary>
    public ConditionalGroup[] Groups { get; }

    /// <summary>Gets the conditional branches <see cref="ReadOpCode.SelectArm"/> steps test, indexed by their <c>A</c>.</summary>
    public ConditionalBranch[] Branches { get; }

    /// <summary>Gets the number of selected-arm entries a frame holds: the composite's conditional group count.</summary>
    public int GroupCount => this.Groups.Length;

    /// <summary>Gets the composite's conditional variable scope in slot terms, or <see langword="null"/> when it has no conditional member.</summary>
    public ReadConditionalScope? Scope { get; }

    /// <summary>Gets the member slots, parallel to <see cref="Fields"/>.</summary>
    internal int[] ShapeSlots { get; }

    /// <summary>
    ///     Gets, per step, the number of consecutive fixed-width scalar reads (<see cref="ReadOpCode.ReadUInt8"/> to
    ///     <see cref="ReadOpCode.ReadFloat64Be"/>) that start at it: 0 for any other step. Nothing else runs between the
    ///     reads of a run, so an executor may take the run's bytes as one block when all of them are present within the
    ///     read budget, with the outcome of reading them one by one; a jump into a run starts the shorter run there.
    /// </summary>
    internal int[] ScalarRunLengths { get; }

    /// <summary>Gets, per step, the total size in bytes of the scalar run that starts at it (<see cref="ScalarRunLengths"/>).</summary>
    internal int[] ScalarRunBytes { get; }

    /// <summary>Whether a step reads one fixed-width number: every such op code lies in one contiguous range.</summary>
    /// <param name="op">The op code.</param>
    /// <returns>Whether the op code is a fixed-width scalar read.</returns>
    internal static bool IsFixedScalar(ReadOpCode op) => op is >= ReadOpCode.ReadUInt8 and <= ReadOpCode.ReadFloat64Be;

    /// <summary>Returns the slot of a member's value in <see cref="Shape"/>.</summary>
    /// <param name="field">The member's index.</param>
    /// <returns>The slot, or -1 when the member's value is dropped (unnamed padding, a promoted member, a <c>#define</c>).</returns>
    public int GetShapeSlot(int field) => this.ShapeSlots[field];

    /// <summary>Measures the scalar runs of a step array, from the last step backwards so each run extends the next one.</summary>
    /// <param name="steps">The steps.</param>
    /// <param name="codecs">The codec table the steps' codec operands index.</param>
    /// <returns>Each step's run length and run size in bytes.</returns>
    private static (int[] Lengths, int[] Bytes) FindScalarRuns(ReadStep[] steps, Codec[] codecs)
    {
        int[] lengths = new int[steps.Length];
        int[] bytes = new int[steps.Length];
        for (int index = steps.Length - 1; index >= 0; index--)
        {
            if (!IsFixedScalar(steps[index].Op))
            {
                continue;
            }

            int size = codecs[steps[index].A].Primitive.Size;
            bool extends = index + 1 < steps.Length && lengths[index + 1] > 0;
            lengths[index] = extends ? lengths[index + 1] + 1 : 1;
            bytes[index] = extends ? bytes[index + 1] + size : size;
        }

        return (lengths, bytes);
    }

    /// <summary>One element codec of a read step.</summary>
    /// <param name="CodecId">The catalog codec id whose stream reader the step uses, or -1 when the step decodes from memory only.</param>
    /// <param name="Primitive">The codec identity: kind, size and byte order.</param>
    internal readonly record struct Codec(int CodecId, PrimitiveCodec Primitive);

    /// <summary>One arm a member sits in: the decision and the arm it needs.</summary>
    /// <param name="Group">The decision's index in <see cref="Groups"/> and in the frame's selected-arm array.</param>
    /// <param name="Arm">The arm: 1 or 0 for an <c>if</c>; a switch's case index, or -1 for <c>default</c>.</param>
    internal readonly record struct ConditionalBranch(int Group, int Arm);

    /// <summary>A slot a bare name is published to under one qualified prefix.</summary>
    /// <param name="Prefix">The active prefix, including its final dot (<c>hdr.</c>).</param>
    /// <param name="Slot">The slot of <c>Prefix + name</c>.</param>
    internal readonly record struct QualifiedTarget(string Prefix, int Slot);

    /// <summary>One <c>if</c>/<c>switch</c> decision of the composite.</summary>
    /// <param name="Selector">The selector's index in <see cref="Expressions"/>.</param>
    /// <param name="Decision">The compiled decision, which maps the selector's value to an arm.</param>
    internal sealed record ConditionalGroup(int Selector, CompiledConditionalGroup Decision);

    /// <summary>The steps and side tables of a program under construction, handed to the constructor at once.</summary>
    /// <param name="Steps">The steps.</param>
    /// <param name="Fields">The members.</param>
    /// <param name="ShapeSlots">Each member's value slot, or -1.</param>
    /// <param name="Codecs">The element codecs.</param>
    /// <param name="Enums">The enum types.</param>
    /// <param name="Expressions">The expressions.</param>
    /// <param name="ExpressionContexts">Each expression's failure context.</param>
    /// <param name="Nested">The nested programs.</param>
    /// <param name="Prefixes">The qualified prefixes.</param>
    /// <param name="QualifiedTargets">The publication targets.</param>
    /// <param name="Unusables">The not-a-number values.</param>
    /// <param name="Groups">The conditional groups.</param>
    /// <param name="Branches">The conditional branches.</param>
    /// <param name="Scope">The conditional scope, or <see langword="null"/>.</param>
    /// <param name="UsesPlacementCursor">Whether the members are placed by a runtime placement cursor.</param>
    /// <param name="PointerTargets">The pointer targets.</param>
    /// <param name="DefersPointers">Whether the program defers pointer targets.</param>
    internal sealed record ReadProgramParts(
        ReadStep[] Steps,
        CompiledField[] Fields,
        int[] ShapeSlots,
        Codec[] Codecs,
        CompiledEnumType[] Enums,
        ProgramExpression[] Expressions,
        string[] ExpressionContexts,
        ReadProgram[] Nested,
        string[] Prefixes,
        QualifiedTarget[][] QualifiedTargets,
        UnusableVariable[] Unusables,
        ConditionalGroup[] Groups,
        ConditionalBranch[] Branches,
        ReadConditionalScope? Scope,
        bool UsesPlacementCursor,
        ReadPointerTarget[] PointerTargets,
        bool DefersPointers);
}
