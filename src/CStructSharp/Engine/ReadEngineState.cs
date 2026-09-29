namespace CStructSharp.Engine;

using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;

/// <summary>
///     The state one compiled-engine read operation carries through its frames, beside the cursor: the layout-variable
///     slots, the limits the executor checks itself, the nesting depth and the active qualified prefix. It holds what
///     the interpreter's <see cref="CStructOperationContext"/> holds for the same read, minus the stream, which is the
///     cursor, and the features later stages add (bitfield units, pointers, debug records).
/// </summary>
/// <remarks>
///     <para>
///         A mutable struct owned by one operation and passed by reference to every frame, so the depth and prefix
///         changes a nested struct makes are the caller's. The slots are borrowed: the operation that created them
///         disposes them.
///     </para>
///     <para>
///         <b>Frame arena.</b> Each frame's selected conditional arms and conditional-scope locals live in two stacks the
///         state owns rather than in arrays of their own: a frame takes a range on entry and gives it back when it
///         completes, so frames nest like the calls that run them. A failed operation abandons its ranges;
///         <see cref="Release"/> returns the stacks, cleared, to the thread's spares when the operation ends.
///     </para>
/// </remarks>
internal struct ReadEngineState
{
    /// <summary>The selected-arm value of a conditional group whose selector the frame has not evaluated yet.</summary>
    public const int Undecided = int.MinValue;

    // The thread's spare stacks, taken for the length of an operation; a nested operation on the same thread (from a
    // callback) finds them taken and allocates its own.
    [ThreadStatic]
    private static int[]? spareArms;

    [ThreadStatic]
    private static SlotValue[]? spareLocals;

    private int[]? arms;
    private int armTop;
    private SlotValue[]? locals;
    private int localTop;
    private int localHigh;

    /// <summary>Creates the state of one operation.</summary>
    /// <param name="layout">The layout being read, which owns the codecs and text encodings the steps use.</param>
    /// <param name="slots">The operation's initialized variable slots; borrowed, not disposed here.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    public ReadEngineState(CStruct layout, VariableSlots slots, in ReadOperationSettings options)
    {
        this.Layout = layout;
        this.Slots = slots;
        this.MaxArrayElements = options.MaxArrayElements;
        this.MaxNestingDepth = options.MaxNestingDepth;
        this.TrimFixedText = options.TrimFixedText;
        this.GeneralPathOnly = options.ExecutionPath == ExecutionPath.GeneralOnly;
        this.StructureDepth = 0;
        this.QualifiedPrefix = null;
        this.arms = null;
        this.armTop = 0;
        this.locals = null;
        this.localTop = 0;
        this.localHigh = 0;
    }

    /// <summary>Gets the layout being read.</summary>
    public CStruct Layout { get; }

    /// <summary>Gets the operation's layout variables as slots.</summary>
    public VariableSlots Slots { get; }

    /// <summary>Gets the largest element count one array may declare.</summary>
    public int MaxArrayElements { get; }

    /// <summary>Gets the largest number of nested struct levels the read may enter.</summary>
    public int MaxNestingDepth { get; }

    /// <summary>Gets whether fixed-capacity text drops its trailing NUL padding (<see cref="ReadOptions.TrimFixedText"/>).</summary>
    public bool TrimFixedText { get; }

    /// <summary>
    ///     Gets whether the read must avoid the static read plans and block reads (<see cref="ExecutionPath.GeneralOnly"/>),
    ///     as the interpreter does under the same option.
    /// </summary>
    public bool GeneralPathOnly { get; }

    /// <summary>Gets or sets the number of struct levels entered on the active path.</summary>
    public int StructureDepth { get; set; }

    /// <summary>
    ///     Gets or sets the dotted prefix (<c>hdr.</c>, <c>a.b.</c>) under which captures are also published while a
    ///     nested struct that an expression names through its path is read; <see langword="null"/> otherwise.
    /// </summary>
    public string? QualifiedPrefix { get; set; }

    /// <summary>Gets the selected-arm stack; a frame reads its arms at the base <see cref="TakeArms"/> returned. Read it again after a nested frame ran, which may have grown it.</summary>
    public readonly int[] Arms => this.arms!;

    /// <summary>Gets the conditional-scope locals stack; a frame reads its locals at the base <see cref="TakeLocals"/> returned. Read it again after a nested frame ran.</summary>
    public readonly SlotValue[] Locals => this.locals!;

