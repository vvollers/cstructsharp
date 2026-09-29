namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     Collects one program's steps and side tables while <see cref="ReadProgramCompiler"/> walks a struct, then freezes
///     them into a <see cref="ReadProgram"/>. Codecs, enums, nested programs and prefixes that repeat share one entry.
/// </summary>
/// <remarks>Used by one compilation on one thread and discarded after <see cref="Build"/>.</remarks>
internal sealed class ReadProgramBuilder
{
    private readonly List<ReadStep> steps = [];
    private readonly List<ReadProgram.Codec> codecs = [];
    private readonly List<CompiledEnumType> enums = [];
    private readonly List<ProgramExpression> expressions = [];
    private readonly List<string> contexts = [];
    private readonly List<ReadProgram> nested = [];
    private readonly List<string> prefixes = [];
    private readonly List<ReadProgram.QualifiedTarget[]> qualifiedTargets = [];
    private readonly List<UnusableVariable> unusables = [];
    private readonly List<ReadProgram.ConditionalBranch> branches = [];
    private readonly ReadProgram.ConditionalGroup?[] groups;
    private readonly SlotTable table;

    /// <summary>Starts an empty program over a struct's or root's members.</summary>
    /// <param name="table">The layout's slot table, which compiles the expressions.</param>
    /// <param name="fields">The members, indexed by the steps' <see cref="ReadStep.Field"/>.</param>
    /// <param name="shape">The layout of the value the members are stored into.</param>
    /// <param name="groupCount">The number of conditional groups among the members.</param>
    public ReadProgramBuilder(SlotTable table, CompiledField[] fields, StructShape shape, int groupCount)
    {
        this.table = table;
        this.Fields = fields;
        this.Shape = shape;
        this.ShapeSlots = new int[fields.Length];
        for (int index = 0; index < fields.Length; index++)
        {
            this.ShapeSlots[index] = -1;
        }

        this.groups = new ReadProgram.ConditionalGroup?[groupCount];
    }

    /// <summary>Gets the members.</summary>
    public CompiledField[] Fields { get; }

    /// <summary>Gets the layout of the value the members are stored into.</summary>
    public StructShape Shape { get; }

    /// <summary>Gets each member's value slot, -1 until <see cref="SetShapeSlot"/> assigns one.</summary>
    public int[] ShapeSlots { get; }

    /// <summary>Gets or sets a value indicating whether the members are placed by a runtime placement cursor (a struct with bitfields).</summary>
    public bool UsesPlacementCursor { get; set; }

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
    /// <param name="codecId">The catalog codec id of the stream reader.</param>
    /// <param name="codec">The codec identity.</param>
    /// <returns>The codec's index.</returns>
    public int AddCodec(int codecId, PrimitiveCodec codec) => IndexOf(this.codecs, new ReadProgram.Codec(codecId, codec));

    /// <summary>Adds an enum type, or finds it.</summary>
    /// <param name="enm">The enum.</param>
    /// <returns>The enum's index.</returns>
    public int AddEnum(CompiledEnumType enm) => IndexOfReference(this.enums, enm);

    /// <summary>Adds a nested program, or finds it.</summary>
    /// <param name="program">The program.</param>
    /// <returns>The program's index.</returns>
    public int AddNested(ReadProgram program) => IndexOfReference(this.nested, program);

    /// <summary>Adds a qualified prefix, or finds an equal one.</summary>
    /// <param name="prefix">The prefix, with its final dot.</param>
    /// <returns>The prefix's index.</returns>
    public int AddPrefix(string prefix) => IndexOf(this.prefixes, prefix);

    /// <summary>Adds a name's publication targets.</summary>
    /// <param name="targets">The targets.</param>
    /// <returns>The targets' index.</returns>
    public int AddQualifiedTargets(ReadProgram.QualifiedTarget[] targets) => IndexOfReference(this.qualifiedTargets, targets);

