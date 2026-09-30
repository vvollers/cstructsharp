namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;

/// <summary>
///     The struct walk the read and write program compilers share: a struct's conditional scope, each member's arms, its
///     emission (the compiler's own <see cref="EmitMember"/>), the scope step after it, the placement state across members
///     (<see cref="Placement"/>, merged after a conditional member), and the struct's end with its tail padding. The steps
///     it emits are <see cref="StructuralStep"/>s, which each builder writes as its own opcode.
/// </summary>
/// <typeparam name="TBuilder">The read or write program builder.</typeparam>
/// <remarks>A compiler is used for one request on one thread.</remarks>
internal abstract class StructProgramCompiler<TBuilder>
    where TBuilder : IStructProgramBuilder
{
    /// <summary>The reason for a member whose name the value shape does not hold.</summary>
    public const string NoShapeSlot = "the value shape has no slot for the member";

    /// <summary>The reason for a member whose static offset differs from the offset the layout compiled.</summary>
    public const string PlacementMismatch = "the static placement differs from the compiled offset";

    /// <summary>Creates the walk for one request over a layout.</summary>
    /// <param name="compilation">The layout.</param>
    protected StructProgramCompiler(LayoutCompilation compilation)
    {
        this.Compilation = compilation;
        this.Extents = new MemberExtents(compilation);
    }

    /// <summary>Gets the layout the programs belong to.</summary>
    protected LayoutCompilation Compilation { get; }

    /// <summary>Gets the member extents a static placement advances by.</summary>
    protected MemberExtents Extents { get; }

    /// <summary>Formats a reason with the struct and member it concerns.</summary>
    /// <param name="location">The struct (or root) name.</param>
    /// <param name="field">The member.</param>
    /// <param name="what">What is not supported.</param>
    /// <returns>The reason, <c>struct.member: what</c>.</returns>
    internal static string Refuse(string location, CompiledField field, string what)
        => location + "." + (field.Name.Length > 0 ? field.Name : field.IsPromotedComposite ? "(anonymous)" : "(unnamed)") + ": " + what;

    /// <summary>The location a struct's reasons name: its name, or a marker for an anonymous one.</summary>
    /// <param name="composite">The composite.</param>
    /// <returns>The name.</returns>
    internal static string Locate(CompiledCompositeType composite)
        => composite.Name.Length > 0 ? composite.Name : composite.IsUnion ? "(anonymous union)" : "(anonymous struct)";

    /// <summary>
    ///     Ends a struct: through the runtime placement cursor when it placed the members, otherwise with its tail padding,
    ///     known statically or aligned at run time. A statically known end that contradicts the struct's compiled size is
    ///     refused.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="composite">The struct.</param>
    /// <param name="placement">The static placement after the last member.</param>
    /// <returns>Whether the end was emitted; <see langword="false"/> when it contradicts the compiled size.</returns>
    protected static bool EmitStructEnd(TBuilder builder, CompiledCompositeType composite, Placement placement)
    {
        int alignment = composite.Symbol.Alignment;
        if (builder.UsesPlacementCursor)
        {
            builder.EmitStructural(StructuralStep.FinishPlaced, -1, 0, alignment);
            return true;
        }

        bool knownTail = placement.TryFinish(alignment, out int padding);
        if (knownTail && placement.KnownOffset is long end && composite.Symbol.FixedSize is int size && end + padding != size)
        {
            return false;
        }

        builder.EmitStructural(StructuralStep.FinishComposite, -1, knownTail ? padding : -1, alignment);
        return true;
    }

    /// <summary>
    ///     Emits a member of a struct by its static placement - nothing, a <see cref="StructuralStep.Seek"/> over known
    ///     padding, or an <see cref="StructuralStep.Align"/> - and, when the layout's build could not check it, its
    ///     <c>@N</c> assertion, by the rule the reader, writer and address resolver share
    ///     (<see cref="Placement.PlaceMember"/>). A member is placed by its declaration, whatever view its value goes through.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="placement">The placement state.</param>
    /// <returns><see cref="PlacementMismatch"/> when a statically known start contradicts the compiled offset; otherwise <see langword="null"/>.</returns>
    protected static string? EmitStaticPlacement(TBuilder builder, int index, ref Placement placement)
    {
        bool contradicts = placement.PlaceMember(index, builder.Fields[index], out PlacementStep? step, out int? asserted);
        if (step is { } move)
        {
            builder.EmitStructural(move.Aligns ? StructuralStep.Align : StructuralStep.Seek, move.Field, move.Amount, 0);
        }

        if (contradicts)
        {
            return PlacementMismatch;
        }

        if (asserted is int offset)
        {
            builder.EmitStructural(StructuralStep.CheckOffset, index, offset, 0);
        }

        return null;
    }

    /// <summary>
    ///     Emits a struct's members in declaration order: the scope's entry step when an expression can read one of its
    ///     kept names, then per member its <c>if</c>/<c>switch</c> arms (patched to skip the member when not selected), the
    ///     member itself, and its scope step; a conditional member's placement merges with the state before it.
    /// </summary>
    /// <param name="builder">The program under construction, whose placement mode is already set.</param>
    /// <param name="composite">The struct.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="placement">The static placement after the last member.</param>
    /// <returns>A member's reason, or <see langword="null"/> when every member was emitted.</returns>
    protected string? EmitStructMembers(TBuilder builder, CompiledCompositeType composite, string location, out Placement placement)
    {
        if (composite.ConditionalScope is { } scope)
        {
            // The scope removes the kept names at entry; only the ones an expression can read matter.
            builder.Scope = new ConditionalScopeSlots(scope, builder.Table);
            if (builder.Scope.ClearedSlots.Length > 0)
            {
                builder.EmitStructural(StructuralStep.EnterConditionalScope, -1, 0, 0);
            }
        }

        placement = new Placement(this.Compilation.Aligned);
        var selections = new List<int>();
        CompiledField[] fields = builder.Fields;
        for (int index = 0; index < fields.Length; index++)
        {
            CompiledField field = fields[index];

            // An unselected member is skipped whole: no placement, no value, no scope step.
            selections.Clear();
            this.OnArmsStart(builder, index);
            foreach (CompiledConditionalBranch branch in field.ConditionalBranches)
            {
                selections.Add(builder.EmitStructural(StructuralStep.SelectArm, index, builder.AddBranch(branch), -1));
            }

            this.OnArmsEnd(builder, index);
            Placement before = placement;
            if (this.EmitMember(builder, index, location, standalone: false, ref placement) is { } reason)
            {
                return reason;
            }

            if (builder.Scope is { } mapped && mapped.HasEffect(index))
            {
                builder.EmitStructural(StructuralStep.CompleteMember, index, 0, 0);
            }

            if (field.IsConditional)
            {
                builder.PatchSkipTargets(selections);
                placement = Placement.Merge(before, placement);
            }
        }

        return null;
    }

    /// <summary>
    ///     Records where a placed member ended: the runtime cursor learns the position after a member that is not a bitfield
    ///     (a bitfield's unit was reserved when it opened), and a static placement advances past the member's size (or
    ///     restarts after a size the data decides). A standalone member records nothing.
    /// </summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="standalone">Whether no composite places the member.</param>
    /// <param name="placement">The static placement state.</param>
    protected void EmitCompletion(TBuilder builder, int index, bool standalone, ref Placement placement)
    {
        if (standalone)
        {
            return;
        }

        if (builder.UsesPlacementCursor)
        {
            if (builder.Fields[index].BitSize == 0)
            {
                builder.EmitStructural(StructuralStep.CompletePlacement, index, 0, 0);
            }
        }
        else
        {
            this.Extents.AdvancePast(ref placement, builder.Fields[index]);
        }
    }

    /// <summary>Emits one member, or returns why it cannot be compiled.</summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    /// <param name="location">The struct's name, for reasons.</param>
    /// <param name="standalone">Whether no composite places the member: a root field or a union member.</param>
    /// <param name="placement">The placement state; advanced past the member (a standalone member leaves it alone).</param>
    /// <returns>A reason, or <see langword="null"/> when the member was emitted.</returns>
    protected abstract string? EmitMember(TBuilder builder, int index, string location, bool standalone, ref Placement placement);

    /// <summary>Called before a member's arm steps; a debug read program traces a conditional member here.</summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    protected virtual void OnArmsStart(TBuilder builder, int index)
    {
    }

    /// <summary>Called after a member's arm steps, where a member that is not skipped continues.</summary>
    /// <param name="builder">The program under construction.</param>
    /// <param name="index">The member's index.</param>
    protected virtual void OnArmsEnd(TBuilder builder, int index)
    {
    }
}
