namespace CStructSharp.Reading;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Streams;
using CStructSharp.Syntax;

/// <summary>Keeps stream position, variables, pointer safety data, and optional debug data for one read operation.</summary>
internal sealed class CStructOperationContext
{
    /// <summary>The variable-dictionary key that holds the active qualified prefix; no field can be spelled this way.</summary>
    private const string QualifiedPrefixKey = "\0qualified-prefix";

    private bool hasQualifiedPrefix;

    private List<DebugData>? debugMapping;

    // Rented on the first pointer and returned by Complete; one field keeps the per-read state small.
    private PointerTraversal? pointers;

    /// <summary>Creates the read state from a stream, compiled lookup tables, and optional read settings.</summary>
    /// <param name="stream">The readable, seekable input, wrapped in a <see cref="ReadBudgetStream"/>.</param>
    /// <param name="variables">The operation's layout variables; reading a field adds or replaces its entry.</param>
    /// <param name="aligned">Whether fields are placed at their natural alignment, with padding between them.</param>
    /// <param name="options">The pointer, safety-limit, text, and cancellation settings for this operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or cannot seek.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit in <paramref name="options"/> is negative, or the
    ///     nesting depth is not positive.</exception>
    public CStructOperationContext(
        Stream stream,
        Dictionary<string, Expr> variables,
        bool aligned,
        ReadOperationSettings options)
    {
        Validate(stream, options);
        this.Stream = new ReadBudgetStream(
            stream,
            options.MaxStringBytes,
            options.MaxTotalBytesRead,
            options.CancellationToken);
        this.Variables = variables;
        this.CaptureAllLayoutVariables = variables is not LayoutVariables { CaptureAll: false };
        this.Aligned = aligned;

        // Copy nullable options into concrete defaults once so the hot parsing path never has to repeat this logic.
        this.PointerOrigin = options.Origin;
        this.AddressingMode = options.AddressingMode;
        this.DereferencePointers = options.DereferencePointers;
        this.MaxPointerDepth = options.MaxPointerDepth;
        this.MaxPointerTargetBytes = options.MaxPointerTargetBytes;
        this.MaxArrayElements = options.MaxArrayElements;
        this.MaxNestingDepth = options.MaxNestingDepth;
        this.TrimFixedText = options.TrimFixedText;
        this.GeneralPathOnly = options.ExecutionPath == ExecutionPath.GeneralOnly;
    }

    /// <summary>Gets whether pointer values are stream positions or offsets from <see cref="PointerOrigin"/>.</summary>
    public PointerAddressingMode AddressingMode { get; }

    /// <summary>Gets whether fields are placed at their natural alignment, with padding between them.</summary>
    public bool Aligned { get; }

    /// <summary>Gets whether pointer targets are read, rather than only the pointer values.</summary>
    public bool DereferencePointers { get; }

    /// <summary>The token the operation observes at composite, pointer, block, element, and chunk boundaries (kept on the budget stream so the per-operation objects carry it once).</summary>
    public System.Threading.CancellationToken CancellationToken => this.Stream.CancellationToken;

    /// <summary>
    ///     Gets or sets whether overlapping union member views must expose pointer addresses without following
    ///     external targets that are not known to be active.
    /// </summary>
    public bool SuppressPointerDereference { get; set; }

    /// <summary>Allocated on first use: ordinary reads never touch it.</summary>
    public List<DebugData> DebugMapping => this.debugMapping ??= new List<DebugData>();

    /// <summary>Optional active-branch trace used only for staged update validation.</summary>
    internal List<(string Path, long Start, long End)>? ConditionalLayoutTrace { get; set; }

    /// <summary>Rented on first pointer dereference: layouts without pointers never touch it.</summary>
    public HashSet<(long Address, string TypeName, int PointerDepth)> ActivePointerTargets => (this.pointers ??= PointerTraversal.Rent()).ActiveTargets;

