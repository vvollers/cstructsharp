namespace CStructSharp.Engine;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Values;
using CStructSharp.Writing;

/// <summary>
///     The compiled engine's writer: executes a root's (or a nested path's member's) <see cref="WriteProgram"/> into a
///     destination and produces exactly what the interpreter's writer produces for the same <c>Serialize</c>, <c>Write</c>
///     or buffer-writer write - the same bytes (and the bytes a failure leaves in a caller's span, stream or committed
///     buffer-writer windows), the same returned count, the same exception type, message, member, path and offset, the
///     same final stream position, and the same write-budget charges. <see cref="UpdateOptions"/> switch on the
///     interpreter's update semantics (<see cref="WriteEngineState.UpdateSemantics"/>).
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
    ///     Serializes a root or nested path into a new array: what the interpreter's <c>Serialize</c> produces over its
    ///     growable stream.
    /// </summary>
    /// <param name="layout">The layout the program belongs to.</param>
    /// <param name="request">The settled write, with the engine's program and slots; the caller disposes the slots.</param>
    /// <param name="data">The caller's data, normalized here as the interpreter normalizes it.</param>
    /// <returns>A new array holding exactly the encoded bytes.</returns>
    /// <exception cref="OperationCanceledException">The token is cancelled before or during the write.</exception>
    /// <exception cref="CStructException">The value cannot be written; the path and offset are attached.</exception>
    public static byte[] SerializeToArray(CStruct layout, in WritePreparation request, object data)
    {
        object rootData = WriteDataBinding.NormalizeRootData(data, request.Segments[0].Name);

        // The interpreter's writer state observes the token when it is created, before the destination is touched.
        request.Options.CancellationToken.ThrowIfCancellationRequested();
        using var buffer = MemoryWriteBuffer.ForNewArray(request.Options);
        var destination = new MemoryWriteDestination(buffer);
        Run(ref destination, layout, request, rootData);
        return buffer.ToArray();
    }

    /// <summary>
    ///     Serializes a root or nested path into a caller's pinned span: what the interpreter's <c>Serialize(Span)</c>
    ///     produces over its region stream, including the prefix a failure leaves in the span.
    /// </summary>
    /// <param name="layout">The layout the program belongs to.</param>
    /// <param name="request">The settled write, with the engine's program and slots; the caller disposes the slots.</param>
    /// <param name="region">The span's first byte; the caller keeps it pinned until the method returns.</param>
    /// <param name="capacity">The span's length in bytes.</param>
    /// <param name="data">The caller's data, normalized here as the interpreter normalizes it.</param>
    /// <returns>The number of bytes written at the span's start: the high-water mark.</returns>
    /// <exception cref="OperationCanceledException">The token is cancelled before or during the write.</exception>
    /// <exception cref="CStructException">The value cannot be written; the path and offset are attached.</exception>
    public static unsafe int SerializeToSpan(CStruct layout, in WritePreparation request, byte* region, int capacity, object data)
    {
        object rootData = WriteDataBinding.NormalizeRootData(data, request.Segments[0].Name);
        request.Options.CancellationToken.ThrowIfCancellationRequested();
        using var buffer = MemoryWriteBuffer.ForSpan(region, capacity, request.Options);
        var destination = new MemoryWriteDestination(buffer);
        Run(ref destination, layout, request, rootData);
        return checked((int)buffer.Length);
    }

    /// <summary>
    ///     Writes a root or nested path into a caller's stream at its position - a buffer writer arrives as its
    ///     <see cref="BufferWriterStream"/> - through the budget stream the interpreter wraps it in: the fields written
    ///     before a failure stay in the stream, bitfields merge into the bytes the stream already holds, and the stream is
    ///     left where the interpreter leaves it.
    /// </summary>
    /// <param name="layout">The layout the program belongs to.</param>
    /// <param name="request">The settled write, with the engine's program and slots; the caller disposes the slots.</param>
    /// <param name="stream">The caller's writable, seekable stream; it stays open and is not flushed.</param>
    /// <param name="data">The caller's data, normalized here as the interpreter normalizes it.</param>
    /// <exception cref="OperationCanceledException">The token is cancelled before or during the write.</exception>
    /// <exception cref="CStructException">The value cannot be written, or the stream fails; the path and the stream's position are attached.</exception>
    public static void WriteToStream(CStruct layout, in WritePreparation request, Stream stream, object data)
    {
        object rootData = WriteDataBinding.NormalizeRootData(data, request.Segments[0].Name);
        request.Options.CancellationToken.ThrowIfCancellationRequested();

        // The budget stream reads the stream's length when it is created, as the interpreter's writer state creates it.
        WriteBudgetStream budget;
        try
        {
            budget = new WriteBudgetStream(stream, request.Options);
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, request.Segments, stream);
            throw;
        }

        var destination = new StreamWriteDestination(budget, stream);
        Run(ref destination, layout, request, rootData);
    }

    /// <summary>
    ///     Runs a write whose token was checked: a nested path first selects its value and checks its indexes (nothing is
    ///     written before), then the program writes; a failure gets the path and the position attached as the interpreter
    ///     attaches them for the destination.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="request">The settled write.</param>
    /// <param name="rootData">The normalized root data.</param>
    private static void Run<TDestination>(ref TDestination destination, CStruct layout, in WritePreparation request, object rootData)
        where TDestination : struct, IWriteDestination
    {
        WriteProgram program = request.Program!;
        var state = new WriteEngineState(layout, request.Slots, request.Options);
        try
        {
            if (request.ChildSegments is { } childSegments)
            {
                // The member the path selects is written on its own from the value at the path: its program is the one the
                // selector chose for the same member, so only the selected value and the index checks remain.
                object value = layout.SelectWrittenPathValue(request.RootElement, childSegments, rootData, request.Variables!, out _);
                RunFrame(ref destination, ref state, program, value, 0);
            }
            else if (program.Steps is [{ Op: WriteOpCode.WriteRootStruct, } root,])
            {
                // A struct root is written straight from the root value: its one-step root frame would only pass it on.
                WriteComposite(ref destination, ref state, program.Nested[root.A], rootData, promoted: false);
            }
            else
            {
                RunFrame(ref destination, ref state, program, rootData, 0);
            }
        }
        catch (CStructException exception)
        {
            destination.AttachContext(exception, request.Segments);
            throw;
        }
        finally
        {
            state.Release();
        }
    }

    /// <summary>
    ///     Writes a struct or union from its value, as the interpreter's <c>WriteStruct</c> does: the token, a null value,
    ///     the mapped-class binding, the unknown-member policy (not for a promoted member, whose data its parent checked),
    ///     then for a struct the static write plan when the interpreter would take it, otherwise member by member - or the
    ///     union's selection staged - inside one claimed nesting level (none for a promoted member).
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, at the struct's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The struct's or union's program.</param>
    /// <param name="data">The value; for a promoted member, the parent's value that carries its members.</param>
    /// <param name="promoted">Whether the struct or union is an anonymous promoted member.</param>
    private static void WriteComposite<TDestination>(ref TDestination destination, ref WriteEngineState state, WriteProgram program, object? data, bool promoted)
        where TDestination : struct, IWriteDestination
    {
        CompiledCompositeType composite = program.Composite!;
        state.CancellationToken.ThrowIfCancellationRequested();
        if (data is null)
        {
            throw new CStructWriteException(WriteFailures.NullComposite(composite.Name));
        }

        data = WriteDataBinding.Bind(data, composite);
        if (state.RejectUnknownMembers && !promoted)
        {
            CStruct.RejectUnknownMembers(composite, data);
        }

        if (!composite.IsUnion && TryWriteStaticPlan(ref destination, ref state, composite, data, promoted))
        {
            return;
        }

        if (!promoted)
        {
            state.EnterStructure();
        }

        try
        {
            if (program.Kind == WriteProgramKind.Union)
            {
                WriteUnion(ref destination, ref state, program, data);
            }
            else
            {
                RunFrame(ref destination, ref state, program, data, 0);
            }
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
    ///     Writes a fixed struct through its static write plan exactly when the interpreter's writer does: no update
    ///     semantics, not restricted to the general path, the plan within one block and the nesting and array limits, the
    ///     bytes under the block readable, and its whole block within the budget and the destination's room. The bytes
    ///     already under the block are read back first, so padding keeps what it held (in a new destination: zeroes), the
    ///     members are encoded before any byte is written, and the block is written once with the interpreter's charge.
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
        if (state.UpdateSemantics || state.GeneralPathOnly || composite.StaticPlan is not { SupportsWrite: true } plan || plan.Size > ReadBlock.Size ||
            state.StructureDepth + plan.NestingDepth - (promoted ? 1 : 0) > state.MaxNestingDepth || plan.MaximumArrayCount > state.MaxArrayElements)
        {
            return false;
        }

        long position = destination.Position;
        long existing = Math.Min(plan.Size, Math.Max(0, destination.Length - position));
        int chargedBytes = state.Layout.Aligned ? plan.ChargedAlignedBytes : plan.ChargedFieldBytes;
        if ((existing > 0 && !destination.CanRead) || !destination.CanAffordBlock(plan.Size, chargedBytes))
        {
            // Bytes under the block that cannot be read back, a span that cannot hold the block, or a budget that cannot
            // pay for it keep the member-by-member writes: the members that fit are written before a failure is reported.
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
    ///     Executes one program's steps in this call's frame, from step <paramref name="entry"/> to the end (or, in a union,
    ///     to the member segment's <see cref="WriteOpCode.Return"/>). A failure inside a named member (from its value lookup
    ///     to its capture) is attributed to that member by the exception filter, which runs before any inner
    ///     <see langword="finally"/>, so the innermost member wins, as in the interpreter's field loop.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, at the program's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="program">The program.</param>
    /// <param name="data">
    ///     The value the members are read from: the struct's, the parent's for a promoted struct, the root value, or a union
    ///     member's selected value.
    /// </param>
    /// <param name="entry">The first step: 0, or a union member's segment.</param>
    private static void RunFrame<TDestination>(ref TDestination destination, ref WriteEngineState state, WriteProgram program, object data, int entry)
        where TDestination : struct, IWriteDestination
    {
        WriteStep[] steps = program.Steps;

        // Alignment, offset assertions and the tail are measured from the struct's own first byte (D-18); a union's
        // members start at its first byte, which is the staging buffer's.
        long start = program.Kind == WriteProgramKind.Root ? 0 : destination.Position;

        // The frame's selected conditional arms live on its stack; a composite with more groups than fit allocates them.
        Span<int> arms = program.GroupCount <= FrameArena.StackArmLimit ? stackalloc int[FrameArena.StackArmLimit] : new int[program.GroupCount];
        arms[..program.GroupCount].Fill(FrameArena.Undecided);
        int locals = program.Scope is { LocalCount: > 0, } scope ? state.TakeLocals(scope.LocalCount) : -1;
        Span<byte> scratch = stackalloc byte[ScratchSize];

        // A value of this program's own shape (a parse's result) is read by slot, any other data by name.
        StructValue? same = data is StructValue structValue && ReferenceEquals(structValue.Shape, program.Shape) ? structValue : null;

        // The registers: the member's supplied value, its element count (-1: the value decides it), the exact number an
        // enum write produced (which a capture stores instead of the supplied value), a member start computed by a
        // placement step but not yet moved to because an offset assertion is checked first, and the open bitfield unit.
        object? value = null;
        int count = 0;
        BigInteger enumValue = default;
        bool hasEnumValue = false;
        long placed = -1;
        int bitOffset = 0;
        int unitSize = 0;

        // A struct with bitfields places its members through the runtime cursor the interpreter uses, held on the stack.
        PlacementCursor placer = program.UsesPlacementCursor
                                     ? new PlacementCursor(start, state.Layout.Aligned, state.Layout.BitfieldPacking, state.Layout.Compilation.HighBitFirst)
                                     : default;
        int index = entry;
        try
        {
            for (; index < steps.Length; index++)
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

                case WriteOpCode.SetCount:
                    count = step.A;
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
                    value = RejectNull(data, program.Fields[step.Field]);
                    hasEnumValue = false;
                    break;

                case WriteOpCode.WriteNumeric:
                    WriteNumber(ref destination, program.Codecs[step.A].Primitive, program.ValueFields[step.Field], value!, scratch);
                    break;

                case WriteOpCode.WriteCodecValue:
                    WriteThroughCodec(ref destination, ref state, program.Codecs[step.A], program.ValueFields[step.Field], value!);
                    break;

                case WriteOpCode.WriteEnum:
                    enumValue = WriteEnum(ref destination, ref state, program.Codecs[step.A], program.Enums[step.B], value!, scratch);
                    hasEnumValue = true;
                    break;

                case WriteOpCode.WriteText:
                    {
                        CompiledField field = program.Fields[step.Field];
                        string text = value as string ?? WriteValueMaterialization.ConvertToBoundedCharString(value!, count, field.Name);
                        WriteText(ref destination, ref state, field, program.Codecs[step.A].CodecId, text, count);
                        break;
                    }

                case WriteOpCode.WriteTextTable:
                    WriteTextTable(ref destination, ref state, program.Fields[step.Field], program.Codecs[step.A].CodecId, value!);
                    break;

                case WriteOpCode.WriteNumericArray:
                    WriteNumericArray(ref destination, ref state, program.Fields[step.Field], program.Codecs[step.A].Primitive, value!, count, step.B == 0, scratch);
                    break;

                case WriteOpCode.WriteElements:
                    WriteElements(ref destination, ref state, program, step.Field, (WriteElementKind)step.B, step.A, value!, count, scratch);
                    break;

                case WriteOpCode.WriteLeaves:
                    WriteLeaves(ref destination, ref state, program, step.Field, (WriteElementKind)step.B, step.A, value!, scratch);
                    break;

                case WriteOpCode.WriteTerminator:
                case WriteOpCode.WriteZeroes:
                    destination.WriteZeroes(step.A);
                    break;

                case WriteOpCode.WritePointer:
                    WritePointer(ref destination, ref state, value, scratch);
                    break;

                case WriteOpCode.WriteBitfield:
                    WriteBitfield(ref destination, ref state, program.Fields[step.Field], value!, ref bitOffset, unitSize, scratch);
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

                case WriteOpCode.WritePromotedUnion:
                    WritePromotedUnion(ref destination, ref state, program.Nested[step.A], data);
                    break;

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
                    FinishComposite(ref destination, start, step.A, step.B, state.UpdateSemantics);
                    break;

                case WriteOpCode.PlaceMember:
                case WriteOpCode.PlaceSeparator:
                    destination.Position = CompositeFieldPlacementCursor.AdvanceToField(ref placer, program.Fields[step.Field]).FieldStart;
                    bitOffset = 0;
                    break;

                case WriteOpCode.PlaceBitfield:
                    {
                        (long unitStart, bitOffset, unitSize) = CompositeFieldPlacementCursor.AdvanceToField(ref placer, program.Fields[step.Field]);
                        destination.Position = unitStart;
                        break;
                    }

                case WriteOpCode.CompletePlacement:
                    placer.CompleteField(destination.Position);
                    break;

                case WriteOpCode.FinishPlaced:
                    FinishPlaced(ref destination, ref state, ref placer, step.B);
                    break;

                case WriteOpCode.OpenBitfieldUnit:
                    bitOffset = 0;
                    unitSize = program.Fields[step.Field].BitStorageSize!.Value;
                    break;

                case WriteOpCode.RewindToUnionStart:
                    destination.Position = start;
                    bitOffset = 0;
                    break;

                case WriteOpCode.Return:
                    index = steps.Length - 1;
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
    /// <returns>The value; <see langword="null"/> only for a scalar pointer.</returns>
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

        return RejectNull(value, member)!;
    }

    /// <summary>
    ///     Rejects a null value, as the interpreter's field write does, unless the member is a scalar pointer, whose null
    ///     value is the null address.
    /// </summary>
    /// <param name="value">The supplied value.</param>
    /// <param name="member">The member.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CStructWriteException">The value is null and the member is not a scalar pointer.</exception>
    private static object? RejectNull(object? value, CompiledField member)
        => value is null && (member.PointerDepth == 0 || member.Array.Kind != CompiledArrayKind.Scalar)
               ? throw new CStructWriteException(WriteFailures.NullForNonPointer(member.Name))
               : value;

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
    ///     byte. Zero bytes are written (and charged) so the output's length is the composite's size. Under update
    ///     semantics the padding keeps the bytes it holds: the position moves past it and nothing is written or charged.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, after the composite's last member.</param>
    /// <param name="start">The composite's first byte.</param>
    /// <param name="padding">The known padding in bytes, or -1.</param>
    /// <param name="alignment">The composite's alignment, used when the padding is not known.</param>
    /// <param name="update">Whether the write has update semantics.</param>
    private static void FinishComposite<TDestination>(ref TDestination destination, long start, int padding, int alignment, bool update)
        where TDestination : struct, IWriteDestination
    {
        if (padding < 0)
        {
            long position = destination.Position;
            padding = checked((int)(start + LayoutMath.AlignUp(position - start, (long)alignment) - position));
        }

        if (!update)
        {
            destination.WriteZeroes(padding);
        }
        else if (padding > 0)
        {
            destination.Position = checked(destination.Position + padding);
        }
    }
}
