namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Values;

/// <summary>
///     The pointer reads of the compiled engine, each with a fixed order of checks: the stored address, a deferred or
///     in-place follow, the target's value, and the deferred pointers a struct follows after its last member. The
///     bookkeeping (targets on the active path for cycle detection, the deferred queue) is the pooled
///     <see cref="PointerTraversal"/>.
/// </summary>
internal static partial class ReadEngine
{
    /// <summary>
    ///     Reads a pointer member (or one element of a pointer array). A deferred pointer that would be followed is stored
    ///     unresolved and queued with the position after its address; one that will not be followed (null, following
    ///     disabled or suppressed in a union view, <c>void *</c>) is final at once. Any other pointer is followed in place.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the stored address.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="target">The pointer's target description.</param>
    /// <param name="deferred">Whether the target is followed after the struct's last member.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    /// <returns>The pointer as stored in the result.</returns>
    private static Pointer ReadPointerField<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadPointerTarget target, bool deferred, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        int depth = target.Field.PointerDepth;
        if (!deferred)
        {
            return ReadPointerValue(ref cursor, ref state, target, depth, -1, scratch);
        }

        long address = ReadPointerAddress(ref cursor, ref state, scratch);
        var pointer = new Pointer(address, null, depth, false);
        if (address != 0 && state.DereferencePointers && !state.SuppressPointers && !target.IsVoid)
        {
            state.Pointers.Pending.Add(new PendingPointer(pointer, target.Field, null, cursor.Position, target));
        }

