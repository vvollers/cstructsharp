namespace CStructSharp.Engine;

using System;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;

/// <summary>
///     The state one compiled-engine read operation carries through its frames, beside the cursor: the layout-variable
///     slots, the limits the executor checks itself, the nesting depth and the active qualified prefix; the stream is the
///     cursor. A debug parse adds its recorder (<see cref="Debug"/>).
/// </summary>
/// <remarks>
///     <para>
///         A mutable struct owned by one operation and passed by reference to every frame, so the depth and prefix
///         changes a nested struct makes are the caller's. The slots are borrowed: the operation that created them
///         disposes them.
///     </para>
///     <para>
///         <b>Frame arena.</b> Each frame's conditional-scope locals live in the locals stack of the state's
///         <see cref="FrameArena"/> (the writer's state has one too) rather than in arrays of their own: a frame takes a
///         range on entry and gives it back when it completes, so frames nest like the calls that run them. A failed
///         operation abandons its ranges; <see cref="Release"/> returns the stack, cleared, to the thread's spare when the
///         operation ends. A frame's selected conditional arms are a few integers it keeps on its own call stack.
///     </para>
/// </remarks>
internal struct ReadEngineState
{
    private FrameArena arena;
    private int unionSlots;
    private PointerTraversal? pointers;

    /// <summary>Creates the state of one operation.</summary>
    /// <param name="layout">The layout being read, which owns the codecs and text encodings the steps use.</param>
    /// <param name="slots">The operation's initialized variable slots; borrowed, not disposed here.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder of a debug parse, which runs the layout's debug programs; <see langword="null"/> for an ordinary read.</param>
    public ReadEngineState(CStruct layout, VariableSlots slots, in ReadOperationSettings options, DebugRecorder? debug)
    {
        this.Layout = layout;
        this.Slots = slots;
        this.Debug = debug;
        this.MaxArrayElements = options.MaxArrayElements;
        this.MaxNestingDepth = options.MaxNestingDepth;
        this.TrimFixedText = options.TrimFixedText;

        // A debug parse takes no static read plan: every value is read, and recorded, alone.
        this.GeneralPathOnly = options.ExecutionPath == ExecutionPath.GeneralOnly || debug is not null;
        this.DereferencePointers = options.DereferencePointers;
        this.AddressingMode = options.AddressingMode;
        this.PointerOrigin = options.Origin;
        this.MaxPointerDepth = options.MaxPointerDepth;
        this.MaxPointerTargetBytes = options.MaxPointerTargetBytes;
        this.PointerDepth = 0;
        this.SuppressPointers = false;
        this.pointers = null;
        this.StructureDepth = 0;
        this.QualifiedPrefix = null;
        this.SeededBitOffset = 0;
        this.SeededUnitSize = 0;
        this.arena = default;
        this.unionSlots = -1;
    }

    /// <summary>Gets the layout being read.</summary>
    public CStruct Layout { get; }

    /// <summary>Gets the operation's layout variables as slots.</summary>
    public VariableSlots Slots { get; }

    /// <summary>Gets the recorder of a debug parse, or <see langword="null"/> for an ordinary read, which never consults it.</summary>
    public DebugRecorder? Debug { get; }

    /// <summary>Gets the largest element count one array may declare.</summary>
    public int MaxArrayElements { get; }

    /// <summary>Gets the largest number of nested struct levels the read may enter.</summary>
    public int MaxNestingDepth { get; }

    /// <summary>Gets whether fixed-capacity text drops its trailing NUL padding (<see cref="ReadOptions.TrimFixedText"/>).</summary>
    public bool TrimFixedText { get; }

    /// <summary>
    ///     Gets whether the read must avoid the static read plans and block reads
    ///     (<see cref="ExecutionPath.GeneralOnly"/>, and every debug parse).
    /// </summary>
    public bool GeneralPathOnly { get; }

    /// <summary>Gets a value indicating whether pointer targets are followed (<see cref="ReadOptions.DereferencePointers"/>).</summary>
    public bool DereferencePointers { get; }

