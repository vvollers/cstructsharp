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
    /// <summary>The variable-dictionary key that holds the active qualified prefix; no field can be spelled this way.</summary>
    private const string QualifiedPrefixKey = "\0qualified-prefix";

    /// <summary>
    ///     The same instance as <see cref="Stream" />, kept under its concrete type so
    ///     <see cref="EnsureStringBytes" />/<see cref="WriteZeroes" /> can call its budget-specific members
    ///     directly instead of downcasting the publicly-typed <see cref="Stream" /> property on every call - the
    ///     constructor is the only place that needs to know the concrete type is always a
    ///     <see cref="WriteBudgetStream" />.
    /// </summary>
    private readonly WriteBudgetStream budgetStream;

    /// <summary>Whether <see cref="QualifiedPrefixKey"/> currently holds an active prefix in the variables.</summary>
    private bool hasQualifiedPrefix;

    /// <summary>Creates the write state from a stream, compiled lookup tables, and write settings.</summary>
    /// <param name="stream">
    ///     The caller's destination, wrapped in a <see cref="WriteBudgetStream"/>; it stays open and caller-owned.
    /// </param>
    /// <param name="variables">
    ///     The operation's variable dictionary; the write adds captured field values and the qualified prefix to it.
    /// </param>
    /// <param name="aligned">Whether composite fields use their portable alignment boundaries.</param>
    /// <param name="options">The already validated and snapshotted write options.</param>
    /// <param name="initialStructureDepth">
    ///     The composite depth already active when this write starts, for a write nested in another operation.
    /// </param>
    /// <exception cref="OperationCanceledException">The options' cancellation token is already cancelled.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="initialStructureDepth"/> is negative or above <see cref="WriteOptions.MaxNestingDepth"/>.
    /// </exception>
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

        // The public boundary has already validated this immutable option value; a token cancelled before the call
        // ends the operation before any byte is staged or written.
        options.CancellationToken.ThrowIfCancellationRequested();
        this.Options = options;
        this.budgetStream = new WriteBudgetStream(stream, this.Options);
        this.Stream = this.budgetStream;
        this.PointerOrigin = this.Options.Origin;
        this.AddressingMode = this.Options.AddressingMode;
        this.RejectUnknownMembers = this.Options.UnknownMembers == UnknownMemberPolicy.Reject;
        this.MaxNestingDepth = this.Options.MaxNestingDepth;
        this.GeneralPathOnly = this.Options.ExecutionPath == ExecutionPath.GeneralOnly;
        this.StructureDepth = initialStructureDepth;
        if (initialStructureDepth < 0 || initialStructureDepth > this.MaxNestingDepth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(initialStructureDepth),
                "The initial structure depth is outside the configured write limit.");
        }
    }

    /// <summary>Gets the options a write without caller options uses; shared because <see cref="WriteOptions"/> is immutable.</summary>
    public static WriteOptions DefaultWriteOptions { get; } = new();

    /// <summary>
    ///     Gets whether written pointer values are absolute stream positions or offsets from
    ///     <see cref="PointerOrigin"/>.
    /// </summary>
    public PointerAddressingMode AddressingMode { get; }

    /// <summary>Gets whether composite fields use their portable alignment boundaries.</summary>
    public bool Aligned { get; }

    /// <summary>Whether a member the composite does not declare fails the write (<see cref="WriteOptions.UnknownMembers"/>).</summary>
    public bool RejectUnknownMembers { get; }

    /// <summary>Gets the snapshotted options for budgets, pointers, and cancellation.</summary>
    public WriteOptions Options { get; }

    /// <summary>Gets whether the write must avoid static plans and block writes (<see cref="ExecutionPath.GeneralOnly"/>).</summary>
    public bool GeneralPathOnly { get; }

    /// <summary>
    ///     Gets the stream position subtracted from relative pointer values before they are written
    ///     (<see cref="WriteOptions.Origin"/>).
    /// </summary>
    public long PointerOrigin { get; }

    /// <summary>Gets the greatest active struct or union depth this write may enter.</summary>
    public int MaxNestingDepth { get; }

    /// <summary>Gets the number of composite levels currently entered through <see cref="EnterStructure"/>.</summary>
    public int StructureDepth { get; private set; }

    /// <summary>Gets or sets whether the next field starts at an already resolved exact byte address.</summary>
    public bool PositionIsResolvedTarget { get; set; }

    /// <summary>Gets the budget-checked destination every field write goes through.</summary>
    public Stream Stream { get; }

    /// <summary>The same stream as <see cref="Stream"/>, typed for the static write plan's block write.</summary>
    public WriteBudgetStream BudgetStream => this.budgetStream;

    /// <summary>
    ///     Gets the operation's variables: caller-supplied values plus field values captured during the write, which
    ///     later size and count expressions evaluate against.
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
    ///     cursor already placed; the standalone write then uses it as is instead of opening a new unit.
    /// </summary>
    public bool BitfieldUnitSeeded { get; set; }

    /// <summary>Copies every update choice before variable enumeration, payload access, or stream traversal.</summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A private copy the caller cannot change during the operation.</returns>
    public static UpdateOptions SnapshotUpdateOptions(UpdateOptions? options)
    {
        // The record's own `with` expression clones every current and future property in one step, instead of a
        // hand-maintained property-by-property copy that silently reverts a newly added property to its default
        // on every call until someone remembers to list it here too.
        return (options ?? new UpdateOptions()) with { };
    }

    /// <summary>Copies normal write choices while retaining update semantics when that derived value was supplied.</summary>
    /// <param name="options">The caller's write or update options, or <see langword="null"/>.</param>
    /// <returns>
    ///     <see cref="DefaultWriteOptions"/> for <see langword="null"/>; otherwise a copy of the same record type.
    /// </returns>
    public static WriteOptions SnapshotWriteOptions(WriteOptions? options)
    {
        if (options is UpdateOptions updateOptions)
        {
            return SnapshotUpdateOptions(updateOptions);
        }

        // WriteOptions is an immutable record, so the shared default needs no copy; a caller's instance is copied because
        // a derived record could add mutable state.
        return options is null ? DefaultWriteOptions : options with { };
    }

    /// <summary>Validates finite write budgets once at the public operation boundary.</summary>
    /// <param name="options">The options to check.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     A byte or element limit is negative, or the maximum nesting depth is not positive.
    /// </exception>
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
    /// <param name="encodedByteCount">The string's complete encoded size in bytes, including any terminator.</param>
    /// <exception cref="CStructWriteLimitException">
    ///     The size is negative or exceeds the string byte limit in <see cref="Options"/>.
    /// </exception>
    public void EnsureStringBytes(long encodedByteCount)
    {
        this.budgetStream.EnsureStringBytes(encodedByteCount);
    }

    /// <summary>Preflights and writes structural zero-fill without allocating the complete region.</summary>
    /// <param name="count">The number of zero bytes to write at the current position.</param>
    public void WriteZeroes(int count)
    {
        this.budgetStream.WriteZeroes(count);
    }

    /// <summary>Republishes a just-captured variable under its qualified name when a dotted reference needs it.</summary>
    /// <param name="name">
    ///     The field's unqualified variable name; its <see cref="QualifiedPrefix"/> copy is set, or removed when
    ///     the unqualified variable is absent.
    /// </param>
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

    /// <summary>Closes the open bitfield storage unit, so the next bitfield starts a new one.</summary>
    public void ResetBitfieldUnit()
    {
        this.CurrentBitOffset = 0;
        this.BitfieldUnitOpen = false;
    }
}
