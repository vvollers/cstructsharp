namespace CStructSharp.Engine;

using System;
using System.Buffers;
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
///     The compiled engine's reader: executes a root's <see cref="ReadProgram"/> through a cursor and produces exactly
///     what the interpreter's reader produces for the same operation - the same values (CLR types and member order), the
///     same exception type, message, member, path and offset, the same final position and the same read-budget charges.
/// </summary>
/// <remarks>
///     <para>
///         <b>Dispatch.</b> <see cref="RunFrame{TCursor}"/> is written once for every cursor
///         (<c>where TCursor : struct, IReadCursor</c>), so .NET compiles a copy per cursor type with direct calls; each
///         frame is a <see langword="for"/> loop over the program's steps with one <see langword="switch"/> on the op code.
///     </para>
///     <para>
///         <b>Frames.</b> Every composite program runs in its own call, as the interpreter reads every composite in its
///         own call, so the order in which failures are attributed and state is restored is the interpreter's: the member
///         context filter (<see cref="CStructException"/> names the innermost member that was being read; filters run
///         before inner <see langword="finally"/> blocks), the nesting level a struct claims on entry and releases on exit
///         (cancellation is observed at that entry), the frame's selected conditional arms (fresh per struct-array
///         element) and its conditional-scope locals.
///     </para>
///     <para>
///         <b>Fast paths.</b> Where the interpreter reads a fully fixed composite through its static read plan or a
///         <c>char[N]</c> through one block, the engine takes the same path under the same conditions, because those
///         paths charge the read budget differently (a plan charges its whole extent, padding included).
///     </para>
/// </remarks>
internal static partial class ReadEngine
{
    /// <summary>The widest single value a scalar step reads (an <c>int128</c> or a UUID), the size of each frame's scratch buffer.</summary>
    private const int ScratchSize = 16;

    /// <summary>The selected-arm value of a conditional group whose selector this frame has not evaluated yet.</summary>
    private const int Undecided = int.MinValue;

    /// <summary>
    ///     Reads one whole root: validates the source and settings as the interpreter's operation state does, reads through
    ///     a memory cursor when the interpreter would read the source from memory (a pinned region or an exposed
    ///     <see cref="MemoryStream"/> buffer) and through the operation's <see cref="ReadBudgetStream"/> otherwise, writes
    ///     the final position back to <paramref name="stream"/>, and attaches the path and offset to a failure.
    /// </summary>
    /// <param name="layout">The layout the program belongs to.</param>
    /// <param name="stream">The caller's source, positioned at the root's first byte.</param>
    /// <param name="segments">The one-segment path that names the root, for failure context.</param>
    /// <param name="program">The root's program (<see cref="ReadProgramKind.Root"/>).</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <returns>The root value, holding the root's value under its name (empty for a <c>#define</c> root).</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or seek.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit is invalid.</exception>
    /// <exception cref="OperationCanceledException">The token is cancelled before or during the read.</exception>
    /// <exception cref="CStructException">The input cannot be read; the path and offset are attached.</exception>
    public static StructValue ReadRoot(CStruct layout, Stream stream, IReadOnlyList<PathSegment> segments, ReadProgram program, VariableSlots slots, in ReadOperationSettings options)
    {
        CStructOperationContext.Validate(stream, options);
        var state = new ReadEngineState(layout, slots, options);
        var root = new StructValue(program.Shape);
        if (MemoryReadCursor.TryCreate(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, out MemoryReadCursor memory))
        {
            Run(ref memory, ref state, program, root, segments, stream);
        }
        else
        {
            var cursor = new StreamReadCursor(new ReadBudgetStream(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken));
            Run(ref cursor, ref state, program, root, segments, stream);
        }

        return root;
    }

