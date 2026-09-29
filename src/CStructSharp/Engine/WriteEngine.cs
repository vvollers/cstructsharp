namespace CStructSharp.Engine;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Numerics;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Values;

/// <summary>
///     The compiled engine's writer: executes a root's <see cref="WriteProgram"/> into a destination and produces exactly
///     what the interpreter's writer produces for the same <c>Serialize</c> - the same bytes (and, in a caller's span, the
///     same bytes left behind by a failure), the same returned count, the same exception type, message, member, path and
///     offset, and the same write-budget charges.
/// </summary>
/// <remarks>
///     <para>
///         <b>Dispatch.</b> <see cref="RunFrame{TDestination}"/> is written once for every destination
///         (<c>where TDestination : struct, IWriteDestination</c>); each frame is a <see langword="for"/> loop over the
///         program's steps with one <see langword="switch"/> on the op code.
///     </para>
///     <para>
///         <b>Frames.</b> Every composite program runs in its own call, as the interpreter writes every composite in its
///         own call, so failures are attributed and state is restored in the interpreter's order: the member-noting filter
///         (innermost member wins), the nesting level a struct claims after its static-plan attempt, the cancellation check
///         at each struct's entry, and the frame's conditional selection and scope.
///     </para>
///     <para>
///         <b>Fast paths.</b> Where the interpreter writes a fixed composite through its static write plan, a numeric array
///         from typed storage, or a <c>char[N]</c> as one block, the engine takes the same path under the same conditions,
///         because those paths charge the budget and fail differently from the element-by-element writes.
///     </para>
/// </remarks>
internal static partial class WriteEngine
{
    /// <summary>The widest single value a numeric step encodes, the size of each frame's scratch buffer.</summary>
    private const int ScratchSize = 16;

    /// <summary>The largest block staged on the stack; a larger one is rented, as the interpreter stages it.</summary>
    private const int StackStagingLimit = 512;

    /// <summary>
    ///     Serializes a root into a new array: what the interpreter's <c>Serialize</c> produces over its growable stream.
    /// </summary>
    /// <param name="layout">The layout the program belongs to.</param>
    /// <param name="program">The root's program.</param>
    /// <param name="rootData">The root's value, already normalized from the caller's data.</param>
    /// <param name="segments">The one-segment path that names the root, for failure context.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted and validated options.</param>
    /// <returns>A new array holding exactly the encoded bytes.</returns>
    /// <exception cref="OperationCanceledException">The token is cancelled before or during the write.</exception>
    /// <exception cref="CStructException">The value cannot be written; the path and offset are attached.</exception>
    public static byte[] SerializeToArray(CStruct layout, WriteProgram program, object rootData, IReadOnlyList<PathSegment> segments, VariableSlots slots, WriteOptions options)
    {
        using var buffer = MemoryWriteBuffer.ForNewArray(options);
        var destination = new MemoryWriteDestination(buffer);
        Run(ref destination, layout, program, rootData, segments, slots, options);
        return buffer.ToArray();
    }

    /// <summary>
    ///     Serializes a root into a caller's pinned span: what the interpreter's <c>Serialize(Span)</c> produces over its
    ///     region stream, including the prefix a failure leaves in the span.
    /// </summary>
    /// <param name="layout">The layout the program belongs to.</param>
    /// <param name="program">The root's program.</param>
    /// <param name="region">The span's first byte; the caller keeps it pinned until the method returns.</param>
    /// <param name="capacity">The span's length in bytes.</param>
    /// <param name="rootData">The root's value, already normalized from the caller's data.</param>
    /// <param name="segments">The one-segment path that names the root, for failure context.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The operation's snapshotted and validated options.</param>
    /// <returns>The number of bytes written at the span's start.</returns>
    /// <exception cref="OperationCanceledException">The token is cancelled before or during the write.</exception>
    /// <exception cref="CStructException">The value cannot be written; the path and offset are attached.</exception>
    public static unsafe int SerializeToSpan(CStruct layout, WriteProgram program, byte* region, int capacity, object rootData, IReadOnlyList<PathSegment> segments, VariableSlots slots, WriteOptions options)
    {
        using var buffer = MemoryWriteBuffer.ForSpan(region, capacity, options);
        var destination = new MemoryWriteDestination(buffer);
        Run(ref destination, layout, program, rootData, segments, slots, options);
        return checked((int)buffer.Length);
    }

