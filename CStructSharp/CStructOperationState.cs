namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CStructSharp.Structure;
using Pidgin;

/// <summary>Holds the private, per-operation state shared by the reader and writer parts of <see cref="CStruct"/>.</summary>
public partial class CStruct
{
    /// <summary>Stores the immutable read settings copied at an operation boundary without a heap allocation.</summary>
    private readonly record struct ReadOperationSettings(
        PointerAddressingMode AddressingMode,
        bool DereferencePointers,
        int MaxPointerDepth,
        long? MaxPointerTargetBytes,
        int MaxArrayElements,
        long MaxStringBytes,
        long MaxTotalBytesRead,
        int MaxNestingDepth,
        long Origin);

    /// <summary>Copies every read choice before variable enumeration, stream access, or another caller callback.</summary>
    private static ReadOperationSettings SnapshotReadOptions(ReadOptions? options)
    {
        if (options is null)
        {
            return new ReadOperationSettings(
                PointerAddressingMode.Absolute,
                true,
                64,
                null,
                1_000_000,
                16 * 1024 * 1024,
                64 * 1024 * 1024,
                256,
                0);
        }

        return new ReadOperationSettings(
            options.AddressingMode,
            options.DereferencePointers,
            options.MaxPointerDepth,
            options.MaxPointerTargetBytes,
            options.MaxArrayElements,
            options.MaxStringBytes,
            options.MaxTotalBytesRead,
            options.MaxNestingDepth,
            options.Origin);
    }

    /// <summary>Maps already-snapshotted update traversal choices into the same read operation settings.</summary>
    private static ReadOperationSettings SnapshotTraversalOptions(UpdateOptions options)
    {
        return new ReadOperationSettings(
            options.AddressingMode,
            options.AllowPointerDereference,
            options.MaxTraversalPointerDepth,
            options.MaxTraversalPointerTargetBytes,
            options.MaxArrayElements,
            options.MaxTraversalStringBytes,
            options.MaxTraversalBytesRead,
            options.MaxTraversalNestingDepth,
            options.Origin);
    }

    /// <summary>Copies every update choice before variable enumeration, payload access, or stream traversal.</summary>
    private static UpdateOptions SnapshotUpdateOptions(UpdateOptions? options)
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
    private static WriteOptions SnapshotWriteOptions(WriteOptions? options)
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
    private static void ValidateWriteOptions(WriteOptions options)
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

    /// <summary>Keeps stream position, variables, pointer safety data, and optional debug data for one read operation.</summary>
    private sealed class CStructOperationContext
    {
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

        public List<DebugData> DebugMapping { get; } = new();

        public HashSet<(long Address, string TypeName, int PointerDepth)> ActivePointerTargets { get; } = new();

        public long PointerOrigin { get; }

        public int MaxPointerDepth { get; }

        public long? MaxPointerTargetBytes { get; }

        public int MaxArrayElements { get; }

        public int MaxNestingDepth { get; }

        public int PointerDereferenceDepth { get; set; }

        public int StructureDepth { get; set; }

        public Stream Stream { get; }

        public Dictionary<string, Expr> Variables { get; }

        public int CurrentBitOffset { get; set; }

        public string? CurrentBitfieldType { get; set; }

        public int CurrentBitfieldSize { get; set; }

        public int CurrentFieldAlignment { get; set; }

        public bool Debug { get; set; }

        public long NextPosition { get; set; }

        /// <summary>Claims one nested-struct level and rejects input that exceeds the caller's recursion budget.</summary>
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
            CStructElement[] debugStack,
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
            this.DebugMapping.Add(
                                  new DebugData
                                  {
                                      CurPos = curPos,
                                      EndPos = endPos,
                                      DebugStack = debugStack,
                                      Value = value,
                                      Buffer = buffer.Select(o => (int)o).ToArray(),
                                      TypeName = fieldTypeName,
                                  });
        }
    }

    /// <summary>Keeps stream position, variables, options, and bitfield progress for one write operation.</summary>
    private sealed class CStructElementWriterState
    {
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
            this.Stream = new WriteBudgetStream(stream, this.Options);
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
            ((WriteBudgetStream)this.Stream).EnsureStringBytes(encodedByteCount);
        }

        /// <summary>Preflights and writes structural zero-fill without allocating the complete region.</summary>
        public void WriteZeroes(int count)
        {
            ((WriteBudgetStream)this.Stream).WriteZeroes(count);
        }
    }
}
