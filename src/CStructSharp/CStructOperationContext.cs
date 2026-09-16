namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Structure;

/// <summary>Keeps stream position, variables, pointer safety data, and optional debug data for one read operation.</summary>
internal sealed class CStructOperationContext
{
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
    public string? QualifiedPrefix { get; set; }

    public int CurrentBitOffset { get; set; }

    public string? CurrentBitfieldType { get; set; }

    public int CurrentBitfieldSize { get; set; }

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
        // Move back to the value's start because normal parsing has already advanced past it.
        this.Stream.Position = curPos;

        // Even a zero-width layout gets a one-byte debug buffer so consumers always receive inspectable data.
        long bufferLen = Math.Max(endPos - curPos, 1);
        byte[] buffer = new byte[bufferLen];
        this.Stream.ReadExactly(buffer);

        // Restore the post-value position before adding metadata; debug collection must not change parsing behavior.
        this.Stream.Position = endPos;

        // The captured bytes are kept as they were read (E2.8): the former int[] widening copy doubled the work and
        // quadrupled the retained memory of every debug record for no additional information.
        this.DebugMapping.Add(
                              new DebugData
                              {
                                  CurPos = curPos,
                                  EndPos = endPos,
                                  DebugStack = debugStack,
                                  Value = value,
                                  Buffer = buffer,
                                  TypeName = fieldTypeName,
                              });
    }

    /// <summary>Republishes a just-captured variable under its qualified name when a dotted reference needs it.</summary>
    public void PublishQualified(string name)
    {
        if (this.QualifiedPrefix is not null)
        {
            if (this.Variables.TryGetValue(name, out Expr? value))
            {
                this.Variables[this.QualifiedPrefix + name] = value;
            }
            else
            {
                this.Variables.Remove(this.QualifiedPrefix + name);
            }
        }
    }
}