    /// <summary>
    ///     The number of pointers read inside a struct whose targets are not yet followed. A struct remembers the
    ///     count on entry and follows the entries added after it once its last field is read.
    /// </summary>
    public int PendingPointerCount => this.pointers?.Pending.Count ?? 0;

    /// <summary>
    ///     The pointers waiting for their containing struct to finish. Rented on the first pointer read inside a
    ///     struct and kept for the rest of the operation, so layouts without pointers never touch it.
    /// </summary>
    public List<PendingPointer> PendingPointers => (this.pointers ??= PointerTraversal.Rent()).Pending;

    /// <summary>Gets the signed base position, in bytes, added to relative pointer offsets.</summary>
    public long PointerOrigin { get; }

    /// <summary>Gets the largest number of pointers the read may follow in one chain.</summary>
    public int MaxPointerDepth { get; }

    /// <summary>Gets the largest size, in bytes, of one pointer target, or null for no limit.</summary>
    public long? MaxPointerTargetBytes { get; }

    /// <summary>Gets the largest element count one array may declare.</summary>
    public int MaxArrayElements { get; }

    /// <summary>Whether fixed-capacity text drops its trailing NUL padding (<see cref="ReadOptions.TrimFixedText"/>).</summary>
    public bool TrimFixedText { get; }

    /// <summary>Gets whether the read must avoid static plans and block reads (<see cref="ExecutionPath.GeneralOnly"/>).</summary>
    public bool GeneralPathOnly { get; }

    /// <summary>Gets the largest number of nested struct levels the read may enter.</summary>
    public int MaxNestingDepth { get; }

    /// <summary>Gets or sets the number of pointers followed on the active path to the value being read.</summary>
    public int PointerDereferenceDepth { get; set; }

    /// <summary>Gets or sets the number of nested struct levels entered on the active path.</summary>
    public int StructureDepth { get; set; }

    /// <summary>The operation's read cursor: budget accounting plus, for memory sources, position and span reads.</summary>
    public ReadBudgetStream Stream { get; }

    /// <summary>
    ///     Gets the layout variables that array lengths and conditions evaluate against; reading a field adds or
    ///     replaces its entry.
    /// </summary>
    public Dictionary<string, Expr> Variables { get; }

    /// <summary>
    ///     True when every field must publish its layout variable, because the supplied variables contain an
    ///     unevaluated expression that may name any field; otherwise only fields the compiled layout's own
    ///     expressions reference (<see cref="CompiledField.CapturesLayoutVariable"/>) are captured.
    /// </summary>
    public bool CaptureAllLayoutVariables { get; }

    /// <summary>
    ///     The dotted prefix (<c>hdr.</c>, <c>a.b.</c>) of the nested struct fields being read, when an expression
    ///     names one of them through its path (<see cref="CompiledField.QualifiedPrefix"/>); otherwise null.
    /// </summary>
    public string? QualifiedPrefix
    {
        get => this.hasQualifiedPrefix ? ((Identifier)this.Variables[QualifiedPrefixKey]).Name : null;
        set
        {
            // The prefix lives in the operation's own variable dictionary (an entry only a layout with dotted
            // references ever adds) and a bool in the state's padding, so every other operation allocates exactly
            // what it did before the feature existed.
            this.hasQualifiedPrefix = value is not null;
            if (value is null)
            {
                this.Variables.Remove(QualifiedPrefixKey);
            }
            else
            {
                this.Variables[QualifiedPrefixKey] = new Identifier(value);
            }
        }
    }

    /// <summary>Whether a qualified prefix is active - the one check a capture site pays.</summary>
    public bool HasQualifiedPrefix => this.hasQualifiedPrefix;

    /// <summary>Gets or sets the bits of the open bitfield storage unit already used, counted from the unit's low bit.</summary>
    public int CurrentBitOffset { get; set; }

    /// <summary>Gets or sets whether a bitfield storage unit is open, so a following bitfield may share it.</summary>
    public bool BitfieldUnitOpen { get; set; }