    /// <summary>Gets whether stored addresses are input positions or offsets from <see cref="PointerOrigin"/>.</summary>
    public PointerAddressingMode AddressingMode { get; }

    /// <summary>Gets the position relative addresses count from.</summary>
    public long PointerOrigin { get; }

    /// <summary>Gets the largest number of pointer levels the read may follow on one path.</summary>
    public int MaxPointerDepth { get; }

    /// <summary>Gets the largest fixed size a pointer target may have, or <see langword="null"/> for no limit.</summary>
    public long? MaxPointerTargetBytes { get; }

    /// <summary>Gets or sets the number of pointer levels followed on the active path.</summary>
    public int PointerDepth { get; set; }

    /// <summary>Gets or sets a value indicating whether following is suppressed: while a union's views are read, a pointer keeps only its address.</summary>
    public bool SuppressPointers { get; set; }

    /// <summary>
    ///     Gets or sets the bit offset, within its storage unit, of a bitfield a path selected: the offset its struct's
    ///     placement gave it, which a selection program's <see cref="ReadOpCode.OpenSeededBitfieldUnit"/> step reads.
    /// </summary>
    public int SeededBitOffset { get; set; }

    /// <summary>Gets or sets the size in bytes of the storage unit its struct placed a selected bitfield in (<see cref="SeededBitOffset"/>).</summary>
    public int SeededUnitSize { get; set; }

    /// <summary>Gets the number of deferred pointers queued, none before the operation's first pointer.</summary>
    public readonly int PendingPointerCount => this.pointers?.Pending.Count ?? 0;

    /// <summary>
    ///     Gets the operation's pointer bookkeeping - the targets on the active path and the deferred pointers - taken
    ///     on first use from the thread's cache, and given back by <see cref="Release"/>.
    /// </summary>
    public PointerTraversal Pointers => this.pointers ??= PointerTraversal.Rent();

    /// <summary>Gets or sets the number of struct levels entered on the active path.</summary>
    public int StructureDepth { get; set; }

    /// <summary>
    ///     Gets or sets the dotted prefix (<c>hdr.</c>, <c>a.b.</c>) under which captures are also published while a
    ///     nested struct that an expression names through its path is read; <see langword="null"/> otherwise.
    /// </summary>
    public string? QualifiedPrefix { get; set; }

    /// <summary>Gets the conditional-scope locals stack; a frame reads its locals at the base <see cref="TakeLocals"/> returned. Read it again after a nested frame ran.</summary>
    public readonly SlotValue[] Locals => this.arena.Locals;

    /// <summary>Takes a frame's conditional-scope locals, every one <see cref="SlotValue.Undefined"/> ("no saved value").</summary>
    /// <param name="count">The scope's local count (or a union's slot count), not negative.</param>
    /// <returns>The index of the frame's first local in <see cref="Locals"/>.</returns>
    public int TakeLocals(int count) => this.arena.TakeLocals(count);

    /// <summary>Gives back the locals a completed frame took.</summary>
    /// <param name="start">The base <see cref="TakeLocals"/> returned.</param>
    public void ReleaseLocals(int start) => this.arena.ReleaseLocals(start);

    /// <summary>Selects a branch's arm in a frame through the shared rule (<see cref="FrameArena.SelectedArm"/>), in the read domain.</summary>
    /// <param name="program">The program.</param>
    /// <param name="branch">The branch being tested.</param>
    /// <param name="frameArms">The frame's selected arms, which the frame keeps on its stack.</param>
    /// <returns>The selected arm of the branch's group.</returns>
    public readonly int SelectedArm(ReadProgram program, ReadProgram.ConditionalBranch branch, Span<int> frameArms)
        => FrameArena.SelectedArm(frameArms, this.Slots, program.Groups, program.Expressions, program.ExpressionContexts, branch, ExpressionFailureDomain.Read);

    /// <summary>Completes a member of a conditional composite through the shared arena rule (<see cref="FrameArena.CompleteMember"/>).</summary>
    /// <param name="scope">The composite's scope in slot terms.</param>
    /// <param name="member">The member's index.</param>
    /// <param name="localBase">The base of the frame's saved values.</param>
    public void CompleteMember(ReadConditionalScope scope, int member, int localBase)
        => FrameArena.CompleteMember(ref this.arena, this.Slots, scope, member, localBase);

