namespace CStructSharp.Compilation.Programs;

/// <summary>
///     A struct or union as the path resolver walks it: its members in declaration order with what the resolver needs of
///     each (<see cref="TargetMember"/>), and the conditional decisions and scope of the composite in slot terms. Built once
///     per composite by <see cref="TargetProgramCache"/>, so a resolution evaluates slot programs instead of expressions over
///     a name dictionary.
/// </summary>
/// <remarks>
///     <para>
///         The resolver locates a path's target by walking the members before it: it places each member, reads only the
///         values an expression may need (captures) and measures each member's end, reading only what the size depends
///         on. The walk is the resolver's own, not a read program's: it reads less than a parse (a fixed-size member is
///         skipped arithmetically) and in its own order (a member is captured before it is measured).
///     </para>
///     <para>Immutable after construction and shared by every thread; the arrays must not be modified.</para>
/// </remarks>
internal sealed class TargetProgram
{
    /// <summary>Creates a program from its compiled parts.</summary>
    /// <param name="composite">The struct or union walked.</param>
    /// <param name="members">The members, in declaration order.</param>
    /// <param name="tables">The side tables that hold the conditional decisions and their selectors.</param>
    /// <param name="scope">The conditional scope in slot terms, or <see langword="null"/> when no member is conditional.</param>
    internal TargetProgram(CompiledCompositeType composite, TargetMember[] members, ProgramTables tables, ConditionalScopeSlots? scope)
    {
        this.Composite = composite;
        this.Members = members;
        this.Groups = tables.Groups();
        this.Branches = tables.Branches();
        this.Expressions = tables.Expressions();
        this.ExpressionContexts = tables.Contexts();
        this.Scope = scope;
    }

    /// <summary>Gets the struct or union walked.</summary>
    public CompiledCompositeType Composite { get; }

    /// <summary>Gets the members, parallel to the composite's fields.</summary>
    public TargetMember[] Members { get; }

    /// <summary>Gets the conditional decisions, indexed by <see cref="ProgramBranch.Group"/> (a decision's selected-arm slot).</summary>
    public ReadProgram.ConditionalGroup[] Groups { get; }

    /// <summary>Gets the conditional branches the members' <see cref="TargetMember.Branches"/> index.</summary>
    public ProgramBranch[] Branches { get; }

    /// <summary>Gets the slot programs of the decisions' selectors.</summary>
    public ProgramExpression[] Expressions { get; }

    /// <summary>Gets each selector's failure context (<c>conditional selector</c>), parallel to <see cref="Expressions"/>.</summary>
    public string[] ExpressionContexts { get; }

    /// <summary>Gets the composite's conditional variable scope in slot terms, or <see langword="null"/> when it has no conditional member.</summary>
    public ConditionalScopeSlots? Scope { get; }

    /// <summary>Gets the number of selected-arm entries a walk of the composite keeps.</summary>
    public int GroupCount => this.Groups.Length;
}