    /// <summary>
    ///     Runs the root program, then - on success and failure alike - writes the final position back to the caller's
    ///     stream, and only then attaches the path and that position to a failure, as the interpreter completes its
    ///     operation state before it attaches the context.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The root's program.</param>
    /// <param name="root">The root value.</param>
    /// <param name="segments">The root's path, for failure context.</param>
    /// <param name="stream">The caller's stream, whose position a failure reports.</param>
    private static void Run<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, StructValue root, IReadOnlyList<PathSegment> segments, Stream stream)
        where TCursor : struct, IReadCursor
    {
        try
        {
            try
            {
                RunFrame(ref cursor, ref state, program, root);
            }
            finally
            {
                cursor.FlushPosition();
            }
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
    }

    /// <summary>
    ///     Reads a struct into a value of its own, as the interpreter's <c>ReadCompiledStructInto</c> does: through the
    ///     composite's static read plan when the interpreter would take it (not restricted to the general path, the plan
    ///     within the nesting and array limits, and its whole extent present within the byte budget), otherwise member by
    ///     member inside one claimed nesting level.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the struct's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The struct's program (<see cref="ReadProgramKind.Composite"/>).</param>
    /// <param name="value">The struct's new, empty value, whose shape is the composite's own.</param>
    private static void ReadComposite<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, StructValue value)
        where TCursor : struct, IReadCursor
    {
        if (!state.GeneralPathOnly && program.Composite!.StaticPlan is { } plan && state.CoversPlan(plan))
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

        state.EnterStructure(ref cursor);
        try
        {
            RunFrame(ref cursor, ref state, program, value);
        }
        finally
        {
            state.StructureDepth--;
        }
    }

    /// <summary>
    ///     Executes one program's steps in this call's frame. A failure inside a member of a struct (not of a root) is
    ///     attributed to that member by the exception filter, which runs before any inner <see langword="finally"/>, so the
    ///     innermost member wins, as in the interpreter's field loop; selection, scope and finishing steps belong to no
    ///     member.
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

        // Alignment, offset assertions and the tail are measured from the struct's own first byte (D-18); a root program
        // places nothing.
        long start = program.Kind == ReadProgramKind.Root ? 0 : cursor.Position;
        int[]? selected = program.GroupCount == 0 ? null : NewSelection(program.GroupCount);
        SlotValue[]? locals = program.Scope is { LocalCount: > 0, } scope ? new SlotValue[scope.LocalCount] : null;
        Span<byte> scratch = stackalloc byte[ScratchSize];

        // The count register, the value the last read step produced (for its capture), whether the last array capture
        // was skipped because the array was empty (its publication is skipped with it), and a member start computed by
        // a placement step but not yet moved to because an offset assertion is checked first.
        int count = 0;
        object? last = null;
        bool captureSkipped = false;
        long placed = -1;
        int field = -1;
        try
        {
            for (int index = 0; index < steps.Length; index++)
            {
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

                case ReadOpCode.ReadUInt8:
                case ReadOpCode.ReadInt8:
                case ReadOpCode.ReadBool:
                case ReadOpCode.ReadInt16Le:
                case ReadOpCode.ReadInt16Be:
                case ReadOpCode.ReadUInt16Le:
                case ReadOpCode.ReadUInt16Be:
                case ReadOpCode.ReadInt24Le:
                case ReadOpCode.ReadInt24Be:
                case ReadOpCode.ReadUInt24Le:
                case ReadOpCode.ReadUInt24Be:
                case ReadOpCode.ReadInt32Le:
                case ReadOpCode.ReadInt32Be:
                case ReadOpCode.ReadUInt32Le:
                case ReadOpCode.ReadUInt32Be:
                case ReadOpCode.ReadInt64Le:
                case ReadOpCode.ReadInt64Be:
                case ReadOpCode.ReadUInt64Le:
                case ReadOpCode.ReadUInt64Be:
                case ReadOpCode.ReadFloat32Le:
                case ReadOpCode.ReadFloat32Be:
                case ReadOpCode.ReadFloat64Le:
                case ReadOpCode.ReadFloat64Be:
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
                    state.Slots.Set(step.A, CaptureValue(last));
                    captureSkipped = false;
                    break;

                case ReadOpCode.CaptureNotANumber:
                    state.Slots.Set(step.A, SlotValue.FromUnusable(program.Unusables[step.B]));
                    captureSkipped = false;
                    break;

                case ReadOpCode.CaptureNotANumberIfElements:
                    // The interpreter captures per element, so an empty array captures (and publishes) nothing.
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
                    if (SelectedArm(ref state, program, program.Branches[step.A], selected!) != program.Branches[step.A].Arm)
                    {
                        // The loop's increment lands on the first step after the member.
                        index = step.B - 1;
                    }

                    break;

                case ReadOpCode.CompleteMember:
                    CompleteMember(ref state, program.Scope!, field, locals!);
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

                case ReadOpCode.ReadRootStruct:
                    {
                        ReadProgram nested = program.Nested[step.A];
                        var value = new StructValue(nested.Shape);

                        // The interpreter attaches a root's value before reading its members; the compiler checked the slot.
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

                default:
                    throw new InvalidOperationException("The compiled engine has no executor for read step " + step.Op + ".");
                }
            }
        }
        catch (CStructException exception) when (field >= 0 && program.NotesMembers && NoteMember(exception, program.Fields[field]))
        {
            // Never entered: the filter records the member and lets the exception propagate.
            throw;
        }
    }

    /// <summary>Records the member a failure happened in, as the interpreter's field-loop filter does; never catches.</summary>
    /// <param name="exception">The failure.</param>
    /// <param name="member">The member being read.</param>
    /// <returns><see langword="false"/>, so the exception propagates.</returns>
    private static bool NoteMember(CStructException exception, CompiledField member) => exception.NoteMember(member.Name, member.DisplayTypeSpelling);

    /// <summary>Creates a frame's selected-arm array with every conditional group undecided.</summary>
    /// <param name="groups">The number of conditional groups.</param>
    /// <returns>The array.</returns>
    private static int[] NewSelection(int groups)
    {
        int[] selected = new int[groups];
        Array.Fill(selected, Undecided);
        return selected;
    }

    /// <summary>Whether the step after <paramref name="index"/> checks the same member's offset assertion, which must see the placed start before the position moves.</summary>
    /// <param name="steps">The program's steps.</param>
    /// <param name="index">The placement step's index.</param>
    /// <returns>Whether an offset assertion of the same member follows.</returns>
    private static bool ChecksOffsetNext(ReadStep[] steps, int index)
        => index + 1 < steps.Length && steps[index + 1].Op == ReadOpCode.CheckOffset && steps[index + 1].Field == steps[index].Field;

    /// <summary>
    ///     Checks a member's <c>@N</c> assertion against its start, then moves there. The interpreter's placement cursor
    ///     computes the start, checks the assertion, and only then sets the position, so a failing assertion leaves the
    ///     position before the padding and wins over a start that lies past the input.
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
    ///     Checks an element count as the interpreter's <c>DeclaredElementCount</c> does: a negative count is a read
    ///     failure naming the member, a count past <c>MaxArrayElements</c> a limit failure naming the exact value.
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
    ///     Returns the arm a conditional branch's group selected in this frame, evaluating the group's selector the first
    ///     time the frame needs it, as <c>ConditionalFieldSelection</c> does once per composite instance.
    /// </summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The program.</param>
    /// <param name="branch">The branch being tested.</param>
    /// <param name="selected">The frame's selected arms.</param>
    /// <returns>The selected arm of the branch's group.</returns>
    /// <exception cref="CStructException">The selector cannot be evaluated.</exception>
    private static int SelectedArm(ref ReadEngineState state, ReadProgram program, ReadProgram.ConditionalBranch branch, int[] selected)
    {
        int arm = selected[branch.Group];
        if (arm == Undecided)
        {
            ReadProgram.ConditionalGroup group = program.Groups[branch.Group];
            Int128 value = state.Slots.Evaluate(program.Expressions[group.Selector], program.ExpressionContexts[group.Selector], ExpressionFailureDomain.Read);
            arm = group.Decision.SelectArm(value);
            selected[branch.Group] = arm;
        }

        return arm;
    }

    /// <summary>
    ///     After an active member of a conditional composite, saves the member's own names into the frame's locals, then
    ///     restores the composite's names a nested declaration replaced (an absent saved value removes the name), in the
    ///     order <c>ConditionalVariableScope.CompleteField</c> uses.
    /// </summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="scope">The composite's scope in slot terms.</param>
    /// <param name="member">The member's index.</param>
    /// <param name="locals">The frame's saved values.</param>
    private static void CompleteMember(ref ReadEngineState state, ReadConditionalScope scope, int member, SlotValue[] locals)
    {
        IReadOnlyList<int> captured = scope.GetCaptured(member);
        for (int index = 0; index < captured.Count; index++)
        {
            int local = captured[index];
            locals[local] = state.Slots.Get(scope.LocalSlots[local]);
        }

        IReadOnlyList<int> restored = scope.GetRestored(member);
        for (int index = 0; index < restored.Count; index++)
        {
            int local = restored[index];
            state.Slots.Set(scope.LocalSlots[local], locals[local]);
        }
    }

    /// <summary>
    ///     Converts a value just read into what a capture stores, by the rule every path shares
    ///     (<see cref="LayoutVariableCapture.ToExpression"/>): an integer in the 128-bit domain is a literal, a wider one
    ///     an unusable value that fails naming the number, and anything with no integer meaning removes the name.
    /// </summary>
    /// <param name="value">The value: an integer, <see cref="bool"/>, <see cref="char"/>, or enum result.</param>
    /// <returns>The slot value.</returns>
    private static SlotValue CaptureValue(object? value)
    {
        Int128 captured;
        bool converted = value is EnumValueResult enumValue
                             ? ExpressionValueCapture.TryFromBigInteger(enumValue.Value, out captured)
                             : ExpressionValueCapture.TryConvert(value, out captured);
        if (converted)
        {
            return SlotValue.FromLiteral(captured);
        }

        object? wide = value is EnumValueResult result ? result.Value : value;
        return wide is UInt128 or System.Numerics.BigInteger ? SlotValue.FromUnusable(new WideValueVariable(wide)) : SlotValue.Undefined;
    }

    /// <summary>
    ///     Takes <paramref name="count"/> bytes the way the interpreter's <c>StagedBytes.Take</c> does: in place from a
    ///     memory source within the budget, else - for an extent up to one 64 KiB block - into a rented block from a
    ///     seekable stream that provably holds them within the budget; otherwise nothing is consumed or charged.
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
