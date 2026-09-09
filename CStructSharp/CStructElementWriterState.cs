namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Structure;

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
    private readonly WriteBudgetStream budgetStream;

    /// <summary>Creates the write state from a stream, compiled lookup tables, and write settings.</summary>
    public CStructElementWriterState(
        Stream stream,
        Dictionary<string, Expr> variables,
        bool aligned,
        WriteOptions options,
        int initialStructureDepth = 0)
    {
        this.Variables = variables;
        this.Aligned = aligned;

        // The public boundary has already validated this immutable option value.
        this.Options = options;
        this.budgetStream = new WriteBudgetStream(stream, this.Options);
        this.Stream = this.budgetStream;
        this.PointerOrigin = this.Options.Origin;
        this.AddressingMode = this.Options.AddressingMode;
        this.BindingMode = this.Options.BindingMode;
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

    public PocoBindingMode BindingMode { get; }

    public WriteOptions Options { get; }

    public long PointerOrigin { get; }

    public int MaxNestingDepth { get; }

    public int StructureDepth { get; private set; }

    /// <summary>Gets or sets whether the next field starts at an already resolved exact byte address.</summary>
    public bool PositionIsResolvedTarget { get; set; }

    public Stream Stream { get; }

    public Dictionary<string, Expr> Variables { get; }

    public int CurrentBitOffset { get; set; }

    public string? CurrentBitfieldType { get; set; }

    public int CurrentBitfieldSize { get; set; }

    public int CurrentFieldAlignment { get; set; }

    public long NextPosition { get; set; }

    /// <summary>Copies every update choice before variable enumeration, payload access, or stream traversal.</summary>
    public static UpdateOptions SnapshotUpdateOptions(UpdateOptions? options)
    {
        UpdateOptions source = options ?? new UpdateOptions();
        return new UpdateOptions
        {
            AddressingMode = source.AddressingMode,
            BindingMode = source.BindingMode,
            MaxArrayElements = source.MaxArrayElements,
            MaxStringBytes = source.MaxStringBytes,
            MaxTotalBytesWritten = source.MaxTotalBytesWritten,
            MaxNestingDepth = source.MaxNestingDepth,
            Origin = source.Origin,
            AllowPointerDereference = source.AllowPointerDereference,
            RequireExistingPointerTarget = source.RequireExistingPointerTarget,
            ClearUnionStorage = source.ClearUnionStorage,
            MaxTraversalPointerDepth = source.MaxTraversalPointerDepth,
            MaxTraversalPointerTargetBytes = source.MaxTraversalPointerTargetBytes,
            MaxTraversalStringBytes = source.MaxTraversalStringBytes,
            MaxTraversalBytesRead = source.MaxTraversalBytesRead,
            MaxTraversalNestingDepth = source.MaxTraversalNestingDepth,
        };
    }

    /// <summary>Copies normal write choices while retaining update semantics when that derived value was supplied.</summary>
    public static WriteOptions SnapshotWriteOptions(WriteOptions? options)
    {
        if (options is UpdateOptions updateOptions)
        {
            return SnapshotUpdateOptions(updateOptions);
        }

        WriteOptions source = options ?? new WriteOptions();
        return new WriteOptions
        {
            AddressingMode = source.AddressingMode,
            BindingMode = source.BindingMode,
            MaxArrayElements = source.MaxArrayElements,
            MaxStringBytes = source.MaxStringBytes,
            MaxTotalBytesWritten = source.MaxTotalBytesWritten,
            MaxNestingDepth = source.MaxNestingDepth,
            Origin = source.Origin,
        };
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
            throw new CStructWriteLimitException("Maximum nested struct write depth exceeded.");
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
}
