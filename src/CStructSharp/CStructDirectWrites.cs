namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;
using CStructSharp.Writing;

/// <summary>
///     The direct write of a whole fixed-layout root into caller memory: <c>Serialize(span, "header", value)</c> for a
///     struct whose every member has a fixed offset. The write engine would build the per-operation state and a
///     budgeted destination over the span, stage the struct in a block through its static write plan, and copy the
///     block out; this path runs the same plan over the same kind of staging block without the destination and state.
/// </summary>
/// <remarks>
///     The direct path is taken only when it cannot behave differently from the write engine: no caller variables,
///     a value that is not null, valid limits that the plan is known to satisfy, and a destination large enough for
///     the whole struct. As in the write engine, the value is encoded completely before any byte reaches the
///     destination, so a value that fails to encode leaves the destination untouched.
/// </remarks>
public sealed partial class CStruct
{
    // Composites up to this size are staged on the stack; larger ones rent a pooled block.
    private const int StackStagingLimit = 512;

    /// <summary>Writes a whole fixed root directly when the conditions in the class remarks hold.</summary>
    /// <param name="destination">The caller's memory; the root is written at its start.</param>
    /// <param name="path">The requested path; only a bare root name qualifies.</param>
    /// <param name="data">The value to write, or a wrapper holding it under the root name.</param>
    /// <param name="variables">The caller's variables; any dictionary disqualifies the call.</param>
    /// <param name="options">The caller's write options.</param>
    /// <param name="written">The number of bytes written when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the root was written directly.</returns>
    /// <exception cref="CStructWriteException">The value cannot be encoded; the destination is unchanged.</exception>
    private bool TryWriteFixedRoot(
        Span<byte> destination,
        string path,
        object data,
        IReadOnlyDictionary<string, int>? variables,
        WriteOptions? options,
        out int written)
    {
        written = 0;
        if (variables is not null || data is null || options is UpdateOptions || options?.ExecutionPath is not (null or ExecutionPath.Fastest) ||
            !this.TryGetFixedRootPlan(path, out CompiledCompositeType? composite, out StaticReadPlan? plan) ||
            !plan.SupportsWrite || plan.Size > ReadBlock.Size || plan.Size > destination.Length)
        {
            return false;
        }

        // The write engine rejects invalid limits and a cancelled token before writing; leave both to it. With the
        // limits valid, the plan qualifies exactly when the engine's static plan would take the root.
        WriteOptions settings = options ?? WriteOptionSnapshots.DefaultWriteOptions;
        if (settings.CancellationToken.IsCancellationRequested ||
            settings.MaxArrayElements < 0 || settings.MaxStringBytes < 0 || settings.MaxTotalBytesWritten < 0 || settings.MaxNestingDepth <= 0 ||
            plan.NestingDepth > settings.MaxNestingDepth || plan.MaximumArrayCount > settings.MaxArrayElements ||
            (this.Aligned ? plan.ChargedAlignedBytes : plan.ChargedFieldBytes) > settings.MaxTotalBytesWritten || plan.Size > settings.MaxTotalBytesWritten)
        {
            return false;
        }

        // The same root preparation as the write engine: unwrap { root: value }, then turn a mapped-class instance
        // into a value of the composite's shape.
        object rootData = WriteDataBinding.NormalizeRootData(data, path);
        if (rootData is null)
        {
            return false;
        }

        byte[]? rented = null;
        Span<byte> block = plan.Size <= StackStagingLimit
                               ? stackalloc byte[plan.Size]
                               : (rented = ArrayPool<byte>.Shared.Rent(plan.Size)).AsSpan(0, plan.Size);
        try
        {
            block.Clear();

            // A layout-bound mapped class generated for this very struct (the same fingerprint) writes its properties
            // straight into the block. It declines anything it cannot write exactly as the path below would.
            if (!WriteDataBinding.IsMemberSource(rootData) &&
                MappedTypes.FindFixedWriter(rootData.GetType(), composite.Fingerprint) is { } direct && direct(rootData, block))
            {
                block.CopyTo(destination);
                written = plan.Size;
                return true;
            }

            rootData = WriteDataBinding.Materialize(rootData, composite);
            if (settings.UnknownMembers == UnknownMemberPolicy.Reject)
            {
                RejectUnknownMembers(composite, rootData);
            }

            // A declined direct write may have left bytes behind.
            block.Clear();
            var captures = default(NoStaticWriteCaptures);
            this.ExecuteStaticWritePlan(plan, composite, block, rootData, ref captures);
            block.CopyTo(destination);
        }
        catch (CStructException exception)
        {
            // The write engine attaches the path and the destination's position, which is still the start of the root.
            exception.AttachContext(path, 0);
            throw;
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        written = plan.Size;
        return true;
    }
}
