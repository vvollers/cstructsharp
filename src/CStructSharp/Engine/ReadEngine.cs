namespace CStructSharp.Engine;

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Values;

/// <summary>
///     The compiled engine's reader: executes a root's <see cref="ReadProgram"/> through a cursor and produces the
///     operation's result - its values (CLR types and member order), or its exception type, message, member, path and
///     offset - with the final position and read-budget charges the golden outcomes pin.
/// </summary>
/// <remarks>
///     <para>
///         <b>Dispatch.</b> <see cref="RunFrame{TCursor}"/> is written once for every cursor
///         (<c>where TCursor : struct, IReadCursor</c>), so .NET compiles a copy per cursor type with direct calls; each
///         frame is a <see langword="for"/> loop over the program's steps with one <see langword="switch"/> on the op code.
///     </para>
///     <para>
///         <b>Frames.</b> Every composite program runs in its own call, which fixes the order in which failures are
///         attributed and state is restored: the member context filter (<see cref="CStructException"/> names the
///         innermost member that was being read; filters run before inner <see langword="finally"/> blocks), the
///         nesting level a struct claims on entry and releases on exit (cancellation is observed at that entry), the
///         frame's selected conditional arms (fresh per struct-array element) and its conditional-scope locals.
///     </para>
///     <para>
///         <b>Fast paths.</b> A fully fixed composite is read through its static read plan and a <c>char[N]</c> through
///         one block under fixed conditions, because those paths charge the read budget differently (a plan charges its
///         whole extent, padding included) and the observable charges depend on which path runs.
///     </para>
/// </remarks>
internal static partial class ReadEngine
{
    /// <summary>The widest single value a scalar step reads (an <c>int128</c> or a UUID), the size of each frame's scratch buffer.</summary>
    private const int ScratchSize = 16;