    /// <summary>
    ///     Saves every variable slot as the innermost union's entry values, in the locals stack after the current
    ///     frames, so every variable a member changes can be restored to its value at the union's entry.
    /// </summary>
    /// <returns>The enclosing union's saved values, which <see cref="ReleaseUnionSlots"/> makes current again.</returns>
    public int SaveUnionSlots()
    {
        int outer = this.unionSlots;
        this.unionSlots = this.TakeLocals(this.Slots.Count);
        this.Slots.CopyTo(this.arena.Locals, this.unionSlots);
        return outer;
    }

    /// <summary>Restores every variable slot to the innermost union's entry values, before each member view and when the union ends.</summary>
    public readonly void RestoreUnionSlots() => this.Slots.CopyFrom(this.arena.Locals, this.unionSlots);

    /// <summary>Gives back the innermost union's saved values and makes the enclosing union's current again.</summary>
    /// <param name="outer">What <see cref="SaveUnionSlots"/> returned.</param>
    public void ReleaseUnionSlots(int outer)
    {
        this.ReleaseLocals(this.unionSlots);
        this.unionSlots = outer;
    }

    /// <summary>
    ///     Drops the deferred pointers queued after <paramref name="count"/> entries: a struct that failed never
    ///     follows its own.
    /// </summary>
    /// <param name="count">The entries of enclosing structs, which stay queued.</param>
    public readonly void DiscardPendingPointers(int count)
    {
        if (this.pointers?.Pending is { } pending && pending.Count > count)
        {
            pending.RemoveRange(count, pending.Count - count);
        }
    }

    /// <summary>
    ///     Returns the frame arena's locals stack to the thread's spare at the end of the operation, cleared so no payload
    ///     stays alive, and the pointer bookkeeping to the thread's cache.
    /// </summary>
    public void Release()
    {
        if (this.pointers is not null)
        {
            PointerTraversal.Return(this.pointers);
            this.pointers = null;
        }

        this.arena.Release();
    }

    /// <summary>
    ///     Claims one struct level: the cancellation token first, then the nesting limit.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, which carries the token.</param>
    /// <exception cref="System.OperationCanceledException">The token is cancelled.</exception>
    /// <exception cref="CStructReadLimitException">The level would exceed <see cref="MaxNestingDepth"/>.</exception>
    public void EnterStructure<TCursor>(ref TCursor cursor)
        where TCursor : struct, IReadCursor
    {
        cursor.ThrowIfCancellationRequested();
        if (this.StructureDepth + 1 > this.MaxNestingDepth)
        {
            throw new CStructReadLimitException(ReadFailures.NestingLimit);
        }

        this.StructureDepth++;
    }

    /// <summary>
    ///     Returns whether the nesting and array limits admit running <paramref name="plan"/> at the current depth; the
    ///     byte budget is checked when the bytes are taken.
    /// </summary>
    /// <param name="plan">The static read plan of a fixed composite.</param>
    /// <returns>Whether the plan's structs and arrays stay within the limits.</returns>
    public readonly bool CoversPlan(StaticReadPlan plan)
        => this.StructureDepth + plan.NestingDepth <= this.MaxNestingDepth && plan.MaximumArrayCount <= this.MaxArrayElements;

    /// <summary>Applies <see cref="TrimFixedText"/> to one decoded fixed-capacity string.</summary>
    /// <param name="text">The decoded text, including any NUL padding.</param>
    /// <returns>The text without trailing NUL characters when trimming is on; otherwise unchanged.</returns>
    public readonly string FixedText(string text) => this.TrimFixedText ? text.TrimEnd('\0') : text;

    /// <summary>
    ///     While a qualified prefix is active, publishes a just-captured value under every target the prefix covers
    ///     (<see cref="QualifiedPublication.Covers"/>), copying (or removing) the qualified names; a
    ///     spelling without a slot is not observable and is skipped.
    /// </summary>
    /// <param name="targets">The bare name's publication targets, one per prefix some expression spells it with.</param>
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
}