    /// <summary>Adds the unusable value a not-a-number capture stores.</summary>
    /// <param name="reason">What the member holds, as the phrase after "is".</param>
    /// <returns>The value's index.</returns>
    public int AddUnusable(string reason)
    {
        // One value per reason: the variable is immutable and compares by its reason, so members that share a reason
        // may share an instance.
        for (int index = 0; index < this.unusables.Count; index++)
        {
            if (((NotANumberVariable)this.unusables[index]).Reason == reason)
            {
                return index;
            }
        }

        this.unusables.Add(new NotANumberVariable(reason));
        return this.unusables.Count - 1;
    }

    /// <summary>Compiles an expression against the slot table and adds it with the context its failures name.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="context">What is evaluated (<c>array length for n</c>).</param>
    /// <returns>The expression's index.</returns>
    public int AddExpression(Expr expression, string context)
    {
        this.expressions.Add(this.table.Compile(expression));
        this.contexts.Add(context);
        return this.expressions.Count - 1;
    }

    /// <summary>
    ///     Adds the branch a member's arm needs, registering its decision the first time: the decision's index is its
    ///     slot in the composite's selected-arm array (<see cref="CompiledConditionalBranch.Slot"/>).
    /// </summary>
    /// <param name="branch">The compiled branch.</param>
    /// <returns>The branch's index.</returns>
    public int AddBranch(CompiledConditionalBranch branch)
    {
        if (this.groups[branch.Slot] is null)
        {
            this.groups[branch.Slot] = new ReadProgram.ConditionalGroup(this.AddExpression(branch.Group.Selector, "conditional selector"), branch.Group);
        }

        return IndexOf(this.branches, new ReadProgram.ConditionalBranch(branch.Slot, branch.Arm));
    }

    /// <summary>Freezes the program.</summary>
    /// <param name="kind">What the program reads.</param>
    /// <param name="name">The composite's or root's name.</param>
    /// <param name="composite">The composite, or <see langword="null"/> for a root.</param>
    /// <returns>The program.</returns>
    public ReadProgram Build(ReadProgramKind kind, string name, CompiledCompositeType? composite)
    {
        var groupArray = new ReadProgram.ConditionalGroup[this.groups.Length];
        for (int index = 0; index < groupArray.Length; index++)
        {
            // Every decision is some member's arm, so each was registered by that member's selection step.
            groupArray[index] = this.groups[index]!;
        }

        return new ReadProgram(
            kind,
            name,
            composite,
            this.Shape,
            new ReadProgram.ReadProgramParts(
                this.steps.ToArray(),
                this.Fields,
                this.ShapeSlots,
                this.codecs.ToArray(),
                this.enums.ToArray(),
                this.expressions.ToArray(),
                this.contexts.ToArray(),
                this.nested.ToArray(),
                this.prefixes.ToArray(),
                this.qualifiedTargets.ToArray(),
                this.unusables.ToArray(),
                groupArray,
                this.branches.ToArray(),
                this.Scope,
                this.UsesPlacementCursor));
    }

    /// <summary>Finds an equal item or appends it.</summary>
    /// <typeparam name="T">The item type, compared with its default equality.</typeparam>
    /// <param name="items">The table.</param>
    /// <param name="item">The item.</param>
    /// <returns>The item's index.</returns>
    private static int IndexOf<T>(List<T> items, T item)
    {
        int index = items.IndexOf(item);
        if (index >= 0)
        {
            return index;
        }

        items.Add(item);
        return items.Count - 1;
    }

    /// <summary>Finds the same instance or appends it.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The table.</param>
    /// <param name="item">The item.</param>
    /// <returns>The item's index.</returns>
    private static int IndexOfReference<T>(List<T> items, T item)
        where T : class
    {
        for (int index = 0; index < items.Count; index++)
        {
            if (ReferenceEquals(items[index], item))
            {
                return index;
            }
        }

        items.Add(item);
        return items.Count - 1;
    }
}