    /// <summary>
    ///     Reads one whole root from a caller's stream: validates the source and settings, reads through a memory
    ///     cursor when the source is memory-backed (a pinned region or an exposed <see cref="MemoryStream"/> buffer)
    ///     and through the operation's <see cref="ReadBudgetStream"/> otherwise, writes the final position back to
    ///     <paramref name="stream"/>, and attaches the path and offset to a failure.
    /// </summary>
    /// <param name="layout">The layout the program belongs to.</param>
    /// <param name="stream">The caller's source, positioned at the root's first byte.</param>
    /// <param name="segments">The one-segment path that names the root, for failure context.</param>
    /// <param name="program">The root's program (<see cref="ReadProgramKind.Root"/>).</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder of a debug parse, whose program is <paramref name="program"/>; <see langword="null"/> for an ordinary read.</param>
    /// <param name="selected">
    ///     Whether the result is the value the root's name selects (a struct root read straight into its own value);
    ///     otherwise it is the root value that holds the root's value under its name.
    /// </param>
    /// <returns>The selected value, or the root value (empty for a <c>#define</c> root).</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or seek.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit is invalid.</exception>
    /// <exception cref="OperationCanceledException">The token is cancelled before or during the read.</exception>
    /// <exception cref="CStructException">The input cannot be read; the path and offset are attached.</exception>
    public static StructValue ReadRoot(CStruct layout, Stream stream, IReadOnlyList<PathSegment> segments, ReadProgram program, VariableSlots slots, in ReadOperationSettings options, DebugRecorder? debug, out bool selected)
    {
        ReadOperationSettings.Validate(stream, options);
        var state = new ReadEngineState(layout, slots, options, debug);
        try
        {
            if (MemoryReadCursor.TryCreate(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, out MemoryReadCursor memory))
            {
                return Run(ref memory, ref state, program, segments, stream, out selected);
            }

            var cursor = new StreamReadCursor(new ReadBudgetStream(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken));
            return Run(ref cursor, ref state, program, segments, stream, out selected);
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Reads one whole root from a pinned memory region, the input's byte 0 at <paramref name="region"/>: what
    ///     <see cref="ReadRoot(CStruct, Stream, IReadOnlyList{PathSegment}, ReadProgram, VariableSlots, in ReadOperationSettings, DebugRecorder, out bool)"/>
    ///     does over a read-only stream wrapping the region, without that stream: the settings are validated (a region
    ///     is always readable and seekable), and a failure reports the position the read reached.
    /// </summary>
    /// <param name="layout">The layout the program belongs to.</param>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The one-segment path that names the root, for failure context.</param>
    /// <param name="program">The root's program.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="debug">The recorder of a debug parse, whose program is <paramref name="program"/>; <see langword="null"/> for an ordinary read.</param>
    /// <param name="selected">Whether the result is the value the root's name selects rather than the root value.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The selected value, or the root value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A limit is invalid.</exception>
    /// <exception cref="OperationCanceledException">The token is cancelled before or during the read.</exception>
    /// <exception cref="CStructException">The input cannot be read; the path and offset are attached.</exception>
    public static unsafe StructValue ReadRoot(CStruct layout, byte* region, int length, IReadOnlyList<PathSegment> segments, ReadProgram program, VariableSlots slots, in ReadOperationSettings options, DebugRecorder? debug, out bool selected, out long position)
    {
        ReadOperationSettings.ValidateSettings(options);
        var state = new ReadEngineState(layout, slots, options, debug);
        try
        {
            var cursor = new MemoryReadCursor(region, length, 0, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken);
            StructValue value = Run(ref cursor, ref state, program, segments, null, out selected);
            position = cursor.Position;
            return value;
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Runs the root program, then - on success and failure alike - writes the final position back to the caller's
    ///     stream, and only then attaches the path and that position to a failure, so the attached offset is the
    ///     completed operation's final position.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The root's program.</param>
    /// <param name="segments">The root's path, for failure context.</param>
    /// <param name="stream">The caller's stream, whose position a failure reports; <see langword="null"/> for a memory region, whose cursor position is reported.</param>
    /// <param name="selected">Whether the result is the value the root's name selects rather than the root value.</param>
    /// <returns>The selected value, or the root value.</returns>
    private static StructValue Run<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, IReadOnlyList<PathSegment> segments, Stream? stream, out bool selected)
        where TCursor : struct, IReadCursor
    {
        try
        {
            try
            {
                return ReadRootValue(ref cursor, ref state, program, segments[0].Name, out selected);
            }
            finally
            {
                cursor.FlushPosition();
            }
        }
        catch (CStructException exception)
        {
            if (stream is null)
            {
                ExceptionContext.Attach(exception, segments, cursor.Position);
            }
            else
            {
                ExceptionContext.Attach(exception, segments, stream);
            }

            throw;
        }
    }

    /// <summary>
    ///     Executes a root program. A struct root requested by its own name is read straight into its value, which is
    ///     then the selected value; a one-member root value wrapping it would only be looked up by that name again.
    ///     Every other root (a field, a <c>#define</c>, a struct stored under another name) runs the root program into
    ///     the root value.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The root's program.</param>
    /// <param name="rootName">The name the root was requested by.</param>
    /// <param name="selected">Whether the result is the value the root's name selects rather than the root value.</param>
    /// <returns>The selected value, or the root value.</returns>
    private static StructValue ReadRootValue<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, string rootName, out bool selected)
        where TCursor : struct, IReadCursor
    {
        if (program.Steps is [{ Op: ReadOpCode.ReadRootStruct, } step,] && string.Equals(program.Name, rootName, StringComparison.Ordinal))
        {
            ReadProgram composite = program.Nested[step.A];
            var value = new StructValue(composite.Shape);
            ReadComposite(ref cursor, ref state, composite, value);
            selected = true;
            return value;
        }

        var root = new StructValue(program.Shape);
        RunFrame(ref cursor, ref state, program, root);
        selected = false;
        return root;
    }

    /// <summary>
    ///     Reads a struct into a value of its own: through the composite's static read plan when that plan applies (not
    ///     restricted to member-by-member reads (<see cref="ExecutionPath.NoFastPaths"/>), the plan within the nesting and array limits, and its whole extent present
    ///     within the byte budget), otherwise member by member inside one claimed nesting level.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the struct's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The struct's program (<see cref="ReadProgramKind.Composite"/>).</param>
    /// <param name="value">The struct's new, empty value, whose shape is the composite's own.</param>
    private static void ReadComposite<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, StructValue value)
        where TCursor : struct, IReadCursor
    {
        if (!state.NoFastPaths && program.Composite!.StaticPlan is { } plan && state.CoversPlan(plan))
        {
            byte[]? rented = null;
            try
            {
                if (TryStage(ref cursor, plan.Size, out ReadOnlySpan<byte> bytes, out rented))
                {
                    RunStaticPlan(ref cursor, ref state, plan, bytes, value);
                    return;
                }
            }
            finally
            {
                if (rented is not null)
                {
                    ArrayPool<byte>.Shared.Return(rented);
                }
            }
        }

        int pendingStart = state.PendingPointerCount;
        state.EnterStructure(ref cursor);
        try
        {
            RunFrame(ref cursor, ref state, program, value);
        }
        finally
        {
            // After a failure the struct's unfollowed pointers are dropped, so no later read follows them.
            state.DiscardPendingPointers(pendingStart);
            state.StructureDepth--;
        }
    }

    /// <summary>
    ///     Executes one program's steps in this call's frame. A failure inside a member of a struct (not of a root) is
    ///     attributed to that member by the exception filter, which runs before any inner <see langword="finally"/>, so
    ///     the innermost member wins; selection, scope and finishing steps belong to no member.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the program's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The program.</param>
    /// <param name="destination">The value the members are stored into: the struct's own, the parent's for a promoted member, or the root value.</param>
    private static void RunFrame<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, StructValue destination)
        where TCursor : struct, IReadCursor
    {
        ReadStep[] steps = program.Steps;
        int[] runs = program.ScalarRunLengths;

        // Alignment, offset assertions and the tail are measured from the struct's own first byte (D-18); a root program
        // places nothing.
        long start = program.Kind == ReadProgramKind.Root ? 0 : cursor.Position;

        // The frame's selected conditional arms live on its stack (a composite with more groups than fit allocates them);
        // its scope locals are a range of the operation's arena, given back on completion.
        Span<int> arms = program.GroupCount <= FrameArena.StackArmLimit ? stackalloc int[FrameArena.StackArmLimit] : new int[program.GroupCount];
        arms[..program.GroupCount].Fill(FrameArena.Undecided);
        int locals = program.Scope is { LocalCount: > 0, } scope ? state.TakeLocals(scope.LocalCount) : -1;
        Span<byte> scratch = stackalloc byte[ScratchSize];

        // The count register, the value the last read step produced (for its capture), whether the last array capture
        // was skipped because the array was empty (its publication is skipped with it), and a member start computed by
        // a placement step but not yet moved to because an offset assertion is checked first.
        int count = 0;
        object? last = null;
        bool captureSkipped = false;
        long placed = -1;
        int field = -1;

        // A struct with bitfields places its members through the runtime placement cursor, held on the stack;
        // a bitfield's placement sets the bit registers - its bit offset in the unit and the unit's size in bytes.
        PlacementCursor placer = program.UsesPlacementCursor
                                     ? new PlacementCursor(start, state.Layout.Aligned, state.Layout.BitfieldPacking, state.Layout.Compilation.HighBitFirst)
                                     : default;
        int bitOffset = 0;
        int unitSize = 0;

        // The deferred pointers of enclosing structs; the ones queued after them are this struct's to follow. A program
        // that defers none never reads it.
        int pendingStart = program.DefersPointers ? state.PendingPointerCount : 0;
        try
        {
            for (int index = 0; index < steps.Length; index++)
            {
                // Consecutive fixed-width scalars whose bytes are all in memory within the budget are taken as one block;
                // otherwise (a stream, a short input, a tight budget) each is read below, failing where it always has.
                if (runs[index] > 1 && cursor.TryReadSpanWithinBudget(program.ScalarRunBytes[index], out ReadOnlySpan<byte> run))
                {
                    last = StoreScalarRun(program, index, runs[index], run, destination);
                    index += runs[index] - 1;
                    continue;
                }

                ReadStep step = steps[index];
                field = step.Field;
                switch (step.Op)
                {
                case ReadOpCode.Seek:
                    if (ChecksOffsetNext(steps, index))
                    {
                        placed = checked(cursor.Position + step.A);
                    }
                    else
                    {
                        cursor.Skip(step.A);
                    }

                    break;

                case ReadOpCode.Align:
                    if (ChecksOffsetNext(steps, index))
                    {
                        placed = start + LayoutMath.AlignUp(cursor.Position - start, step.A);
                    }
                    else
                    {
                        cursor.Align(start, step.A);
                    }

                    break;

                case ReadOpCode.CheckOffset:
                    CheckOffset(ref cursor, program.Fields[field], step.A, start, ref placed);
                    break;

                case ReadOpCode.CheckFixedCount:
                    count = CheckCount(step.A, program.Fields[field], state.MaxArrayElements);
                    break;

                case ReadOpCode.EvaluateCount:
                    count = CheckCount(
                        state.Slots.Evaluate(program.Expressions[step.A], program.ExpressionContexts[step.A], ExpressionFailureDomain.Read),
                        program.Fields[field],
                        state.MaxArrayElements);
                    break;

                // Data-sized counts run the shared counting code over this cursor, from the placed start.
                case ReadOpCode.CountToEnd:
                    count = DynamicArrayExtent.CountToEnd(ref cursor, cursor.Position, step.A, state.MaxArrayElements, program.Fields[field].Name);
                    break;

                case ReadOpCode.CountTerminated:
                    count = DynamicArrayExtent.CountTerminated(ref cursor, cursor.Position, step.A, state.MaxArrayElements, program.Fields[field].Name);
                    break;

                case ReadOpCode.SkipTerminator:
                    cursor.Skip(step.A);
                    break;

                // Each multi-byte scalar decodes for its width and byte order directly (a value in memory is read in
                // place, otherwise through the codec's exact read); one-byte values keep the codec's shared boxes.
                case ReadOpCode.ReadInt16Le:
                    last = BinaryPrimitives.ReadInt16LittleEndian(cursor.ReadFixed(scratch[..2]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadInt16Be:
                    last = BinaryPrimitives.ReadInt16BigEndian(cursor.ReadFixed(scratch[..2]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadUInt16Le:
                    last = BinaryPrimitives.ReadUInt16LittleEndian(cursor.ReadFixed(scratch[..2]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadUInt16Be:
                    last = BinaryPrimitives.ReadUInt16BigEndian(cursor.ReadFixed(scratch[..2]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadInt32Le:
                    last = BinaryPrimitives.ReadInt32LittleEndian(cursor.ReadFixed(scratch[..4]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadInt32Be:
                    last = BinaryPrimitives.ReadInt32BigEndian(cursor.ReadFixed(scratch[..4]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadUInt32Le:
                    last = BinaryPrimitives.ReadUInt32LittleEndian(cursor.ReadFixed(scratch[..4]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadUInt32Be:
                    last = BinaryPrimitives.ReadUInt32BigEndian(cursor.ReadFixed(scratch[..4]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadInt64Le:
                    last = BinaryPrimitives.ReadInt64LittleEndian(cursor.ReadFixed(scratch[..8]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadInt64Be:
                    last = BinaryPrimitives.ReadInt64BigEndian(cursor.ReadFixed(scratch[..8]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadUInt64Le:
                    last = BinaryPrimitives.ReadUInt64LittleEndian(cursor.ReadFixed(scratch[..8]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadUInt64Be:
                    last = BinaryPrimitives.ReadUInt64BigEndian(cursor.ReadFixed(scratch[..8]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadFloat32Le:
                    last = BinaryPrimitives.ReadSingleLittleEndian(cursor.ReadFixed(scratch[..4]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadFloat32Be:
                    last = BinaryPrimitives.ReadSingleBigEndian(cursor.ReadFixed(scratch[..4]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadFloat64Le:
                    last = BinaryPrimitives.ReadDoubleLittleEndian(cursor.ReadFixed(scratch[..8]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadFloat64Be:
                    last = BinaryPrimitives.ReadDoubleBigEndian(cursor.ReadFixed(scratch[..8]));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadUInt8:
                case ReadOpCode.ReadInt8:
                case ReadOpCode.ReadBool:
                case ReadOpCode.ReadInt24Le:
                case ReadOpCode.ReadInt24Be:
                case ReadOpCode.ReadUInt24Le:
                case ReadOpCode.ReadUInt24Be:
                    {
                        PrimitiveCodec codec = program.Codecs[step.A].Primitive;
                        last = codec.ReadNumeric(cursor.ReadFixed(scratch[..codec.Size]));
                        Store(destination, program, field, last);
                        break;
                    }

                case ReadOpCode.ReadInt48:
                case ReadOpCode.ReadUInt48:
                case ReadOpCode.ReadInt128:
                case ReadOpCode.ReadUInt128:
                case ReadOpCode.ReadFloat16:
                case ReadOpCode.ReadFixedPoint:
                case ReadOpCode.ReadIdentifier:
                case ReadOpCode.ReadLeb128:
                case ReadOpCode.ReadCharacter:
                case ReadOpCode.ReadWideCharacter:
                case ReadOpCode.ReadTerminatedText:
                    last = ReadCodecValue(ref cursor, program.Codecs[step.A].Primitive, scratch);
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadEnum:
                    last = CStruct.CreateEnumValue(program.Enums[step.B], ReadCodecValue(ref cursor, program.Codecs[step.A].Primitive, scratch));
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadNumericArray:
                    Store(destination, program, field, ReadNumericArray(ref cursor, program.Codecs[step.A].Primitive, count));
                    break;

                case ReadOpCode.ReadNumericElements:
                    Store(destination, program, field, ReadNumericElements(ref cursor, program.Codecs[step.A].Primitive, count, scratch));
                    break;

                case ReadOpCode.ReadCodecArray:
                    Store(destination, program, field, ReadCodecArray(ref cursor, program.Codecs[step.A].Primitive, count, scratch));
                    break;

                case ReadOpCode.ReadCharArray:
                    Store(destination, program, field, ReadCharArray(ref cursor, ref state, program, field, step.A, count, scratch));
                    break;

                case ReadOpCode.ReadWideCharArray:
                    Store(destination, program, field, ReadWideCharArray(ref cursor, ref state, program.Fields[field], program.Codecs[step.A].Primitive, count, scratch));
                    break;

                case ReadOpCode.ReadCustom:
                    last = ReadCustomValue(ref cursor, ref state, program, field, step.A);
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadCustomArray:
                    Store(destination, program, field, ReadCustomArray(ref cursor, ref state, program, field, step.A, count));
                    break;

                case ReadOpCode.ReadNumericList:
                    Store(destination, program, field, ReadNumericList(ref cursor, program.Codecs[step.A].Primitive, count));
                    break;

                case ReadOpCode.ReadNumericElementList:
                    Store(destination, program, field, ReadNumericElementList(ref cursor, program.Codecs[step.A].Primitive, count, scratch));
                    break;

                case ReadOpCode.ReadStructElements:
                    Store(destination, program, field, ReadStructElements(ref cursor, ref state, program.Nested[step.A], count));
                    break;

                case ReadOpCode.ReadCharTable:
                    Store(destination, program, field, ReadCharTable(ref cursor, ref state, program.Fields[field], program.Codecs[step.A].Primitive, count, scratch));
                    break;

                case ReadOpCode.ReshapeTable:
                    ReshapeTable(destination, program, field);
                    break;

                case ReadOpCode.ReadBoundedText:
                    Store(destination, program, field, state.FixedText(cursor.ReadBoundedText(count, program.Fields[field].TypeSpelling)));
                    break;

                case ReadOpCode.ReadEnumArray:
                    Store(destination, program, field, ReadEnumArray(ref cursor, program.Codecs[step.A].Primitive, program.Enums[step.B], count, scratch));
                    break;

                case ReadOpCode.SkipElements:
                    SkipElements(ref cursor, program.Codecs[step.A].Primitive, count, scratch);
                    break;

                case ReadOpCode.ReadStruct:
                    {
                        ReadProgram nested = program.Nested[step.A];
                        string? outer = state.QualifiedPrefix;
                        if (step.B >= 0)
                        {
                            state.QualifiedPrefix = outer is null ? program.Prefixes[step.B] : outer + program.Prefixes[step.B];
                        }

                        var value = new StructValue(nested.Shape);
                        ReadComposite(ref cursor, ref state, nested, value);
                        state.QualifiedPrefix = outer;
                        Store(destination, program, field, value);
                        break;
                    }

                case ReadOpCode.ReadPromotedStruct:
                    // An anonymous member's values belong to this struct and it claims no nesting level, but it observes
                    // cancellation on entry like every composite.
                    cursor.ThrowIfCancellationRequested();
                    RunFrame(ref cursor, ref state, program.Nested[step.A], destination);
                    break;

                case ReadOpCode.ReadStructArray:
                    Store(destination, program, field, ReadStructArray(ref cursor, ref state, program, field, program.Nested[step.A], count));
                    break;

                case ReadOpCode.CaptureInteger:
                case ReadOpCode.CaptureUInt128:
                case ReadOpCode.CaptureEnum:
                    state.Slots.Set(step.A, LayoutVariableCapture.ToSlotValue(last));
                    captureSkipped = false;
                    break;

                case ReadOpCode.CaptureNotANumber:
                    state.Slots.Set(step.A, SlotValue.FromUnusable(program.Unusables[step.B]));
                    captureSkipped = false;
                    break;

                case ReadOpCode.CaptureNotANumberIfElements:
                    // A capture is taken per element, so an empty array captures (and publishes) nothing.
                    captureSkipped = count == 0;
                    if (!captureSkipped)
                    {
                        state.Slots.Set(step.A, SlotValue.FromUnusable(program.Unusables[step.B]));
                    }

                    break;

                case ReadOpCode.PublishQualified:
                    if (!captureSkipped)
                    {
                        state.PublishQualified(program.QualifiedTargets[step.B], state.Slots.Get(step.A));
                    }

                    break;

                case ReadOpCode.EnterConditionalScope:
                    foreach (int slot in program.Scope!.ClearedSlots)
                    {
                        state.Slots.Set(slot, SlotValue.Undefined);
                    }

                    break;

                case ReadOpCode.SelectArm:
                    if (state.SelectedArm(program, program.Branches[step.A], arms) != program.Branches[step.A].Arm)
                    {
                        // The loop's increment lands on the first step after the member.
                        index = step.B - 1;
                    }

                    break;

                case ReadOpCode.CompleteMember:
                    state.CompleteMember(program.Scope!, field, locals);
                    break;

                case ReadOpCode.FinishComposite:
                    if (step.A >= 0)
                    {
                        cursor.Skip(step.A);
                    }
                    else
                    {
                        cursor.Align(start, step.B);
                    }

                    break;

                case ReadOpCode.PlaceMember:
                    cursor.Position = CompositeFieldPlacementCursor.AdvanceToField(ref placer, program.Fields[field]).FieldStart;
                    break;

                case ReadOpCode.PlaceBitfield:
                    {
                        (long unitStart, bitOffset, unitSize) = CompositeFieldPlacementCursor.AdvanceToField(ref placer, program.Fields[field]);
                        cursor.Position = unitStart;
                        break;
                    }

                case ReadOpCode.PlaceSeparator:
                    cursor.Position = CompositeFieldPlacementCursor.AdvanceToField(ref placer, program.Fields[field]).FieldStart;
                    break;

                case ReadOpCode.CompletePlacement:
                    placer.CompleteField(cursor.Position);
                    break;

                case ReadOpCode.FinishPlaced:
                    cursor.Position = placer.Finish(step.B)!.Value;
                    break;

                case ReadOpCode.OpenBitfieldUnit:
                    bitOffset = 0;
                    unitSize = program.Fields[field].BitStorageSize!.Value;
                    break;

                case ReadOpCode.OpenSeededBitfieldUnit:
                    // A selected bitfield reads the unit its struct placed, as the path resolver measured it.
                    bitOffset = state.SeededBitOffset;
                    unitSize = state.SeededUnitSize;
                    break;

                case ReadOpCode.ReadBitfield:
                    last = ReadBitfield(ref cursor, ref state, program.Fields[field], program.Codecs[step.A].Primitive, ref bitOffset, unitSize, scratch);
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadPointer:
                    last = ReadPointerField(ref cursor, ref state, program.PointerTargets[step.A], step.B == 1, scratch);
                    Store(destination, program, field, last);
                    break;

                case ReadOpCode.ReadPointerArray:
                    {
                        var pointers = new List<object?>(count);
                        for (int element = 0; element < count; element++)
                        {
                            pointers.Add(ReadPointerField(ref cursor, ref state, program.PointerTargets[step.A], step.B == 1, scratch));
                        }

                        Store(destination, program, field, pointers);
                        break;
                    }

                case ReadOpCode.FollowPendingPointers:
                    FollowPendingPointers(ref cursor, ref state, pendingStart, scratch);
                    break;

                case ReadOpCode.RewindToUnionStart:
                    cursor.Position = start;
                    break;

                case ReadOpCode.RestoreUnionSlots:
                    state.RestoreUnionSlots();
                    break;

                case ReadOpCode.ReadUnion:
                    {
                        string? outer = state.QualifiedPrefix;
                        if (step.B >= 0)
                        {
                            state.QualifiedPrefix = outer is null ? program.Prefixes[step.B] : outer + program.Prefixes[step.B];
                        }

                        UnionValue union = ReadUnion(ref cursor, ref state, program.Nested[step.A], promoted: false);
                        state.QualifiedPrefix = outer;
                        Store(destination, program, field, union);
                        break;
                    }

                case ReadOpCode.ReadPromotedUnion:
                    {
                        // The views belong to this value: copied in by name, in the union's member order.
                        UnionValue union = ReadUnion(ref cursor, ref state, program.Nested[step.A], promoted: true);
                        IDictionary<string, object?> members = destination;
                        foreach (KeyValuePair<string, object?> member in union.Members)
                        {
                            members[member.Key] = member.Value;
                        }

                        break;
                    }

                case ReadOpCode.ReadUnionArray:
                    Store(destination, program, field, ReadUnionArray(ref cursor, ref state, program.Nested[step.A], count));
                    break;

                case ReadOpCode.ReadRootUnion:
                    {
                        // Unlike a struct, a root union's value is attached only once it is read.
                        UnionValue union = ReadUnion(ref cursor, ref state, program.Nested[step.A], promoted: false);
                        _ = program.Shape.TryGetIndex(program.Name, out int slot);
                        destination.StoreSlot(slot, union);
                        break;
                    }

                case ReadOpCode.ReadRootStruct:
                    {
                        ReadProgram nested = program.Nested[step.A];
                        var value = new StructValue(nested.Shape);

                        // A root's value is attached before its members are read; the compiler checked the slot.
                        _ = program.Shape.TryGetIndex(program.Name, out int slot);
                        destination.StoreSlot(slot, value);
                        ReadComposite(ref cursor, ref state, nested, value);
                        break;
                    }

                case ReadOpCode.EvaluateDefinition:
                    {
                        Int128 value = state.Slots.Evaluate(program.Expressions[step.A], program.ExpressionContexts[step.A], ExpressionFailureDomain.Read);
                        if (step.B >= 0)
                        {
                            state.Slots.Set(step.B, SlotValue.FromLiteral(value));
                        }

                        break;
                    }

                case ReadOpCode.FailNoReader:
                    throw new InvalidOperationException(ReadFailures.NoValueHandler(program.Fields[field].DisplayTypeSpelling));

                case ReadOpCode.FailElementCountOverflow:
                    // The element count is the product of the dimensions, which overflows before any limit is checked.
                    throw new OverflowException();

                default:
                    // Only a debug program holds another code; its handler rejects anything else.
                    last = RunDebugStep(ref cursor, ref state, program, step, destination, last, count, unitSize, scratch);
                    break;
                }
            }

            if (locals >= 0)
            {
                state.ReleaseLocals(locals);
            }
        }
        catch (CStructException exception) when (field >= 0 && program.NotesMembers && NoteMember(exception, program.Fields[field]))
        {
            // Never entered: the filter records the member and lets the exception propagate.
            throw;
        }
    }

    /// <summary>
    ///     Decodes and stores a run of fixed-width scalars from their bytes, taken as one block, exactly as reading them
    ///     one by one would store them.
    /// </summary>
    /// <param name="program">The program.</param>
    /// <param name="first">The index of the run's first step.</param>
    /// <param name="length">The number of steps in the run.</param>
    /// <param name="bytes">The run's bytes, consumed and charged.</param>
    /// <param name="destination">The value the members are stored into.</param>
    /// <returns>The last value read, for a capture that follows the run.</returns>
    private static object StoreScalarRun(ReadProgram program, int first, int length, ReadOnlySpan<byte> bytes, StructValue destination)
    {
        object value = null!;
        int offset = 0;
        for (int index = first; index < first + length; index++)
        {
            ReadStep step = program.Steps[index];
            PrimitiveCodec codec = program.Codecs[step.A].Primitive;
            ReadOnlySpan<byte> element = bytes.Slice(offset, codec.Size);
            value = step.Op switch
            {
                ReadOpCode.ReadInt16Le => BinaryPrimitives.ReadInt16LittleEndian(element),
                ReadOpCode.ReadInt16Be => BinaryPrimitives.ReadInt16BigEndian(element),
                ReadOpCode.ReadUInt16Le => BinaryPrimitives.ReadUInt16LittleEndian(element),
                ReadOpCode.ReadUInt16Be => BinaryPrimitives.ReadUInt16BigEndian(element),
                ReadOpCode.ReadInt32Le => BinaryPrimitives.ReadInt32LittleEndian(element),
                ReadOpCode.ReadInt32Be => BinaryPrimitives.ReadInt32BigEndian(element),
                ReadOpCode.ReadUInt32Le => BinaryPrimitives.ReadUInt32LittleEndian(element),
                ReadOpCode.ReadUInt32Be => BinaryPrimitives.ReadUInt32BigEndian(element),
                ReadOpCode.ReadInt64Le => BinaryPrimitives.ReadInt64LittleEndian(element),
                ReadOpCode.ReadInt64Be => BinaryPrimitives.ReadInt64BigEndian(element),
                ReadOpCode.ReadUInt64Le => BinaryPrimitives.ReadUInt64LittleEndian(element),
                ReadOpCode.ReadUInt64Be => BinaryPrimitives.ReadUInt64BigEndian(element),
                ReadOpCode.ReadFloat32Le => BinaryPrimitives.ReadSingleLittleEndian(element),
                ReadOpCode.ReadFloat32Be => BinaryPrimitives.ReadSingleBigEndian(element),
                ReadOpCode.ReadFloat64Le => BinaryPrimitives.ReadDoubleLittleEndian(element),
                ReadOpCode.ReadFloat64Be => BinaryPrimitives.ReadDoubleBigEndian(element),

                // One-byte values (shared boxes) and 24-bit integers decode through the codec.
                _ => codec.ReadNumeric(element),
            };
            Store(destination, program, step.Field, value);
            offset += codec.Size;
        }

        return value;
    }

    /// <summary>Records the member a failure happened in, as an exception filter; never catches.</summary>
    /// <param name="exception">The failure.</param>
    /// <param name="member">The member being read.</param>
    /// <returns><see langword="false"/>, so the exception propagates.</returns>
    private static bool NoteMember(CStructException exception, CompiledField member) => exception.NoteMember(member.Name, member.DisplayTypeSpelling);

    /// <summary>Whether the step after <paramref name="index"/> checks the same member's offset assertion, which must see the placed start before the position moves.</summary>
    /// <param name="steps">The program's steps.</param>
    /// <param name="index">The placement step's index.</param>
    /// <returns>Whether an offset assertion of the same member follows.</returns>
    private static bool ChecksOffsetNext(ReadStep[] steps, int index)
        => index + 1 < steps.Length && steps[index + 1].Op == ReadOpCode.CheckOffset && steps[index + 1].Field == steps[index].Field;

    /// <summary>
    ///     Checks a member's <c>@N</c> assertion against its start, then moves there. The start is computed, the
    ///     assertion checked, and only then the position set, so a failing assertion leaves the position before the
    ///     padding and wins over a start that lies past the input.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="member">The asserted member.</param>
    /// <param name="asserted">The asserted offset from the struct's first byte.</param>
    /// <param name="start">The struct's first byte.</param>
    /// <param name="placed">The member start a placement step computed, or -1 when the member needed no padding; reset to -1.</param>
    /// <exception cref="CStructLayoutException">The member does not start at the asserted offset.</exception>
    private static void CheckOffset<TCursor>(ref TCursor cursor, CompiledField member, int asserted, long start, ref long placed)
        where TCursor : struct, IReadCursor
    {
        long memberStart = placed >= 0 ? placed : cursor.Position;
        if (OffsetAssertion.Check(member.Name, asserted, memberStart - start) is { } failure)
        {
            throw new CStructLayoutException(failure);
        }

        if (placed >= 0)
        {
            placed = -1;
            cursor.Position = memberStart;
        }
    }

    /// <summary>
    ///     Checks an element count: a negative count is a read failure naming the member, a count past
    ///     <c>MaxArrayElements</c> a limit failure naming the exact value.
    /// </summary>
    /// <param name="count">The count, in the expression domain.</param>
    /// <param name="member">The array member.</param>
    /// <param name="maximum">The operation's element limit.</param>
    /// <returns>The count as an <see cref="int"/>.</returns>
    /// <exception cref="CStructReadException">The count is negative.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds <paramref name="maximum"/>.</exception>
    private static int CheckCount(Int128 count, CompiledField member, int maximum)
    {
        if (count < 0)
        {
            throw new CStructReadException(LayoutFailures.NegativeArrayLength(member.Name));
        }

        if (count > maximum)
        {
            throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(count, maximum));
        }

        return (int)count;
    }

    /// <summary>Stores a member's value in its slot of the destination; a member without a slot (unnamed padding) is dropped.</summary>
    /// <param name="destination">The value receiving the member.</param>
    /// <param name="program">The program the member belongs to.</param>
    /// <param name="field">The member's index.</param>
    /// <param name="value">The value.</param>
    private static void Store(StructValue destination, ReadProgram program, int field, object? value)
    {
        int slot = program.GetShapeSlot(field);
        if (slot >= 0)
        {
            destination.StoreSlot(slot, value);
        }
    }

    /// <summary>
    ///     Takes <paramref name="count"/> bytes as one staged block: in place from a memory source within the budget,
    ///     else - for an extent up to one 64 KiB block - into a rented block from a seekable stream that provably holds
    ///     them within the budget; otherwise nothing is consumed or charged.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="count">The number of bytes.</param>
    /// <param name="bytes">The staged bytes, valid until <paramref name="rented"/> is returned.</param>
    /// <param name="rented">The rented block the caller returns to <see cref="ArrayPool{T}.Shared"/>, or <see langword="null"/>.</param>
    /// <returns>Whether the bytes were taken.</returns>
    private static bool TryStage<TCursor>(ref TCursor cursor, int count, out ReadOnlySpan<byte> bytes, out byte[]? rented)
        where TCursor : struct, IReadCursor
    {
        rented = null;
        if (cursor.TryReadSpanWithinBudget(count, out bytes))
        {
            return true;
        }

        if (count <= ReadBlock.Size)
        {
            byte[]? block = ArrayPool<byte>.Shared.Rent(count);
            try
            {
                if (cursor.TryReadBlockWithinBudget(block.AsSpan(0, count)))
                {
                    bytes = block.AsSpan(0, count);
                    rented = block;
                    block = null;
                    return true;
                }
            }
            finally
            {
                if (block is not null)
                {
                    ArrayPool<byte>.Shared.Return(block);
                }
            }
        }

        bytes = default;
        return false;
    }
}
