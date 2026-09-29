namespace CStructSharp.Engine;

using System.Threading;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;

/// <summary>
///     The state one compiled-engine write operation carries through its frames, beside the destination: the
///     layout-variable slots, the options the executor checks itself, the nesting depth, the active qualified prefix and
///     the frames' conditional stacks. It holds what the interpreter's <c>CStructElementWriterState</c> holds for the same
///     write, minus the stream, which is the destination.
/// </summary>
/// <remarks>
///     A mutable struct owned by one operation and passed by reference to every frame, so the depth and prefix changes a
///     nested struct makes are the caller's. The slots are borrowed: the operation that created them disposes them.
/// </remarks>
internal struct WriteEngineState
{
    private FrameArena arena;

    /// <summary>Creates the state of one operation.</summary>
    /// <param name="layout">The layout being written, which owns the codecs and text encodings the steps use.</param>
    /// <param name="slots">The operation's initialized variable slots; borrowed, not disposed here.</param>
    /// <param name="options">The operation's snapshotted and validated options.</param>
    public WriteEngineState(CStruct layout, VariableSlots slots, WriteOptions options)
    {
        this.Layout = layout;
        this.Slots = slots;
        this.MaxArrayElements = options.MaxArrayElements;
        this.MaxNestingDepth = options.MaxNestingDepth;
        this.RejectUnknownMembers = options.UnknownMembers == UnknownMemberPolicy.Reject;
        this.GeneralPathOnly = options.ExecutionPath == ExecutionPath.GeneralOnly;
        this.CancellationToken = options.CancellationToken;
        this.StructureDepth = 0;
        this.QualifiedPrefix = null;
        this.arena = default;
    }

    /// <summary>Gets the layout being written.</summary>
    public CStruct Layout { get; }

    /// <summary>Gets the operation's layout variables as slots.</summary>
    public VariableSlots Slots { get; }

    /// <summary>Gets the largest element count one array may have.</summary>
    public int MaxArrayElements { get; }

    /// <summary>Gets the largest number of nested struct levels the write may enter.</summary>
    public int MaxNestingDepth { get; }

    /// <summary>Gets whether a member the composite does not declare fails the write (<see cref="UnknownMemberPolicy.Reject"/>).</summary>
    public bool RejectUnknownMembers { get; }

    /// <summary>
    ///     Gets whether the write must avoid the static write plans and block writes (<see cref="ExecutionPath.GeneralOnly"/>),
    ///     as the interpreter does under the same option.
    /// </summary>
    public bool GeneralPathOnly { get; }

    /// <summary>Gets the operation's cancellation token, observed where the interpreter observes it.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Gets or sets the number of struct levels entered on the active path.</summary>
    public int StructureDepth { get; set; }

    /// <summary>
    ///     Gets or sets the dotted prefix (<c>hdr.</c>) under which captures are also published while a nested struct that
    ///     an expression names through its path is written; <see langword="null"/> otherwise.
    /// </summary>
    public string? QualifiedPrefix { get; set; }

    /// <summary>Takes a frame's selected arms, every group undecided.</summary>
    /// <param name="count">The frame's conditional group count, positive.</param>
    /// <returns>The base of the frame's arms.</returns>
    public int TakeArms(int count) => this.arena.TakeArms(count);

    /// <summary>Gives back the arms a completed frame took.</summary>
    /// <param name="start">The base <see cref="TakeArms"/> returned.</param>
    public void ReleaseArms(int start) => this.arena.ReleaseArms(start);

    /// <summary>Takes a frame's conditional-scope locals, every one undefined.</summary>
    /// <param name="count">The scope's local count.</param>
    /// <returns>The base of the frame's locals.</returns>
    public int TakeLocals(int count) => this.arena.TakeLocals(count);

    /// <summary>Gives back the locals a completed frame took.</summary>
    /// <param name="start">The base <see cref="TakeLocals"/> returned.</param>
    public void ReleaseLocals(int start) => this.arena.ReleaseLocals(start);

    /// <summary>Selects a branch's arm in a frame by the shared rule (<see cref="FrameArena.SelectedArm"/>), in the write domain.</summary>
    /// <param name="program">The program.</param>
    /// <param name="branch">The branch being tested.</param>
    /// <param name="armBase">The base of the frame's selected arms.</param>
    /// <returns>The selected arm of the branch's group.</returns>
    public int SelectedArm(WriteProgram program, ReadProgram.ConditionalBranch branch, int armBase)
        => FrameArena.SelectedArm(ref this.arena, this.Slots, program.Groups, program.Expressions, program.ExpressionContexts, branch, armBase, ExpressionFailureDomain.Write);

    /// <summary>Completes a member of a conditional composite by the shared rule (<see cref="FrameArena.CompleteMember"/>).</summary>
    /// <param name="scope">The composite's scope in slot terms.</param>
    /// <param name="member">The member's index.</param>
    /// <param name="localBase">The base of the frame's saved values.</param>
    public void CompleteMember(ReadConditionalScope scope, int member, int localBase)
        => FrameArena.CompleteMember(ref this.arena, this.Slots, scope, member, localBase);

    /// <summary>Claims one struct level as the interpreter's writer does, failing past the nesting limit.</summary>
    /// <exception cref="CStructWriteLimitException">The level would exceed <see cref="MaxNestingDepth"/>.</exception>
    public void EnterStructure()
    {
        if (this.StructureDepth >= this.MaxNestingDepth)
        {
            throw new CStructWriteLimitException(WriteFailures.NestingLimit);
        }

        this.StructureDepth++;
    }

    /// <summary>
    ///     While a qualified prefix is active, publishes a just-captured value under every target the prefix covers
    ///     (<see cref="QualifiedPublication.Covers"/>), as the interpreter copies (or removes) the qualified names.
    /// </summary>
    /// <param name="targets">The bare name's publication targets.</param>
    /// <param name="value">The captured value; <see cref="SlotValue.Undefined"/> removes the qualified name.</param>
    public readonly void PublishQualified(ReadProgram.QualifiedTarget[] targets, SlotValue value)
    {
        if (this.QualifiedPrefix is not { } prefix)
        {
            return;
        }

        foreach (ReadProgram.QualifiedTarget target in targets)
        {
            if (QualifiedPublication.Covers(prefix, target.Prefix))
            {
                this.Slots.Set(target.Slot, value);
            }
        }
    }

    /// <summary>Returns the frame stacks to the thread's spares at the end of the operation.</summary>
    public void Release() => this.arena.Release();
}