    /// <summary>Gets or sets the size, in bytes, of the last opened bitfield storage unit.</summary>
    public int CurrentBitfieldSize { get; set; }

    /// <summary>
    ///     Whether the current bitfield state (offset, unit size) was seeded from a resolved target that the placement
    ///     cursor already placed; the per-field placement then uses it as is instead of re-deriving a unit.
    /// </summary>
    public bool BitfieldUnitSeeded { get; set; }

    /// <summary>Gets or sets whether each read value records its byte range in <see cref="DebugMapping"/>.</summary>
    public bool Debug { get; set; }

    /// <summary>
    ///     Gets or sets the stream position, in bytes, just past the last value or bitfield storage unit read; the
    ///     reader moves there when it leaves an unfinished bitfield unit.
    /// </summary>
    public long NextPosition { get; set; }

    /// <summary>
    ///     Rejects a source or settings a read operation cannot start with, before any byte is read: the checks the
    ///     interpreter's operation state and the compiled engine both make, in this order, so a failing call reports the
    ///     same exception whichever implementation runs it.
    /// </summary>
    /// <param name="stream">The caller's input stream.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or cannot seek.</exception>
    /// <exception cref="OperationCanceledException">The operation's token is already cancelled.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit in <paramref name="options"/> is negative, or the
    ///     nesting depth is not positive.</exception>
    public static void Validate(Stream stream, in ReadOperationSettings options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Parsing requires a readable, seekable stream.", nameof(stream));
        }