        return pointer;
    }

    /// <summary>Reads a stored address and follows it when the options allow.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the stored address; it ends just after it.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="target">The pointer's target description.</param>
    /// <param name="depth">The pointer levels still to follow, at least 1.</param>
    /// <param name="elementCount">A counted target's element count, or -1 to evaluate it when the final level is reached.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    /// <returns>The pointer, dereferenced when it was followed.</returns>
    private static Pointer ReadPointerValue<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadPointerTarget target, int depth, int elementCount, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        long address = ReadPointerAddress(ref cursor, ref state, scratch);
        object? value = FollowPointerTarget(ref cursor, ref state, target, address, depth, elementCount, scratch);
        return value is null ? new Pointer(address, null, depth, false) : new Pointer(address, value, depth, true);
    }

    /// <summary>Reads a stored address: exactly the layout's pointer width in its byte order, which must fit a signed position.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state, whose layout gives the width and byte order.</param>
    /// <param name="scratch">A buffer of at least 8 bytes.</param>
    /// <returns>The stored address.</returns>
    /// <exception cref="CStructReadException">The input ends early, or the address is at or above 2^63.</exception>
    internal static long ReadPointerAddress<TCursor>(ref TCursor cursor, ref ReadEngineState state, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        Span<byte> bytes = scratch[..state.Layout.PointerSize];
        cursor.ReadExactly(bytes);
        ulong stored = BinaryPrimitiveIO.ReadUnsigned(bytes, state.Layout.IsLittleEndian);
        try
        {
            return CStructPointerArithmetic.DecodeStoredAddress(stored);
        }
        catch (OverflowException exception)
        {
            throw new CStructReadException(ReadFailures.PointerAddressRange, exception);
        }
    }

    /// <summary>
    ///     Follows an address to its target, checking in this order: nothing for a null pointer, disabled or suppressed
    ///     following, or a one-level <c>void *</c>; then the depth limit, the target address (relative overflow), its bounds,
    ///     the count of an in-place counted target, the target size limit, the cycle check, and cancellation. The target is
    ///     read one level deeper, and the depth, the active target and the position are restored whatever happens. A debug
    ///     read records the target (<see cref="ReadRecordedPointerTarget{TCursor}"/>).
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, just after the stored address.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="target">The pointer's target description.</param>
    /// <param name="address">The stored address.</param>
    /// <param name="depth">The pointer levels still to follow, at least 1.</param>
    /// <param name="elementCount">A counted target's element count, or -1 to evaluate it here.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    /// <returns>The target (another pointer above the last level), or <see langword="null"/> when it is not followed.</returns>
    /// <exception cref="CStructException">A limit is exceeded, the target lies outside the input or on the active path, or it cannot be read.</exception>
    private static object? FollowPointerTarget<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadPointerTarget target, long address, int depth, int elementCount, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        if (address == 0 || !state.DereferencePointers || state.SuppressPointers || (depth == 1 && target.IsVoid))
        {
            return null;
        }

        if (state.PointerDepth >= state.MaxPointerDepth)
        {
            throw new CStructReadLimitException(ReadFailures.PointerDepthLimit);
        }

        long resume = cursor.Position;
        long targetAddress;
        try
        {
            targetAddress = CStructPointerArithmetic.ResolveTargetAddress(address, state.AddressingMode, state.PointerOrigin);
        }
        catch (OverflowException exception)
        {
            throw new CStructReadException(ReadFailures.RelativePointerOverflow, exception);
        }

        if (targetAddress < 0 || targetAddress >= cursor.Length)
        {
            throw new CStructReadException(ReadFailures.PointerTargetOutside(targetAddress));
        }

        if (elementCount < 0)
        {
            // Only an in-place follow gets here without a count; above the last level the count passed down is 1.
            elementCount = depth == 1 && target.IsCounted ? EvaluatePointerCount(ref state, target) : 1;
        }

        CompiledField field = target.Field;
        state.Layout.EnsurePointerTargetSize(depth, field, state.MaxPointerTargetBytes, elementCount);
        (long Address, string TypeName, int PointerDepth) key = (targetAddress, field.TypeSpelling, depth);
        HashSet<(long Address, string TypeName, int PointerDepth)> active = state.Pointers.ActiveTargets;
        if (!active.Add(key))
        {
            throw new CStructReadException(ReadFailures.CyclicPointer(targetAddress));
        }

        cursor.ThrowIfCancellationRequested();
        state.PointerDepth++;
        try
        {
            cursor.Position = targetAddress;
            if (state.Debug is { } debug)
            {
                // A debug read records the target under the followed pointer's path extended by `value`.
                return ReadRecordedPointerTarget(ref cursor, ref state, target, depth - 1, elementCount, new DebugPath(debug.Target, "value"), scratch);
            }

            return depth > 1
                       ? ReadPointerValue(ref cursor, ref state, target, depth - 1, elementCount, scratch)
                       : ReadPointerTargetValue(ref cursor, ref state, target, elementCount, scratch);
        }
        finally
        {
            state.PointerDepth--;
            active.Remove(key);
            cursor.Position = resume;
        }
    }

    /// <summary>
    ///     Evaluates a counted target's <c>@count(N)</c> against the current variables and checks it as an array length:
    ///     a negative count and one past the element limit fail.
    /// </summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="target">The counted target.</param>
    /// <returns>The element count.</returns>
    /// <exception cref="CStructException">The count cannot be evaluated, is negative, or exceeds the limit.</exception>
    private static int EvaluatePointerCount(ref ReadEngineState state, ReadPointerTarget target)
    {
        Int128 count = target.Count is { } expression
                           ? state.Slots.Evaluate(expression, target.CountContext, ExpressionFailureDomain.Read)
                           : target.Field.PointerElements!.FixedCount!.Value;
        return CheckCount(count, target.Field, state.MaxArrayElements);
    }

    /// <summary>Reads a pointer's final target at the position, by the kind its description gives.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the target.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="target">The target description.</param>
    /// <param name="elementCount">A counted target's element count.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    /// <returns>The target's value.</returns>
    /// <exception cref="InvalidOperationException">The target type has no reader, or a caller's codec decoded no value.</exception>
    private static object ReadPointerTargetValue<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadPointerTarget target, int elementCount, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        switch (target.Kind)
        {
        case ReadPointerTargetKind.Enum:
            return ValueDecoding.CreateEnumValue(target.Enum!, ReadCodecValue(ref cursor, target.Codec.Primitive, scratch));
        case ReadPointerTargetKind.Composite:
            // A debug read records the target's members under the target's path, `value` after the pointer's.
            return state.Debug is { } debug ? ReadRecordedPointerComposite(ref cursor, ref state, target, debug.Target) : ReadPointerComposite(ref cursor, ref state, target);
        case ReadPointerTargetKind.Terminated:
            return ReadCodecValue(ref cursor, target.Codec.Primitive, scratch);
        case ReadPointerTargetKind.Value:
            return ReadTargetCodecValue(ref cursor, ref state, target, "Compiled pointer target has no reader: ", scratch);
        case ReadPointerTargetKind.NoReader:
            throw new InvalidOperationException("Compiled pointer target has no reader: " + target.Field.TypeSpelling);
        default:
            return ReadCountedTarget(ref cursor, ref state, target, elementCount, scratch);
        }
    }

    /// <summary>
    ///     Reads one value of a target's codec (a caller's codec through its adapter); a caller's codec that decodes no value
    ///     fails with the caller's message for the missing reader.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state, whose layout holds the caller's codecs.</param>
    /// <param name="target">The target description, whose codec is read.</param>
    /// <param name="missing">The start of the failure message, followed by the pointer's type spelling.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    /// <returns>The value.</returns>
    private static object ReadTargetCodecValue<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadPointerTarget target, string missing, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        if (!target.Codec.Primitive.IsCustom)
        {
            return ReadCodecValue(ref cursor, target.Codec.Primitive, scratch);
        }

        return cursor.ReadCustom(state.Layout.Codecs.CustomCodecOf(target.Codec.CodecId)) ??
               throw new InvalidOperationException(missing + target.Field.TypeSpelling);
    }

    /// <summary>
    ///     Reads a struct or union target at its address: a union as a union value of its own, a struct into a new
    ///     value through its program (or its static plan), at the current nesting depth.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the target.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="target">The target description; its composite's program is taken from the layout's cache on first use.</param>
    /// <returns>The struct or union value.</returns>
    private static object ReadPointerComposite<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadPointerTarget target)
        where TCursor : struct, IReadCursor
    {
        ReadProgram program = target.Program ??= state.Slots.Table.ReadPrograms.GetComposite(state.Layout.Compilation, target.Composite!);
        if (program.Kind == ReadProgramKind.Union)
        {
            return ReadUnion(ref cursor, ref state, program, promoted: false);
        }

        var value = new StructValue(program.Shape);
        ReadComposite(ref cursor, ref state, program, value);
        return value;
    }

    /// <summary>
    ///     Reads a counted target's elements from the position: characters as one string (trimmed; wide text validated),
    ///     fixed-width numbers as a typed array in blocks, and anything else one element after another into a list, with
    ///     cancellation observed before each element. A debug read records every element under the target's path
    ///     (<see cref="DebugRecorder.Target"/>): a character, number, enum number or value as one record each (where the
    ///     recorder keeps target values, <see cref="DebugRecorder.RecordsTargetValues"/>), and a struct or union element's
    ///     members under the element's path (<c>nodes.value[3]</c>).
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the first element.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="target">The counted target.</param>
    /// <param name="count">The element count.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    /// <returns>The target's value.</returns>
    private static object ReadCountedTarget<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadPointerTarget target, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        PrimitiveCodec codec = target.Codec.Primitive;
        DebugRecorder? debug = state.Debug;
        DebugPath? path = debug?.Target;
        string typeName = target.Field.TypeSpelling;

        // The recorder of the non-composite elements, absent outside a debug read and in an update's layout capture;
        // without it no element position is taken.
        DebugRecorder? valueRecorder = debug is { RecordsTargetValues: true, } ? debug : null;
        switch (target.Kind)
        {
        case ReadPointerTargetKind.CountedText:
            {
                char[] characters = new char[count];
                for (int index = 0; index < count; index++)
                {
                    long start = valueRecorder is null ? 0 : cursor.Position;
                    object character = ReadCodecValue(ref cursor, codec, scratch);
                    valueRecorder?.Record(start, cursor.Position, path, character, typeName);
                    characters[index] = (char)character;
                }

                string text = state.FixedText(new string(characters));
                CompiledField element = target.Element!;
                if (element.IsWideCharElement)
                {
                    PrimitiveCodecs.ValidateWideText(text, state.Layout.GetWideCharacterEncoding(element));
                }

                return text;
            }

        case ReadPointerTargetKind.CountedNumbers:
            if (valueRecorder is not null)
            {
                return RecordCountedNumbers(ref cursor, valueRecorder, codec, count, typeName);
            }

            return count == 0 ? PrimitiveArrayReader.Empty(codec) : cursor.ReadPrimitiveArray(codec, count);
        default:
            {
                var values = new List<object?>(count);
                for (int index = 0; index < count; index++)
                {
                    cursor.ThrowIfCancellationRequested();
                    if (target.Kind == ReadPointerTargetKind.CountedComposites)
                    {
                        // A debug read records each composite element under the target's path with the element's index.
                        values.Add(debug is null ? ReadPointerComposite(ref cursor, ref state, target) : ReadRecordedPointerComposite(ref cursor, ref state, target, DebugRecorder.CountedElementPath(path, index)));
                        continue;
                    }

                    long start = valueRecorder is null ? 0 : cursor.Position;
                    object value = target.Kind == ReadPointerTargetKind.CountedEnums
                                       ? ValueDecoding.CreateEnumValue(target.Enum!, ReadCodecValue(ref cursor, codec, scratch))
                                       : ReadTargetCodecValue(ref cursor, ref state, target, "Counted target has no reader: ", scratch);
                    valueRecorder?.Record(start, cursor.Position, path, value is EnumValueResult number ? number.Value : value, typeName);
                    values.Add(value);
                }

                return values;
            }
        }
    }

    /// <summary>
    ///     After a struct's last member, follows the pointers it and its promoted members deferred, in declaration order:
    ///     each from just after its stored address, its <c>@count</c> evaluated before any level is checked, and resolves
    ///     the stored pointer in place. A failure names the pointer and leaves the position after its address; after the
    ///     last one the position is back where the struct's members ended, from where the struct is finished.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="start">The queued pointers of enclosing structs, which stay queued.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    private static void FollowPendingPointers<TCursor>(ref TCursor cursor, ref ReadEngineState state, int start, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        if (state.PendingPointerCount <= start)
        {
            return;
        }

        // A target struct follows its own deferred pointers before its read returns, so each follow leaves the queue as
        // long as it found it and the indexes stay valid.
        List<PendingPointer> pending = state.Pointers.Pending;
        int end = pending.Count;
        long resume = cursor.Position;
        for (int index = start; index < end; index++)
        {
            PendingPointer entry = pending[index];
            ReadPointerTarget target = entry.Target!;
            try
            {
                cursor.Position = entry.AddressEnd;
                if (state.Debug is { } debug)
                {
                    // The target is recorded under the path the pointer was read with.
                    debug.Target = entry.DebugStack;
                }

                int count = target.IsCounted ? EvaluatePointerCount(ref state, target) : 1;
                object? value = FollowPointerTarget(ref cursor, ref state, target, entry.Placeholder.Address, target.Field.PointerDepth, count, scratch);
                if (value is not null)
                {
                    entry.Placeholder.Resolve(value);
                }
            }
            catch (CStructException exception) when (exception.NoteMember(target.Field.Name, target.Field.DisplayTypeSpelling))
            {
                // Never entered: the filter names the pointer, as a failure during its own read would.
                throw;
            }
        }

        pending.RemoveRange(start, end - start);
        cursor.Position = resume;
    }
}
