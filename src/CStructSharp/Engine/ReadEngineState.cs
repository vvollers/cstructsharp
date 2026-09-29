namespace CStructSharp.Engine;

using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;

/// <summary>
///     The state one compiled-engine read operation carries through its frames, beside the cursor: the layout-variable
///     slots, the limits the executor checks itself, the nesting depth and the active qualified prefix. It holds what
///     the interpreter's <see cref="CStructOperationContext"/> holds for the same read, minus the stream, which is the
///     cursor, and the features later stages add (bitfield units, pointers, debug records).
/// </summary>
/// <remarks>
///     A mutable struct owned by one operation and passed by reference to every frame, so the depth and prefix changes a
///     nested struct makes are the caller's. The slots are borrowed: the operation that created them disposes them.
/// </remarks>
internal struct ReadEngineState
{
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

    /// <summary>Applies <see cref="TrimFixedText"/> to one decoded fixed-capacity string.</summary>
    /// <param name="text">The decoded text, including any NUL padding.</param>
    /// <returns>The text without trailing NUL characters when trimming is on; otherwise unchanged.</returns>
    public readonly string FixedText(string text) => this.TrimFixedText ? text.TrimEnd('\0') : text;

    /// <summary>
    ///     While a qualified prefix is active, publishes a just-captured value under the one target the prefix and the
    ///     bare name spell, as the interpreter copies (or removes) <c>prefix + name</c>; a spelling without a slot is not
    ///     observable and is skipped.
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
            if (string.Equals(target.Prefix, prefix, System.StringComparison.Ordinal))
            {
                this.Slots.Set(target.Slot, value);
            }
        }
    }
}