        ValidateSettings(options);
    }

    /// <summary>
    ///     The settings half of <see cref="Validate"/>, for a source that is always readable and seekable (a pinned
    ///     memory region): the token first, then every limit, in the order <see cref="Validate"/> checks them.
    /// </summary>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <exception cref="OperationCanceledException">The operation's token is already cancelled.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit in <paramref name="options"/> is negative, or the
    ///     nesting depth is not positive.</exception>
    public static void ValidateSettings(in ReadOperationSettings options)
    {
        // A token cancelled before the call ends the operation before any byte is read.
        options.CancellationToken.ThrowIfCancellationRequested();
        if (options.MaxPointerDepth < 0)
        {
            // A negative limit has no meaningful safety interpretation and would make the comparison misleading.
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum pointer depth cannot be negative.");
        }

        if (options.MaxPointerTargetBytes < 0)
        {
            // Likewise, a byte budget must either be absent or be a non-negative number of bytes.
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum pointer target bytes cannot be negative.");
        }

        if (options.MaxArrayElements < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum array elements cannot be negative.");
        }

        if (options.MaxStringBytes < 0 || options.MaxTotalBytesRead < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Read byte limits cannot be negative.");
        }

        if (options.MaxNestingDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum nesting depth must be greater than zero.");
        }
    }

    /// <summary>Returns whether the nesting and array limits admit running <paramref name="plan"/> at the current structure depth.</summary>
    /// <param name="plan">The static read plan.</param>
    /// <returns>Whether the plan's structs and arrays stay within the limits; the byte budget is checked when the bytes are taken.</returns>
    public bool CoversPlan(StaticReadPlan plan)
        => this.StructureDepth + plan.NestingDepth <= this.MaxNestingDepth && plan.MaximumArrayCount <= this.MaxArrayElements;

    /// <summary>Closes the open bitfield storage unit, so the next bitfield starts a new one.</summary>
    public void ResetBitfieldUnit()
    {
        this.CurrentBitOffset = 0;
        this.BitfieldUnitOpen = false;
    }

    /// <summary>Removes every deferred pointer queued after <paramref name="count"/> entries.</summary>
    /// <param name="count">The number of entries, belonging to enclosing structs, that stay queued.</param>
    public void DiscardPendingPointers(int count)
    {
        if (this.pointers?.Pending is { } pending && pending.Count > count)
        {
            pending.RemoveRange(count, pending.Count - count);
        }
    }

    /// <summary>
    ///     Writes the cursor position back to the caller's stream and returns the pointer bookkeeping to the thread's
    ///     cache; call once when the operation ends, before any error context is captured.
    /// </summary>
    public void Complete()
    {
        this.Stream.FlushPosition();
        if (this.pointers is { } pointers)
        {
            // The results hold only the resolved pointers; the bookkeeping goes back to this thread's cache.
            this.pointers = null;
            PointerTraversal.Return(pointers);
        }
    }

    /// <summary>Completes a failed operation, then adds the path and the failure's stream offset to its exception.</summary>
    /// <remarks>Completing first writes the operation's position back to <paramref name="stream"/>, and that position
    /// is the offset the exception reports. Completing again in a <c>finally</c> block does nothing more.</remarks>
    /// <param name="exception">The failure, rethrown by the caller.</param>
    /// <param name="segments">The operation's parsed path.</param>
    /// <param name="stream">The caller's stream.</param>
    public void CompleteWithContext(CStructException exception, IReadOnlyList<PathSegment> segments, Stream stream)
    {
        this.Complete();
        ExceptionContext.Attach(exception, segments, stream);
    }

    /// <summary>Claims one nested-struct level and rejects input that exceeds the caller's recursion budget.</summary>
    public void EnterStructure()
    {
        this.CancellationToken.ThrowIfCancellationRequested();
        this.EnsureStructureDepth(this.StructureDepth + 1);
        this.StructureDepth++;
    }

    /// <summary>Rejects a logical structure depth before traversal or a selected reader commits to it.</summary>
    /// <param name="requiredDepth">The structure depth the caller is about to use.</param>
    /// <exception cref="CStructReadLimitException">The depth exceeds <see cref="MaxNestingDepth"/>.</exception>
    public void EnsureStructureDepth(int requiredDepth)
    {
        if (requiredDepth > this.MaxNestingDepth)
        {
            throw new CStructReadLimitException(ReadFailures.NestingLimit);
        }
    }

    /// <summary>Releases one nested-struct level after a successful or failed child read.</summary>
    public void ExitStructure()
    {
        this.StructureDepth--;
    }

    /// <summary>Copies the bytes and layout stack for one read value into the debug result.</summary>
    /// <param name="curPos">The stream position, in bytes, where the value starts.</param>
    /// <param name="endPos">The stream position, in bytes, just past the value.</param>
    /// <param name="debugStack">The path of layout members leading to the value, or null at the root.</param>
    /// <param name="value">The decoded value.</param>
    /// <param name="fieldTypeName">The name of the value's declared type.</param>
    public void RegisterDebugData(
        long curPos,
        long endPos,
        DebugPath? debugStack,
        object value,
        string fieldTypeName)
    {
        // The record carries the range only; the caller owns the input and can select Start..End from it, so no
        // bytes are re-read or copied per field.
        this.DebugMapping.Add(
                              new DebugData
                              {
                                  Start = curPos,
                                  End = endPos,
                                  DebugStack = debugStack,
                                  Value = value,
                                  TypeName = fieldTypeName,
                              });
    }

    /// <summary>
    ///     Republishes a just-captured variable under the qualified names a dotted reference can spell while a prefix is
    ///     active (<see cref="QualifiedPublication"/>).
    /// </summary>
    /// <param name="name">The unqualified field name whose variable entry is copied or removed.</param>
    public void PublishQualified(string name)
    {
        if (this.hasQualifiedPrefix)
        {
            QualifiedPublication.Publish(this.Variables, this.QualifiedPrefix!, name);
        }
    }

    /// <summary>Applies <see cref="TrimFixedText"/> to one decoded fixed-capacity string.</summary>
    /// <param name="text">The decoded fixed-capacity text, including any NUL padding.</param>
    /// <returns>The text without trailing NUL characters when trimming is on; otherwise the unchanged text.</returns>
    public string FixedText(string text)
    {
        return this.TrimFixedText ? text.TrimEnd('\0') : text;
    }
}