    /// <summary>
    ///     Runs a root program: the token is checked first, as the interpreter's writer state checks it when it is created;
    ///     a failure gets the path and the destination's position attached, as the interpreter attaches its stream's.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="program">The root's program.</param>
    /// <param name="rootData">The root's value.</param>
    /// <param name="segments">The root's path.</param>
    /// <param name="slots">The operation's slots.</param>
    /// <param name="options">The operation's options.</param>
    private static void Run<TDestination>(ref TDestination destination, CStruct layout, WriteProgram program, object rootData, IReadOnlyList<PathSegment> segments, VariableSlots slots, WriteOptions options)
        where TDestination : struct, IWriteDestination
    {
        var state = new WriteEngineState(layout, slots, options);
        try
        {
            options.CancellationToken.ThrowIfCancellationRequested();
            RunFrame(ref destination, ref state, program, rootData);
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, destination.Position);
            throw;
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Writes a struct from its value, as the interpreter's <c>WriteStruct</c> does: the token, a null value, the
    ///     mapped-class binding, the unknown-member policy (not for a promoted struct, whose data its parent checked), then
    ///     the static write plan when the interpreter would take it, otherwise member by member inside one claimed nesting
    ///     level (none for a promoted struct).
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, at the struct's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The struct's program.</param>
    /// <param name="data">The struct's value; for a promoted struct, the parent's value that carries its members.</param>
    /// <param name="promoted">Whether the struct is an anonymous promoted member of its parent.</param>
    private static void WriteComposite<TDestination>(ref TDestination destination, ref WriteEngineState state, WriteProgram program, object? data, bool promoted)
        where TDestination : struct, IWriteDestination
    {
        CompiledCompositeType composite = program.Composite!;
        state.CancellationToken.ThrowIfCancellationRequested();
        if (data is null)
        {
            throw new CStructWriteException(WriteFailures.NullComposite(composite.Name));
        }

        data = WriteDataBinding.Materialize(data, composite);
        if (state.RejectUnknownMembers && !promoted)
        {
            CStruct.RejectUnknownMembers(composite, data);
        }

        if (TryWriteStaticPlan(ref destination, ref state, composite, data, promoted))
        {
            return;
        }

        if (!promoted)
        {
            state.EnterStructure();
        }

        try
        {
            RunFrame(ref destination, ref state, program, data);
        }
        finally
        {
            if (!promoted)
            {
                state.StructureDepth--;
            }
        }
    }

    /// <summary>
    ///     Writes a fixed struct through its static write plan exactly when the interpreter's writer does: not restricted
    ///     to the general path, the plan within one block and the nesting and array limits, and its whole block within the
    ///     budget and the destination's room. The bytes already under the block are read back first, so padding keeps what
    ///     it held (in a new destination: zeroes), the members are encoded before any byte is written, and the block is
    ///     written once with the interpreter's charge.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, at the struct's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="composite">The struct.</param>
    /// <param name="data">Its bound value.</param>
    /// <param name="promoted">Whether the struct is promoted, which claims no nesting level of its own.</param>
    /// <returns>Whether the plan wrote the struct; <see langword="false"/> having written nothing.</returns>
    private static bool TryWriteStaticPlan<TDestination>(ref TDestination destination, ref WriteEngineState state, CompiledCompositeType composite, object data, bool promoted)
        where TDestination : struct, IWriteDestination
    {
        if (state.GeneralPathOnly || composite.StaticPlan is not { SupportsWrite: true } plan || plan.Size > ReadBlock.Size ||
            state.StructureDepth + plan.NestingDepth - (promoted ? 1 : 0) > state.MaxNestingDepth || plan.MaximumArrayCount > state.MaxArrayElements)
        {
            return false;
        }

        long position = destination.Position;
        long existing = Math.Min(plan.Size, Math.Max(0, destination.Length - position));
        int chargedBytes = state.Layout.Aligned ? plan.ChargedAlignedBytes : plan.ChargedFieldBytes;
        if (!destination.CanAffordBlock(plan.Size, chargedBytes))
        {
            // A span that cannot hold the block, or a budget that cannot pay for it, keeps the member-by-member writes:
            // the members that fit are written before the failure is reported.
            return false;
        }

        byte[] block = ArrayPool<byte>.Shared.Rent(plan.Size);
        try
        {
            Span<byte> span = block.AsSpan(0, plan.Size);
            int preserved = 0;
            while (preserved < existing)
            {
                int read = destination.Read(span.Slice(preserved, (int)existing - preserved));
                if (read <= 0)
                {
                    break;
                }

                preserved += read;
            }

            span[preserved..].Clear();
            destination.Position = position;
            var captures = new SlotWriteCaptures(state.Slots, state.QualifiedPrefix);
            state.Layout.ExecuteStaticWritePlan(plan, composite, span, data, ref captures);
            destination.WriteBlock(span, chargedBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(block);
        }

        return true;
    }

    /// <summary>
    ///     Executes one program's steps in this call's frame. A failure inside a named member (from its value lookup to
    ///     its capture) is attributed to that member by the exception filter, which runs before any inner
    ///     <see langword="finally"/>, so the innermost member wins, as in the interpreter's field loop.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, at the program's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The program.</param>
    /// <param name="data">The value the members are read from: the struct's, the parent's for a promoted struct, or the root value.</param>
    private static void RunFrame<TDestination>(ref TDestination destination, ref WriteEngineState state, WriteProgram program, object data)
        where TDestination : struct, IWriteDestination
    {
        WriteStep[] steps = program.Steps;

        // Alignment, offset assertions and the tail are measured from the struct's own first byte (D-18).
        long start = program.Kind == WriteProgramKind.Root ? 0 : destination.Position;
        int arms = program.GroupCount == 0 ? -1 : state.TakeArms(program.GroupCount);
        int locals = program.Scope is { LocalCount: > 0, } scope ? state.TakeLocals(scope.LocalCount) : -1;
        Span<byte> scratch = stackalloc byte[ScratchSize];

        // A value of this program's own shape (a parse's result) is read by slot, any other data by name.
        StructValue? same = data is StructValue structValue && ReferenceEquals(structValue.Shape, program.Shape) ? structValue : null;

        // The registers: the member's supplied value, its element count, the exact number an enum write produced (which a
        // capture stores instead of the supplied value), and a member start computed by a placement step but not yet
        // moved to because an offset assertion is checked first.
        object? value = null;
        int count = 0;
        BigInteger enumValue = default;
        bool hasEnumValue = false;
        long placed = -1;
        int index = 0;
        try
        {
            for (index = 0; index < steps.Length; index++)
            {
                WriteStep step = steps[index];
                switch (step.Op)
                {
                case WriteOpCode.Seek:
                    if (ChecksOffsetNext(steps, index))
                    {
                        placed = checked(destination.Position + step.A);
                    }
                    else
                    {
                        destination.Position = checked(destination.Position + step.A);
                    }

                    break;

                case WriteOpCode.Align:
                    {
                        long aligned = start + LayoutMath.AlignUp(destination.Position - start, (long)step.A);
                        if (ChecksOffsetNext(steps, index))
                        {
                            placed = aligned;
                        }
                        else
                        {
                            destination.Position = aligned;
                        }

                        break;
                    }

                case WriteOpCode.CheckOffset:
                    CheckOffset(ref destination, program.Fields[step.Field], step.A, start, ref placed);
                    break;

                case WriteOpCode.CheckFixedCount:
                    count = step.A;
                    if (count > state.MaxArrayElements)
                    {
                        throw new CStructWriteLimitException(WriteFailures.ArrayLengthLimit(program.Fields[step.Field].Name));
                    }

                    break;

                case WriteOpCode.EvaluateCount:
                    count = CheckCount(
                        state.Slots.Evaluate(program.Expressions[step.A], program.ExpressionContexts[step.A], ExpressionFailureDomain.Write),
                        program.Fields[step.Field],
                        state.MaxArrayElements);
                    break;

                case WriteOpCode.LoadMember:
                    value = LoadMember(same, data, step.A, program.Fields[step.Field]);
                    hasEnumValue = false;
                    break;

                case WriteOpCode.LoadPadding:
                    value = CStruct.CreatePaddingValue(program.Fields[step.Field]);
                    hasEnumValue = false;
                    break;

                case WriteOpCode.LoadRoot:
                    value = data ?? throw new CStructWriteException(WriteFailures.NullForNonPointer(program.Fields[step.Field].Name));
                    hasEnumValue = false;
                    break;

                case WriteOpCode.WriteNumeric:
                    WriteNumber(ref destination, program.Codecs[step.A].Primitive, program.ValueFields[step.Field], value!, scratch);
                    break;

                case WriteOpCode.WriteCodecValue:
                    WriteThroughCodec(ref destination, ref state, program.Codecs[step.A].CodecId, program.ValueFields[step.Field], value!);
                    break;

                case WriteOpCode.WriteEnum:
                    enumValue = WriteEnum(ref destination, ref state, program.Codecs[step.A], program.Enums[step.B], value!, scratch);
                    hasEnumValue = true;
                    break;

                case WriteOpCode.WriteText:
                    WriteText(ref destination, ref state, program.Fields[step.Field], program.Codecs[step.A].CodecId, value!, count);
                    break;

                case WriteOpCode.WriteNumericArray:
                    WriteNumericArray(ref destination, ref state, program.Fields[step.Field], program.Codecs[step.A].Primitive, value!, count, step.B == 0, scratch);
                    break;

                case WriteOpCode.WriteCodecArray:
                    WriteCodecArray(ref destination, ref state, program.Fields[step.Field], program.Codecs[step.A].CodecId, value!, count);
                    break;

                case WriteOpCode.WriteEnumArray:
                    WriteEnumArray(ref destination, ref state, program.Fields[step.Field], program.Codecs[step.A], program.Enums[step.B], value!, count, scratch);
                    break;

                case WriteOpCode.WriteStruct:
                    {
                        string? outer = state.QualifiedPrefix;
                        if (step.B >= 0)
                        {
                            state.QualifiedPrefix = outer is null ? program.Prefixes[step.B] : outer + program.Prefixes[step.B];
                        }

                        WriteComposite(ref destination, ref state, program.Nested[step.A], value, promoted: false);
                        state.QualifiedPrefix = outer;
                        break;
                    }

                case WriteOpCode.WriteStructArray:
                    WriteStructArray(ref destination, ref state, program.Fields[step.Field], program.Nested[step.A], value!, count);
                    break;

                case WriteOpCode.WritePromotedStruct:
                    {
                        string? outer = state.QualifiedPrefix;
                        if (step.B >= 0)
                        {
                            state.QualifiedPrefix = outer is null ? program.Prefixes[step.B] : outer + program.Prefixes[step.B];
                        }

                        WriteComposite(ref destination, ref state, program.Nested[step.A], data, promoted: true);
                        state.QualifiedPrefix = outer;
                        break;
                    }

                case WriteOpCode.WriteRootStruct:
                    WriteComposite(ref destination, ref state, program.Nested[step.A], data, promoted: false);
                    break;

                case WriteOpCode.CaptureValue:
                    {
                        SlotValue captured = LayoutVariableCapture.ToSlotValue(hasEnumValue ? enumValue : value);
                        state.Slots.Set(step.A, captured);
                        if (step.B >= 0)
                        {
                            state.PublishQualified(program.QualifiedTargets[step.B], captured);
                        }

                        break;
                    }

                case WriteOpCode.CaptureNotANumber:
                    {
                        SlotValue captured = SlotValue.FromUnusable(program.NotANumbers[step.Field]!);
                        state.Slots.Set(step.A, captured);
                        if (step.B >= 0)
                        {
                            state.PublishQualified(program.QualifiedTargets[step.B], captured);
                        }

                        break;
                    }

                case WriteOpCode.SelectArm:
                    {
                        ReadProgram.ConditionalBranch branch = program.Branches[step.A];
                        if (state.SelectedArm(program, branch, arms) != branch.Arm)
                        {
                            RejectInactive(program, program.Fields[step.Field], data);

                            // The loop's increment lands on the first step after the member.
                            index = step.B - 1;
                        }

                        break;
                    }

                case WriteOpCode.EnterConditionalScope:
                    foreach (int slot in program.Scope!.ClearedSlots)
                    {
                        state.Slots.Set(slot, SlotValue.Undefined);
                    }

                    break;

                case WriteOpCode.CompleteMember:
                    state.CompleteMember(program.Scope!, step.Field, locals);
                    break;

                case WriteOpCode.FinishComposite:
                    FinishComposite(ref destination, start, step.A, step.B);
                    break;

                case WriteOpCode.EvaluateDefinition:
                    {
                        Int128 defined = state.Slots.Evaluate(program.Expressions[step.A], program.ExpressionContexts[step.A], ExpressionFailureDomain.Write);
                        if (step.B >= 0)
                        {
                            state.Slots.Set(step.B, SlotValue.FromLiteral(defined));
                        }

                        break;
                    }

                default:
                    throw new InvalidOperationException("The compiled engine has no executor for write step " + step.Op + ".");
                }
            }

            if (locals >= 0)
            {
                state.ReleaseLocals(locals);
            }

            if (arms >= 0)
            {
                state.ReleaseArms(arms);
            }
        }
        catch (CStructException exception) when (NoteMember(exception, program, index))
        {
            // Never entered: the filter records the member and lets the exception propagate.
            throw;
        }
    }

    /// <summary>Records the member a failed step belongs to, as the interpreter's field-loop filter does; never catches.</summary>
    /// <param name="exception">The failure.</param>
    /// <param name="program">The program.</param>
    /// <param name="index">The failed step's index.</param>
    /// <returns><see langword="false"/>, so the exception propagates.</returns>
    private static bool NoteMember(CStructException exception, WriteProgram program, int index)
    {
        if (index >= program.Steps.Length || program.NotedMembers[index] is not (>= 0 and var member))
        {
            return false;
        }

        CompiledField field = program.Fields[member];
        return exception.NoteMember(field.Name, field.DisplayTypeSpelling);
    }

    /// <summary>Whether the step after <paramref name="index"/> checks the same member's offset assertion, which must see the placed start before the position moves.</summary>
    /// <param name="steps">The program's steps.</param>
    /// <param name="index">The placement step's index.</param>
    /// <returns>Whether an offset assertion of the same member follows.</returns>
    private static bool ChecksOffsetNext(WriteStep[] steps, int index)
        => index + 1 < steps.Length && steps[index + 1].Op == WriteOpCode.CheckOffset && steps[index + 1].Field == steps[index].Field;

    /// <summary>
    ///     Checks a member's <c>@N</c> assertion against its start, then moves there: the interpreter's placement cursor
    ///     computes the start, checks the assertion, and only then sets the position.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="member">The asserted member.</param>
    /// <param name="asserted">The asserted offset from the struct's first byte.</param>
    /// <param name="start">The struct's first byte.</param>
    /// <param name="placed">The member start a placement step computed, or -1 when the member needed no padding; reset to -1.</param>
    /// <exception cref="CStructLayoutException">The member does not start at the asserted offset.</exception>
    private static void CheckOffset<TDestination>(ref TDestination destination, CompiledField member, int asserted, long start, ref long placed)
        where TDestination : struct, IWriteDestination
    {
        long memberStart = placed >= 0 ? placed : destination.Position;
        if (OffsetAssertion.Check(member.Name, asserted, memberStart - start) is { } failure)
        {
            throw new CStructLayoutException(failure);
        }

        if (placed >= 0)
        {
            placed = -1;
            destination.Position = memberStart;
        }
    }

    /// <summary>
    ///     Checks a runtime element count as the interpreter's writer does: a negative count is a write failure naming the
    ///     member, a count past <c>MaxArrayElements</c> a limit failure naming it.
    /// </summary>
    /// <param name="count">The count, in the expression domain.</param>
    /// <param name="member">The array member.</param>
    /// <param name="maximum">The operation's element limit.</param>
    /// <returns>The count as an <see cref="int"/>.</returns>
    /// <exception cref="CStructWriteException">The count is negative.</exception>
    /// <exception cref="CStructWriteLimitException">The count exceeds <paramref name="maximum"/>.</exception>
    private static int CheckCount(Int128 count, CompiledField member, int maximum)
    {
        if (count < 0)
        {
            throw new CStructWriteException(LayoutFailures.NegativeArrayLength(member.Name));
        }

        if (count > maximum)
        {
            throw new CStructWriteLimitException(WriteFailures.ArrayLengthLimit(member.Name));
        }

        return (int)count;
    }

    /// <summary>
    ///     Looks a member's value up as the interpreter's writer does - by slot in a value of the program's own shape, by
    ///     name in any other data - and rejects a missing or null value.
    /// </summary>
    /// <param name="same">The frame's data when it is a struct value of the program's shape, else <see langword="null"/>.</param>
    /// <param name="data">The frame's data.</param>
    /// <param name="slot">The member's slot in the program's shape.</param>
    /// <param name="member">The member.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CStructWriteException">No value, or a null value, was supplied.</exception>
    private static object LoadMember(StructValue? same, object data, int slot, CompiledField member)
    {
        object? value;
        if (same is not null)
        {
            if (!same.TryGetSlot(slot, out value))
            {
                throw new CStructWriteException(WriteFailures.NoValueSupplied(member.Name));
            }
        }
        else
        {
            value = WriteDataBinding.GetMemberValueOrThrow(data, member.Name);
        }

        return value ?? throw new CStructWriteException(WriteFailures.NullForNonPointer(member.Name));
    }

    /// <summary>
    ///     Rejects a value supplied for any name an unselected conditional member makes visible, as the interpreter does
    ///     before it skips the member.
    /// </summary>
    /// <param name="program">The program, whose composite owns the conditional scope.</param>
    /// <param name="member">The unselected member.</param>
    /// <param name="data">The frame's data.</param>
    /// <exception cref="CStructWriteException">The data supplies such a name.</exception>
    private static void RejectInactive(WriteProgram program, CompiledField member, object data)
    {
        foreach (string name in program.Composite!.ConditionalScope!.VisibleNames[member.MemberIndex])
        {
            if (WriteDataBinding.TryGetMemberValue(data, name, out _))
            {
                throw new CStructWriteException(WriteFailures.InactiveConditionalField(name));
            }
        }
    }

    /// <summary>
    ///     Writes a composite's tail padding as zeroes: the known padding, or up to the composite's alignment from its first
    ///     byte. Zero bytes are written (and charged) so the output's length is the composite's size.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, after the composite's last member.</param>
    /// <param name="start">The composite's first byte.</param>
    /// <param name="padding">The known padding in bytes, or -1.</param>
    /// <param name="alignment">The composite's alignment, used when the padding is not known.</param>
    private static void FinishComposite<TDestination>(ref TDestination destination, long start, int padding, int alignment)
        where TDestination : struct, IWriteDestination
    {
        if (padding < 0)
        {
            long position = destination.Position;
            padding = checked((int)(start + LayoutMath.AlignUp(position - start, (long)alignment) - position));
        }

        destination.WriteZeroes(padding);
    }
}
