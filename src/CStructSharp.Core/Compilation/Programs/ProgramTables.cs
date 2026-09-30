namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     The side tables a compiled read or write program's steps index, collected while a compiler walks one struct:
///     element codecs, enum types, slot-indexed expressions with their failure contexts, qualified prefixes and
///     publication targets, not-a-number values, and conditional branches with the decisions they belong to. Entries
///     that repeat share one index. <see cref="ReadProgramBuilder"/> and <see cref="WriteProgramBuilder"/> each own one,
///     so both kinds of program describe these parts identically.
/// </summary>
/// <remarks>Used by one compilation on one thread and discarded once the program is built.</remarks>
internal sealed class ProgramTables
{
    private readonly List<ReadProgram.Codec> codecs = [];
    private readonly List<CompiledEnumType> enums = [];
    private readonly List<ProgramExpression> expressions = [];
    private readonly List<string> contexts = [];
    private readonly List<string> prefixes = [];
    private readonly List<ReadProgram.QualifiedTarget[]> qualifiedTargets = [];
    private readonly List<UnusableVariable> unusables = [];
    private readonly List<ReadProgram.ConditionalBranch> branches = [];
    private readonly ReadProgram.ConditionalGroup?[] groups;
    private readonly SlotTable table;

    /// <summary>Starts empty tables for a struct with <paramref name="groupCount"/> conditional decisions.</summary>
    /// <param name="table">The layout's slot table, which compiles the expressions.</param>
    /// <param name="groupCount">The number of conditional groups among the struct's members.</param>
    public ProgramTables(SlotTable table, int groupCount)
    {
        this.table = table;
        this.groups = new ReadProgram.ConditionalGroup?[groupCount];
    }

    /// <summary>Gets the layout's slot table.</summary>
    public SlotTable Table => this.table;

    /// <summary>Adds a codec, or finds an equal one.</summary>
    /// <param name="codecId">The field's catalog codec id, which names a caller's codec or a writer delegate at run time; -1 for none.</param>
    /// <param name="codec">The codec identity.</param>
    /// <returns>The codec's index.</returns>
    public int AddCodec(int codecId, PrimitiveCodec codec) => IndexOf(this.codecs, new ReadProgram.Codec(codecId, codec));

    /// <summary>Adds an enum type, or finds it.</summary>
    /// <param name="enm">The enum.</param>
    /// <returns>The enum's index.</returns>
    public int AddEnum(CompiledEnumType enm) => IndexOfReference(this.enums, enm);

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

    /// <summary>Returns the element codecs collected so far.</summary>
    /// <returns>A new array.</returns>
    public ReadProgram.Codec[] Codecs() => this.codecs.ToArray();

    /// <summary>Returns the enum types collected so far.</summary>
    /// <returns>A new array.</returns>
    public CompiledEnumType[] Enums() => this.enums.ToArray();

    /// <summary>Returns the expressions collected so far.</summary>
    /// <returns>A new array.</returns>
    public ProgramExpression[] Expressions() => this.expressions.ToArray();

    /// <summary>Returns each expression's failure context, parallel to <see cref="Expressions"/>.</summary>
    /// <returns>A new array.</returns>
    public string[] Contexts() => this.contexts.ToArray();

    /// <summary>Returns the qualified prefixes collected so far.</summary>
    /// <returns>A new array.</returns>
    public string[] Prefixes() => this.prefixes.ToArray();

    /// <summary>Returns the publication targets collected so far.</summary>
    /// <returns>A new array.</returns>
    public ReadProgram.QualifiedTarget[][] QualifiedTargets() => this.qualifiedTargets.ToArray();

    /// <summary>Returns the not-a-number values collected so far.</summary>
    /// <returns>A new array.</returns>
    public UnusableVariable[] Unusables() => this.unusables.ToArray();

    /// <summary>Returns the conditional branches collected so far.</summary>
    /// <returns>A new array.</returns>
    public ReadProgram.ConditionalBranch[] Branches() => this.branches.ToArray();

    /// <summary>Returns the composite's conditional decisions, indexed by their selected-arm slot.</summary>
    /// <returns>A new array.</returns>
    public ReadProgram.ConditionalGroup[] Groups()
    {
        var groupArray = new ReadProgram.ConditionalGroup[this.groups.Length];
        for (int index = 0; index < groupArray.Length; index++)
        {
            // Every decision is some member's arm, so each was registered by that member's selection step.
            groupArray[index] = this.groups[index]!;
        }

        return groupArray;
    }

    /// <summary>Finds an equal item or appends it.</summary>
    /// <typeparam name="T">The item type, compared with its default equality.</typeparam>
    /// <param name="items">The table.</param>
    /// <param name="item">The item.</param>
    /// <returns>The item's index.</returns>
    internal static int IndexOf<T>(List<T> items, T item)
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
    internal static int IndexOfReference<T>(List<T> items, T item)
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
