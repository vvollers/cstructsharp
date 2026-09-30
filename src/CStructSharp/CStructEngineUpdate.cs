namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Syntax;

/// <summary>
///     The update of the compiled engine: the path resolved by the engine's path resolver, the replacement staged by its
///     writer, and the layout captured and compared by its debug programs, all over the operation's variable slots.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>The failure of an update whose terminated value changes its encoded length and so moves later fields.</summary>
    private const string UpdateExtentChanged = "Update changes the extent of a terminated value and would move the fields that follow; the replacement must have the same encoded length, or serialize a new buffer instead.";

    /// <summary>The failure of an update that changes which conditional members are active or where they lie.</summary>
    private const string UpdateLayoutChanged = "Update changes the active conditional storage layout; serialize a new buffer instead.";

    /// <summary>
    ///     Updates one value in a stream with the compiled engine, in this order: the settings are validated and the stream wrapped in the traversal budget; a root with conditional
    ///     members has its layout captured first; the path is resolved (the walk's captures stay in the slots the write then
    ///     evaluates against); the replacement is written into sparse staging over the budget (a pointer's address as an
    ///     address, the root through its write program, anything else through the program of the storage it selects, a
    ///     bitfield seeded with its placed unit); a terminated target captures the layout after the walk; the layout is
    ///     captured again from the staged copy and compared; only then are the staged bytes committed. The stream's position
    ///     is restored whatever happens.
    /// </summary>
    /// <param name="stream">The caller's readable, writable, seekable stream; its position is the operation origin.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="value">The replacement value, not normalized (an update writes exactly what it is given).</param>
    /// <param name="slots">The operation's initialized slots; they receive the walk's and the write's captures. The caller disposes them.</param>
    /// <param name="options">The operation's snapshotted and validated options.</param>
    /// <exception cref="CStructException">The update fails; the path and the stream's position are attached.</exception>
    private void UpdateWithEngine(Stream stream, IReadOnlyList<PathSegment> segments, object value, VariableSlots slots, UpdateOptions options)
    {
        string rootName = segments[0].Name;
        ReadOperationSettings readOptions = ReadOperationSettings.SnapshotTraversalOptions(options);
        ReadOperationSettings.Validate(stream, readOptions);
        var budget = new ReadBudgetStream(stream, readOptions.MaxStringBytes, readOptions.MaxTotalBytesRead, readOptions.CancellationToken);
        var cursor = new StreamReadCursor(budget);
        var readState = new ReadEngineState(this, slots, readOptions, null);

        // Update promises not to leave the caller's stream somewhere unexpected, even if writing fails.
        long originalPosition = budget.Position;
        Exception? primaryException = null;
        VariableSlots layoutSlots = default;
        bool capturesLayout = false;
        try
        {
            ReadProgram? layoutProgram = null;
            (string Path, long Start, long End)[]? originalLayout = null;
            bool variableExtentTarget = false;
            if (this.HasConditionalLayout(rootName))
            {
                // The comparison reads start from the variables the operation started with.
                layoutSlots = slots.Clone();
                capturesLayout = true;
                layoutProgram = this.compilation.GetRootDebugReadProgram(rootName)!;
                originalLayout = this.CaptureLayoutFrom(budget, originalPosition, layoutProgram, layoutSlots, readOptions);
                budget.Position = originalPosition;
            }

            ResolvedPath target;
            try
            {
                target = TargetResolver.Resolve(ref cursor, ref readState, segments, null);
            }
            catch (CStructException exception)
            {
                ExceptionContext.Attach(exception, segments, budget);
                throw;
            }

            // From here on, the writer uses the exact absolute target coordinates without touching the caller's bytes.
            budget.Position = target.Address;
            if (target.Kind == ResolvedTargetKind.PointerValue && options.RequireExistingPointerTarget && target.Address == 0)
            {
                throw new CStructReadException("Cannot update a null pointer target when RequireExistingPointerTarget is enabled.");
            }

            // The staging's baseline reads share the traversal budget; the writer's budget wraps the staging after the
            // token is observed.
            using var staging = new SparseUpdateStream(budget, target.Address);
            options.CancellationToken.ThrowIfCancellationRequested();
            var writeBudget = new WriteBudgetStream(staging, options);
            if (target.Kind == ResolvedTargetKind.PointerAddress)
            {
                // .address changes only the pointer number; it never touches the pointed-to data.
                this.WritePointerAddress(writeBudget, CStructPointerArithmetic.ConvertTargetAddress(value), options);
            }
            else if (target.Kind == ResolvedTargetKind.Root)
            {
                WriteEngine.WriteUpdateTarget(this, this.compilation.GetRootWriteProgram(rootName).Program!, writeBudget, value, slots, options, 0, 0);
            }
            else
            {
                WriteProgram program = this.UpdateTargetProgram(target);
                CompiledField writable = program.Fields[0];
                if (originalLayout is null && (writable.Codec.IsTerminatedText || writable.HasTerminatedCodec || writable.Array.Kind == CompiledArrayKind.Terminated))
                {
                    // A terminated value has no fixed extent: a replacement of another encoded length would move every
                    // later field, so the whole layout is captured - from the variables the walk left - and compared.
                    layoutSlots = slots.Clone();
                    capturesLayout = true;
                    layoutProgram = this.compilation.GetRootDebugReadProgram(rootName)!;
                    originalLayout = this.CaptureLayoutFrom(budget, originalPosition, layoutProgram, layoutSlots, readOptions);
                    variableExtentTarget = true;
                    budget.Position = target.Address;
                }

                WriteEngine.WriteUpdateTarget(this, program, writeBudget, value, slots, options, target.BitOffset, target.BitStorageSize);
            }

            // The caller sees writes only after every library-detectable writer failure has been ruled out.
            if (originalLayout is not null)
            {
                (string Path, long Start, long End)[] changedLayout;
                try
                {
                    changedLayout = this.CaptureLayoutFrom(staging, originalPosition, layoutProgram!, layoutSlots, readOptions);
                }
                catch (CStructException inner) when (variableExtentTarget)
                {
                    // The moved fields no longer read at all (a later field ran past the end, a pointer went astray).
                    throw new CStructWriteException(UpdateExtentChanged, inner);
                }

                if (!originalLayout.SequenceEqual(changedLayout))
                {
                    throw new CStructWriteException(variableExtentTarget ? UpdateExtentChanged : UpdateLayoutChanged);
                }
            }

            staging.CommitTo(stream);
        }
        catch (Exception exception)
        {
            primaryException = exception;
            if (exception is CStructException domainException)
            {
                ExceptionContext.Attach(domainException, segments, stream);
            }

            throw;
        }
        finally
        {
            if (capturesLayout)
            {
                layoutSlots.Dispose();
            }

            try
            {
                // Keep the position contract on success, validation errors, and physical commit errors alike.
                budget.Position = originalPosition;
                budget.FlushPosition();
            }
            catch (Exception) when (primaryException is not null)
            {
                // A broken destination may reject restoration after a failed commit; preserve the primary failure.
            }
            catch (CStructException restorationException)
            {
                ExceptionContext.Attach(restorationException, segments, stream);
                throw;
            }
            finally
            {
                readState.Release();
            }
        }
    }

    /// <summary>Whether a root reaches a conditional member, which an update must capture around its change.</summary>
    /// <param name="rootName">The declared root name.</param>
    /// <returns>Whether any type reachable from the root has an <c>if</c> or <c>switch</c> member.</returns>
    private bool HasConditionalLayout(string rootName)
        => this.compilation.CompiledModel.Symbols[rootName].Symbol.Definition is CompiledCompositeType composite && composite.ReachesConditionalMembers;

    /// <summary>
    ///     Captures a root's layout with its debug program from a copy of <paramref name="snapshot"/>, so every
    ///     capture starts from the same variables.
    /// </summary>
    /// <param name="stream">The data: the traversal budget over the original, or the staged copy.</param>
    /// <param name="origin">The root's position.</param>
    /// <param name="program">The root's debug program.</param>
    /// <param name="snapshot">The variables every capture starts from; not changed.</param>
    /// <param name="options">The traversal's read settings.</param>
    /// <returns>Each value's path and byte range, then each conditional member's name, position and selection.</returns>
    private (string Path, long Start, long End)[] CaptureLayoutFrom(Stream stream, long origin, ReadProgram program, VariableSlots snapshot, in ReadOperationSettings options)
    {
        VariableSlots copy = snapshot.Clone();
        try
        {
            return ReadEngine.CaptureLayout(this, stream, origin, program, copy, options);
        }
        finally
        {
            copy.Dispose();
        }
    }

    /// <summary>
    ///     The program that writes what a resolved update path selects on its own: a member or element through the member's
    ///     program, a pointer's <c>.value</c> storage through its pointed-to program. The selector found the same program
    ///     eligible from the path's shape, so the lookup cannot fail.
    /// </summary>
    /// <param name="target">The resolved member, element or pointed-to storage.</param>
    /// <returns>The program.</returns>
    /// <exception cref="InvalidOperationException">The program is not eligible, which the selector rules out.</exception>
    private WriteProgram UpdateTargetProgram(in ResolvedPath target)
    {
        WriteProgramCache programs = this.compilation.SlotTable.WritePrograms;
        WriteProgramOutcome outcome = target.Kind == ResolvedTargetKind.PointerValue
                                          ? programs.GetPointee(this.compilation, target.Declared!, target.Indexes, target.RemainingPointerDepth)
                                          : programs.GetMember(this.compilation, target.Declared!, target.Indexes);
        return outcome.Program ?? throw new InvalidOperationException("The compiled engine has no program for the update target: " + outcome.Reason);
    }
}
