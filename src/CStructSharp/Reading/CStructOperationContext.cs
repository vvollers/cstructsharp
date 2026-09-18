namespace CStructSharp.Reading;

using System;
using System.Collections.Generic;
using System.IO;
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
    private HashSet<(long Address, string TypeName, int PointerDepth)>? activePointerTargets;

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

        this.Stream = new ReadBudgetStream(
            stream,
            options.MaxStringBytes,
            options.MaxTotalBytesRead);
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

    /// <summary>
    ///     Gets or sets whether overlapping union member views must expose pointer addresses without following
    ///     external targets that are not known to be active.
    /// </summary>
    public bool SuppressPointerDereference { get; set; }

    /// <summary>Allocated on first use: ordinary reads never touch it (E2.12).</summary>
    public List<DebugData> DebugMapping => this.debugMapping ??= new List<DebugData>();

    /// <summary>Optional active-branch trace used only for staged update validation.</summary>
    internal List<(string Path, long Start, long End)>? ConditionalLayoutTrace { get; set; }

    /// <summary>Allocated on first pointer dereference: layouts without pointers never touch it (E2.12).</summary>
    public HashSet<(long Address, string TypeName, int PointerDepth)> ActivePointerTargets => this.activePointerTargets ??= new HashSet<(long, string, int)>();

    public long PointerOrigin { get; }

    public int MaxPointerDepth { get; }

    public long? MaxPointerTargetBytes { get; }

    public int MaxArrayElements { get; }

    /// <summary>Whether fixed-capacity text drops its trailing NUL padding (<see cref="ReadOptions.TrimFixedText"/>).</summary>
    public bool TrimFixedText { get; }

    public int MaxNestingDepth { get; }

    public int PointerDereferenceDepth { get; set; }

    public int StructureDepth { get; set; }

    /// <summary>The operation's read cursor: budget accounting plus, for memory sources, position and span reads (E2.1).</summary>
    public ReadBudgetStream Stream { get; }

    public Dictionary<string, Expr> Variables { get; }

    /// <summary>
    ///     True when every field must publish its layout variable, because the supplied variables contain an
    ///     unevaluated expression that may name any field (E2.6); otherwise only fields the compiled layout's own
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

    public int CurrentBitOffset { get; set; }

    public string? CurrentBitfieldType { get; set; }

    public int CurrentBitfieldSize { get; set; }

    /// <summary>
    ///     Whether the current bitfield state (offset, unit size) was seeded from a resolved target that the placement
    ///     cursor already placed; the legacy per-field placement then uses it as is instead of re-deriving a unit.
    /// </summary>
    public bool BitfieldUnitSeeded { get; set; }

    public int CurrentFieldAlignment { get; set; }

    public bool Debug { get; set; }

    public long NextPosition { get; set; }

    /// <summary>Claims one nested-struct level and rejects input that exceeds the caller's recursion budget.</summary>
    /// <summary>Writes the cursor position back to the caller's stream; call once when the operation ends, before any error context is captured.</summary>
    public void Complete()
    {
        this.Stream.FlushPosition();
    }

    public void EnterStructure()
    {
        this.EnsureStructureDepth(this.StructureDepth + 1);
        this.StructureDepth++;
    }

    /// <summary>Rejects a logical structure depth before traversal or a selected reader commits to it.</summary>
    public void EnsureStructureDepth(int requiredDepth)
    {
        if (requiredDepth > this.MaxNestingDepth)
        {
            throw new CStructReadLimitException("Maximum nested struct depth exceeded.");
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
