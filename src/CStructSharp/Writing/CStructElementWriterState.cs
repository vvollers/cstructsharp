namespace CStructSharp.Writing;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Streams;
using CStructSharp.Syntax;

/// <summary>Keeps stream position, variables, options, and bitfield progress for one write operation.</summary>
internal sealed class CStructElementWriterState
{
    /// <summary>
    ///     The same instance as <see cref="Stream" />, kept under its concrete type so
    ///     <see cref="EnsureStringBytes" />/<see cref="WriteZeroes" /> can call its budget-specific members
    ///     directly instead of downcasting the publicly-typed <see cref="Stream" /> property on every call - the
    ///     constructor is the only place that needs to know the concrete type is always a
    ///     <see cref="WriteBudgetStream" />.
    /// </summary>
    /// <summary>The variable-dictionary key that holds the active qualified prefix; no field can be spelled this way.</summary>
    private const string QualifiedPrefixKey = "\0qualified-prefix";

    private readonly WriteBudgetStream budgetStream;

    private bool hasQualifiedPrefix;

    /// <summary>Creates the write state from a stream, compiled lookup tables, and write settings.</summary>
    public CStructElementWriterState(
        Stream stream,
        Dictionary<string, Expr> variables,
        bool aligned,
        WriteOptions options,
        int initialStructureDepth = 0)
    {
        this.Variables = variables;
        this.CaptureAllLayoutVariables = variables is not LayoutVariables { CaptureAll: false };
        this.Aligned = aligned;

        // The public boundary has already validated this immutable option value.
        this.Options = options;
        this.budgetStream = new WriteBudgetStream(stream, this.Options);
        this.Stream = this.budgetStream;
        this.PointerOrigin = this.Options.Origin;
        this.AddressingMode = this.Options.AddressingMode;
        this.RejectUnknownMembers = this.Options.UnknownMembers == UnknownMemberPolicy.Reject;
        this.MaxNestingDepth = this.Options.MaxNestingDepth;
        this.StructureDepth = initialStructureDepth;
        if (initialStructureDepth < 0 || initialStructureDepth > this.MaxNestingDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialStructureDepth),
                "The initial structure depth is outside the configured write limit.");
        }
    }

    public PointerAddressingMode AddressingMode { get; }

    public bool Aligned { get; }

    /// <summary>Whether a member the composite does not declare fails the write (<see cref="WriteOptions.UnknownMembers"/>).</summary>
    public bool RejectUnknownMembers { get; }

    public WriteOptions Options { get; }

    public long PointerOrigin { get; }

    public int MaxNestingDepth { get; }

    public int StructureDepth { get; private set; }

    /// <summary>Gets or sets whether the next field starts at an already resolved exact byte address.</summary>
    public bool PositionIsResolvedTarget { get; set; }

    public Stream Stream { get; }

    /// <summary>The same stream as <see cref="Stream"/>, typed for the static write plan's block write.</summary>
    public WriteBudgetStream BudgetStream => this.budgetStream;

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

    public int CurrentBitOffset { get; set; }

    public string? CurrentBitfieldType { get; set; }

    public int CurrentBitfieldSize { get; set; }

    /// <summary>
    ///     Whether the current bitfield state (offset, unit size) was seeded from a resolved target that the placement
    ///     cursor already placed; the legacy per-field placement then uses it as is instead of re-deriving a unit.
    /// </summary>
    public bool BitfieldUnitSeeded { get; set; }

    public int CurrentFieldAlignment { get; set; }

    public long NextPosition { get; set; }

    /// <summary>Copies every update choice before variable enumeration, payload access, or stream traversal.</summary>
    public static UpdateOptions SnapshotUpdateOptions(UpdateOptions? options)
    {
        // The record's own `with` expression clones every current and future property in one step, instead of a
        // hand-maintained property-by-property copy that silently reverts a newly added property to its default
        // on every call until someone remembers to list it here too.
        return (options ?? new UpdateOptions()) with { };
    }

    /// <summary>Copies normal write choices while retaining update semantics when that derived value was supplied.</summary>
    public static WriteOptions SnapshotWriteOptions(WriteOptions? options)
    {
        if (options is UpdateOptions updateOptions)
        {
            return SnapshotUpdateOptions(updateOptions);
        }

        return (options ?? new WriteOptions()) with { };
    }

    /// <summary>Validates finite write budgets once at the public operation boundary.</summary>
    public static void ValidateWriteOptions(WriteOptions options)
    {
        if (options.MaxArrayElements < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum array elements cannot be negative.");
        }

        if (options.MaxStringBytes < 0 || options.MaxTotalBytesWritten < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Write byte limits cannot be negative.");
        }

        if (options.MaxNestingDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum nesting depth must be greater than zero.");
        }
    }

    /// <summary>Claims one active composite level before any fields at that level are written.</summary>
    public void EnterStructure()
    {
        if (this.StructureDepth >= this.MaxNestingDepth)
        {
            throw new CStructWriteLimitException(WriteFailures.NestingLimit);
        }

        this.StructureDepth++;
    }

    /// <summary>Releases one active composite level after a successful or failed nested write.</summary>
    public void ExitStructure()
    {
        this.StructureDepth--;
    }

    /// <summary>Checks one fixed or terminated string's complete encoded storage before allocation or output.</summary>
    public void EnsureStringBytes(long encodedByteCount)
    {
        this.budgetStream.EnsureStringBytes(encodedByteCount);
    }

    /// <summary>Preflights and writes structural zero-fill without allocating the complete region.</summary>
    public void WriteZeroes(int count)
    {
        this.budgetStream.WriteZeroes(count);
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
}