    /// <summary>Takes a frame's selected arms, every group <see cref="Undecided"/>.</summary>
    /// <param name="count">The frame's conditional group count, positive.</param>
    /// <returns>The index of the frame's first arm in <see cref="Arms"/>.</returns>
    public int TakeArms(int count)
    {
        int start = this.armTop;
        int end = start + count;
        if (this.arms is null || this.arms.Length < end)
        {
            this.arms = Grow(this.arms, ref spareArms, end);
        }

        System.Array.Fill(this.arms, Undecided, start, count);
        this.armTop = end;
        return start;
    }

    /// <summary>Gives back the arms a completed frame took.</summary>
    /// <param name="start">The base <see cref="TakeArms"/> returned.</param>
    public void ReleaseArms(int start) => this.armTop = start;

    /// <summary>Takes a frame's conditional-scope locals, every one <see cref="SlotValue.Undefined"/> (the interpreter's "no saved value").</summary>
    /// <param name="count">The scope's local count, positive.</param>
    /// <returns>The index of the frame's first local in <see cref="Locals"/>.</returns>
    public int TakeLocals(int count)
    {
        int start = this.localTop;
        int end = start + count;
        if (this.locals is null || this.locals.Length < end)
        {
            this.locals = Grow(this.locals, ref spareLocals, end);
        }

        System.Array.Clear(this.locals, start, count);
        this.localTop = end;
        this.localHigh = System.Math.Max(this.localHigh, end);
        return start;
    }

    /// <summary>Gives back the locals a completed frame took.</summary>
    /// <param name="start">The base <see cref="TakeLocals"/> returned.</param>
    public void ReleaseLocals(int start) => this.localTop = start;

    /// <summary>Returns the frame stacks to the thread's spares at the end of the operation, the locals cleared so no payload stays alive.</summary>
    public void Release()
    {
        if (this.arms is not null)
        {
            spareArms = this.arms;
            this.arms = null;
        }

        if (this.locals is not null)
        {
            System.Array.Clear(this.locals, 0, this.localHigh);
            spareLocals = this.locals;
            this.locals = null;
        }
    }

    /// <summary>
    ///     Claims one struct level as the interpreter's <c>EnterStructure</c> does: the cancellation token first, then the
    ///     nesting limit.
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
    ///     Returns whether the nesting and array limits admit running <paramref name="plan"/> at the current depth, the
    ///     interpreter's <c>CoversPlan</c>; the byte budget is checked when the bytes are taken.
    /// </summary>
    /// <param name="plan">The static read plan of a fixed composite.</param>
    /// <returns>Whether the plan's structs and arrays stay within the limits.</returns>
    public readonly bool CoversPlan(StaticReadPlan plan)
        => this.StructureDepth + plan.NestingDepth <= this.MaxNestingDepth && plan.MaximumArrayCount <= this.MaxArrayElements;

    /// <summary>Returns a stack of at least <paramref name="length"/> entries holding the current one's entries: the thread's spare when it is free and large enough, else a new, larger array.</summary>
    /// <typeparam name="T">The entry type.</typeparam>
    /// <param name="current">The stack in use, or <see langword="null"/>.</param>
    /// <param name="spare">The thread's spare of this type; taken when used.</param>
    /// <param name="length">The length needed.</param>
    /// <returns>The stack.</returns>
    private static T[] Grow<T>(T[]? current, ref T[]? spare, int length)
    {
        T[] grown;
        if (current is null && spare is { } free && free.Length >= length)
        {
            grown = free;
            spare = null;
            return grown;
        }

        grown = new T[System.Math.Max(16, System.Math.Max(length, (current?.Length ?? 0) * 2))];
        if (current is not null)
        {
            System.Array.Copy(current, grown, current.Length);
        }

        return grown;
    }

    /// <summary>Applies <see cref="TrimFixedText"/> to one decoded fixed-capacity string.</summary>
    /// <param name="text">The decoded text, including any NUL padding.</param>
    /// <returns>The text without trailing NUL characters when trimming is on; otherwise unchanged.</returns>
    public readonly string FixedText(string text) => this.TrimFixedText ? text.TrimEnd('\0') : text;

    /// <summary>
    ///     While a qualified prefix is active, publishes a just-captured value under every target the prefix covers
    ///     (<see cref="QualifiedPublication.Covers"/>), as the interpreter copies (or removes) the qualified names; a
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
