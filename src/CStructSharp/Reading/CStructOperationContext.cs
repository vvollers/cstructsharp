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
    public CStructOperationContext(
        Stream stream,
        Dictionary<string, Expr> variables,
        bool aligned,
        ReadOperationSettings options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Parsing requires a readable, seekable stream.", nameof(stream));
        }

        // A token cancelled before the call ends the operation before any byte is read.
        options.CancellationToken.ThrowIfCancellationRequested();

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
        if (this.MaxPointerDepth < 0)
        {
            // A negative limit has no meaningful safety interpretation and would make the comparison misleading.
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum pointer depth cannot be negative.");
        }

        if (this.MaxPointerTargetBytes < 0)
        {
            // Likewise, a byte budget must either be absent or be a non-negative number of bytes.
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum pointer target bytes cannot be negative.");
        }

        if (this.MaxArrayElements < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum array elements cannot be negative.");
        }

        if (options.MaxStringBytes < 0 || options.MaxTotalBytesRead < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Read byte limits cannot be negative.");
        }

        if (this.MaxNestingDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum nesting depth must be greater than zero.");
        }
    }

    public PointerAddressingMode AddressingMode { get; }

    public bool Aligned { get; }

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

    public long PointerOrigin { get; }

    public int MaxPointerDepth { get; }

    public long? MaxPointerTargetBytes { get; }

    public int MaxArrayElements { get; }

    /// <summary>Whether fixed-capacity text drops its trailing NUL padding (<see cref="ReadOptions.TrimFixedText"/>).</summary>
    public bool TrimFixedText { get; }

    /// <summary>Gets whether the read must avoid static plans and block reads (<see cref="ExecutionPath.GeneralOnly"/>).</summary>
    public bool GeneralPathOnly { get; }

    public int MaxNestingDepth { get; }

    public int PointerDereferenceDepth { get; set; }

    public int StructureDepth { get; set; }

    /// <summary>The operation's read cursor: budget accounting plus, for memory sources, position and span reads.</summary>
    public ReadBudgetStream Stream { get; }

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

    public bool Debug { get; set; }

    public long NextPosition { get; set; }

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

    /// <summary>Republishes a just-captured variable under its qualified name when a dotted reference needs it.</summary>
    public void PublishQualified(string name)
    {
        if (this.hasQualifiedPrefix)
        {
            string prefix = this.QualifiedPrefix!;
            if (this.Variables.TryGetValue(name, out Expr? value))
            {
                this.Variables[prefix + name] = value;
            }
            else
            {
                this.Variables.Remove(prefix + name);
            }
        }
    }

    /// <summary>Applies <see cref="TrimFixedText"/> to one decoded fixed-capacity string.</summary>
    public string FixedText(string text)
    {
        return this.TrimFixedText ? text.TrimEnd('\0') : text;
    }
}
