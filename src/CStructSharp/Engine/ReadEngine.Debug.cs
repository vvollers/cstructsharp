namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Values;

/// <summary>
///     The debug parse of the compiled engine: the steps only debug programs hold (<see cref="ReadProgramCache.Debug"/>),
///     which record every value read as the interpreter's debug parse does - the same ranges, paths, type spellings,
///     values and order - and the layout capture an update compares.
/// </summary>
/// <remarks>
///     <para>
///         A debug program reads exactly what the ordinary program reads, with the same checks, but every array one element
///         at a time and every struct member by member (the recorder turns the static read plans off,
///         <see cref="ReadEngineState.GeneralPathOnly"/>), because the interpreter's debug parse takes none of its block
///         paths. Its read steps are the ordinary ones, surrounded by <see cref="ReadOpCode.DebugMark"/> and
///         <see cref="ReadOpCode.DebugRecord"/>; its array, composite and pointer steps are the debug codes handled here.
///     </para>
///     <para>
///         <b>Order.</b> A value is recorded once it is read, so a struct's members come in declaration order, a union's
///         own record follows its views', a pointer followed in place comes after its target's records, and a deferred
///         pointer's target records come after its struct's last member.
///     </para>
/// </remarks>
internal static partial class ReadEngine
{
    /// <summary>
    ///     Reads one whole root with its debug program from a stream, as the interpreter's update captures the layout
    ///     before and after a change: from <paramref name="origin"/>, through a new budget over the stream, with the
    ///     conditional-layout trace, and without attaching a path or offset to a failure. The final position is written
    ///     back to the stream.
    /// </summary>
    /// <param name="layout">The layout the program belongs to.</param>
    /// <param name="stream">The data: the original input or an update's staged copy.</param>
    /// <param name="origin">The root's position in <paramref name="stream"/>.</param>
    /// <param name="program">The root's debug program.</param>
    /// <param name="slots">The operation's initialized variable slots; the caller disposes them.</param>
    /// <param name="options">The read settings.</param>
    /// <returns>Each value's path and byte range, then each conditional member's name, position and selection (1 or 0).</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> cannot read or seek.</exception>
    /// <exception cref="CStructException">The data cannot be read.</exception>
    public static (string Path, long Start, long End)[] CaptureLayout(CStruct layout, Stream stream, long origin, ReadProgram program, VariableSlots slots, in ReadOperationSettings options)
    {
        stream.Position = origin;
        CStructOperationContext.Validate(stream, options);
        var recorder = new DebugRecorder(trace: true);
        var state = new ReadEngineState(layout, slots, options, recorder);
        try
        {
            if (MemoryReadCursor.TryCreate(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken, out MemoryReadCursor memory))
            {
                CaptureRoot(ref memory, ref state, program);
            }
            else
            {
                var cursor = new StreamReadCursor(new ReadBudgetStream(stream, options.MaxStringBytes, options.MaxTotalBytesRead, options.CancellationToken));
                CaptureRoot(ref cursor, ref state, program);
            }
        }
        finally
        {
            state.Release();
        }

        return recorder.Layout();
    }

    /// <summary>Runs a root's debug program into a throwaway root value and writes the final position back, whatever happens.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state, which holds the recorder.</param>
    /// <param name="program">The root's debug program.</param>
    private static void CaptureRoot<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program)
        where TCursor : struct, IReadCursor
    {
        try
        {
            RunFrame(ref cursor, ref state, program, new StructValue(program.Shape));
        }
        finally
        {
            cursor.FlushPosition();
        }
    }

    /// <summary>
    ///     Executes one step only a debug program holds, in the frame of <see cref="RunFrame{TCursor}"/>, and returns the
    ///     frame's value register: the pointer a pointer step read, otherwise the register unchanged, so a capture after a
    ///     recorded read captures the value read.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state, which holds the recorder.</param>
    /// <param name="program">The frame's program.</param>
    /// <param name="step">The step.</param>
    /// <param name="destination">The value the frame stores members into.</param>
    /// <param name="last">The frame's value register: the value the last read step produced.</param>
    /// <param name="count">The frame's count register.</param>
    /// <param name="unitSize">The size in bytes of the frame's current bitfield storage unit.</param>
    /// <param name="scratch">The frame's scratch buffer.</param>
    /// <returns>The value register after the step.</returns>
    /// <exception cref="InvalidOperationException">The step is not a debug step.</exception>
    private static object? RunDebugStep<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, ReadStep step, StructValue destination, object? last, int count, int unitSize, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        DebugRecorder debug = state.Debug ?? throw new InvalidOperationException("The compiled engine has no executor for read step " + step.Op + ".");
        int field = step.Field;
        switch (step.Op)
        {
        case ReadOpCode.DebugMember:
            debug.EnterMember(program.Fields[field]);
            return last;

        case ReadOpCode.DebugMark:
            debug.Start = cursor.Position;
            return last;

        case ReadOpCode.DebugRecord:
            RecordValue(ref cursor, debug, program.Fields[field], (DebugRecordKind)step.A, last!, unitSize);
            return last;

        case ReadOpCode.DebugCondition:
            debug.BeginCondition(program.Fields[field], cursor.Position);
            return last;

        case ReadOpCode.DebugConditionActive:
            debug.ActivateCondition();
            return last;

        case ReadOpCode.DebugBoundedText:
            {
                long start = cursor.Position;
                string text = state.FixedText(cursor.ReadBoundedText(count, program.Fields[field].TypeSpelling));
                debug.Record(start, cursor.Position, debug.Member, text, program.Fields[field].DisplayTypeSpelling);
                Store(destination, program, field, text);
                return last;
            }

        case ReadOpCode.DebugNumericElements:
            {
                PrimitiveCodec codec = program.Codecs[step.A].Primitive;
                List<object?> elements = RecordNumericElements(ref cursor, debug, program.Fields[field], codec, count, scratch);
                Store(destination, program, field, PrimitiveArrayReader.FromBoxed(PrimitiveArrayReader.GetElementType(codec), elements));
                return last;
            }

        case ReadOpCode.DebugNumericElementList:
            Store(destination, program, field, RecordNumericElements(ref cursor, debug, program.Fields[field], program.Codecs[step.A].Primitive, count, scratch));
            return last;

        case ReadOpCode.DebugCodecArray:
        case ReadOpCode.DebugSkipElements:
            Store(destination, program, field, RecordCodecElements(ref cursor, debug, program.Fields[field], program.Codecs[step.A].Primitive, count, scratch));
            return last;

        case ReadOpCode.DebugCharArray:
            Store(destination, program, field, state.FixedText(new string(RecordCharacters(ref cursor, debug, program.Fields[field], program.Codecs[step.A].Primitive, count, scratch))));
            return last;

        case ReadOpCode.DebugWideCharArray:
            {
                CompiledField member = program.Fields[field];
                string text = state.FixedText(new string(RecordCharacters(ref cursor, debug, member, program.Codecs[step.A].Primitive, count, scratch)));
                PrimitiveCodecs.ValidateWideText(text, state.Layout.GetWideCharacterEncoding(member));
                Store(destination, program, field, text);
                return last;
            }

        case ReadOpCode.DebugCharTable:
            {
                CompiledField member = program.Fields[field];
                Store(destination, program, field, CharacterRows(ref state, member, RecordCharacters(ref cursor, debug, member, program.Codecs[step.A].Primitive, count, scratch)));
                return last;
            }

        case ReadOpCode.DebugEnumArray:
            Store(destination, program, field, RecordEnumElements(ref cursor, debug, program.Fields[field], program.Codecs[step.A].Primitive, program.Enums[step.B], count, scratch));
            return last;

        case ReadOpCode.DebugCustomArray:
            {
                var elements = new List<object?>(count);
                DebugPath? path = debug.Member;
                string typeName = program.Fields[field].DisplayTypeSpelling;
                for (int index = 0; index < count; index++)
                {
                    long start = cursor.Position;
                    object value = ReadCustomValue(ref cursor, ref state, program, field, step.A);
                    debug.Record(start, cursor.Position, path, value, typeName);
                    elements.Add(value);
                }

                Store(destination, program, field, elements);
                return last;
            }

        case ReadOpCode.DebugStruct:
            {
                ReadProgram nested = program.Nested[step.A];
                string? outer = state.QualifiedPrefix;
                if (step.B >= 0)
                {
                    state.QualifiedPrefix = outer is null ? program.Prefixes[step.B] : outer + program.Prefixes[step.B];
                }

                var value = new StructValue(nested.Shape);
                ReadRecordedComposite(ref cursor, ref state, nested, value, debug.Member);
                state.QualifiedPrefix = outer;
                Store(destination, program, field, value);
                return last;
            }

        case ReadOpCode.DebugStructArray:
            Store(destination, program, field, RecordStructElements(ref cursor, ref state, program.Fields[field], program.Nested[step.A], count));
            return last;

        case ReadOpCode.DebugUnion:
            {
                string? outer = state.QualifiedPrefix;
                if (step.B >= 0)
                {
                    state.QualifiedPrefix = outer is null ? program.Prefixes[step.B] : outer + program.Prefixes[step.B];
                }

                UnionValue union = ReadRecordedUnion(ref cursor, ref state, program.Nested[step.A], promoted: false, debug.Member);
                state.QualifiedPrefix = outer;
                Store(destination, program, field, union);
                return last;
            }

        case ReadOpCode.DebugPromotedUnion:
            {
                // An anonymous union adds no segment: its views and its own record take the enclosing composite's path.
                UnionValue union = ReadRecordedUnion(ref cursor, ref state, program.Nested[step.A], promoted: true, debug.Path);
                IDictionary<string, object?> members = destination;
                foreach (KeyValuePair<string, object?> member in union.Members)
                {
                    members[member.Key] = member.Value;
                }

                return last;
            }

        case ReadOpCode.DebugUnionArray:
            {
                CompiledField member = program.Fields[field];
                DebugPath? path = debug.Member;
                var elements = new List<object?>(count);
                for (int index = 0; index < count; index++)
                {
                    elements.Add(ReadRecordedUnion(ref cursor, ref state, program.Nested[step.A], promoted: false, DebugRecorder.ElementPath(member, path, index, count)));
                }

                Store(destination, program, field, elements);
                return last;
            }

        case ReadOpCode.DebugPointer:
            {
                Pointer pointer = ReadRecordedPointer(ref cursor, ref state, program.Fields[field], program.PointerTargets[step.A], step.B == 1, debug.Member, scratch);
                Store(destination, program, field, pointer);
                return pointer;
            }

        case ReadOpCode.DebugPointerArray:
            {
                CompiledField member = program.Fields[field];
                DebugPath? path = debug.Member;
                var pointers = new List<object?>(count);
                for (int index = 0; index < count; index++)
                {
                    // A pointer to a struct or union is recorded, with its target's members, under its element path.
                    DebugPath? element = member.TargetComposite is not null ? DebugRecorder.ElementPath(member, path, index, count) : path;
                    pointers.Add(ReadRecordedPointer(ref cursor, ref state, member, program.PointerTargets[step.A], step.B == 1, element, scratch));
                }

                Store(destination, program, field, pointers);
                return last;
            }

        case ReadOpCode.DebugRootStruct:
            {
                ReadProgram nested = program.Nested[step.A];
                var value = new StructValue(nested.Shape);

                // The interpreter attaches a root's value before reading its members; the compiler checked the slot.
                _ = program.Shape.TryGetIndex(program.Name, out int slot);
                destination.StoreSlot(slot, value);
                ReadRecordedComposite(ref cursor, ref state, nested, value, new DebugPath(null, program.Name));
                return last;
            }

        case ReadOpCode.DebugRootUnion:
            {
                UnionValue union = ReadRecordedUnion(ref cursor, ref state, program.Nested[step.A], promoted: false, new DebugPath(null, program.Name));
                _ = program.Shape.TryGetIndex(program.Name, out int slot);
                destination.StoreSlot(slot, union);
                return last;
            }

        default:
            throw new InvalidOperationException("The compiled engine has no executor for read step " + step.Op + ".");
        }
    }

    /// <summary>
    ///     Records the value the previous read step produced, under the member's path and display type: the value from the
    ///     marked start to the position, an enum result's number, or a bitfield over its whole storage unit.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, just after the value (or back at a shared bitfield unit's start).</param>
    /// <param name="debug">The recorder.</param>
    /// <param name="member">The member read.</param>
    /// <param name="kind">What the record holds.</param>
    /// <param name="value">The value read.</param>
    /// <param name="unitSize">The bitfield's storage unit size in bytes.</param>
    private static void RecordValue<TCursor>(ref TCursor cursor, DebugRecorder debug, CompiledField member, DebugRecordKind kind, object value, int unitSize)
        where TCursor : struct, IReadCursor
    {
        switch (kind)
        {
        case DebugRecordKind.EnumNumber:
            debug.Record(debug.Start, cursor.Position, debug.Member, ((EnumValueResult)value).Value, member.DisplayTypeSpelling);
            break;
        case DebugRecordKind.Bitfield:
            // The whole unit was read; the position may already be back at its start for the next bitfield in it.
            debug.Record(debug.Start, debug.Start + unitSize, debug.Member, value, member.DisplayTypeSpelling);
            break;
        default:
            debug.Record(debug.Start, cursor.Position, debug.Member, value, member.DisplayTypeSpelling);
            break;
        }
    }

    /// <summary>
    ///     Reads count fixed-width numbers one at a time, as the interpreter's debug parse reads every numeric element
    ///     (straight from memory when the value is there, else through the codec's reads), with a record each.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="debug">The recorder, whose member path the records carry.</param>
    /// <param name="member">The array member.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="count">The element count.</param>
    /// <param name="scratch">A buffer of at least one element.</param>
    /// <returns>The boxed elements.</returns>
    private static List<object?> RecordNumericElements<TCursor>(ref TCursor cursor, DebugRecorder debug, CompiledField member, PrimitiveCodec codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        Span<byte> element = scratch[..codec.Size];
        DebugPath? path = debug.Member;
        string typeName = member.DisplayTypeSpelling;
        for (int index = 0; index < count; index++)
        {
            long start = cursor.Position;
            object value = codec.ReadNumeric(cursor.ReadFixed(element));
            debug.Record(start, cursor.Position, path, value, typeName);
            elements.Add(value);
        }

        return elements;
    }

    /// <summary>
    ///     Reads count elements of a codec with a record each: through the codec's reader, or, for the fixed-width numbers of
    ///     unnamed padding, as a numeric element is read.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="debug">The recorder, whose member path the records carry.</param>
    /// <param name="member">The array member.</param>
    /// <param name="codec">The element codec.</param>
    /// <param name="count">The element count.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    /// <returns>The elements.</returns>
    private static List<object?> RecordCodecElements<TCursor>(ref TCursor cursor, DebugRecorder debug, CompiledField member, PrimitiveCodec codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        DebugPath? path = debug.Member;
        string typeName = member.DisplayTypeSpelling;
        for (int index = 0; index < count; index++)
        {
            long start = cursor.Position;
            object value = codec.IsFixedWidthNumeric ? codec.ReadNumeric(cursor.ReadFixed(scratch[..codec.Size])) : ReadCodecValue(ref cursor, codec, scratch);
            debug.Record(start, cursor.Position, path, value, typeName);
            elements.Add(value);
        }

        return elements;
    }

    /// <summary>Reads count characters one at a time through their codec reader, with a record per character.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="debug">The recorder, whose member path the records carry.</param>
    /// <param name="member">The character array member.</param>
    /// <param name="codec">The character codec.</param>
    /// <param name="count">The character count.</param>
    /// <param name="scratch">A scratch buffer.</param>
    /// <returns>The characters as read, untrimmed.</returns>
    private static char[] RecordCharacters<TCursor>(ref TCursor cursor, DebugRecorder debug, CompiledField member, PrimitiveCodec codec, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        char[] characters = new char[count];
        DebugPath? path = debug.Member;
        string typeName = member.DisplayTypeSpelling;
        for (int index = 0; index < count; index++)
        {
            long start = cursor.Position;
            object character = ReadCodecValue(ref cursor, codec, scratch);
            debug.Record(start, cursor.Position, path, character, typeName);
            characters[index] = (char)character;
        }

        return characters;
    }

    /// <summary>Reads count enum or flag values through their storage codec, recording each value's number.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="debug">The recorder, whose member path the records carry.</param>
    /// <param name="member">The array member.</param>
    /// <param name="codec">The storage codec.</param>
    /// <param name="enumType">The enum type.</param>
    /// <param name="count">The element count.</param>
    /// <param name="scratch">A scratch buffer.</param>
    /// <returns>The enum results.</returns>
    private static List<object?> RecordEnumElements<TCursor>(ref TCursor cursor, DebugRecorder debug, CompiledField member, PrimitiveCodec codec, CompiledEnumType enumType, int count, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        var elements = new List<object?>(count);
        DebugPath? path = debug.Member;
        string typeName = member.DisplayTypeSpelling;
        for (int index = 0; index < count; index++)
        {
            long start = cursor.Position;
            EnumValueResult value = CStruct.CreateEnumValue(enumType, ReadCodecValue(ref cursor, codec, scratch));
            debug.Record(start, cursor.Position, path, value.Value, typeName);
            elements.Add(value);
        }

        return elements;
    }

    /// <summary>
    ///     Reads count structs, each a value of its own with a fresh conditional selection, whose members are recorded under
    ///     the element's path; a multidimensional array's flat elements are nested afterwards.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state, which holds the recorder.</param>
    /// <param name="member">The array member.</param>
    /// <param name="element">The element struct's debug program.</param>
    /// <param name="count">The number of elements in every dimension together.</param>
    /// <returns>The flat elements.</returns>
    private static List<object?> RecordStructElements<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField member, ReadProgram element, int count)
        where TCursor : struct, IReadCursor
    {
        DebugPath? path = state.Debug!.Member;
        var elements = new List<object?>(count);
        for (int index = 0; index < count; index++)
        {
            var value = new StructValue(element.Shape);
            ReadRecordedComposite(ref cursor, ref state, element, value, DebugRecorder.ElementPath(member, path, index, count));
            elements.Add(value);
        }

        return elements;
    }

    /// <summary>Reads a struct into its value with its members recorded under <paramref name="path"/>, restoring the enclosing composite's path after it.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the struct's first byte.</param>
    /// <param name="state">The operation's state, which holds the recorder.</param>
    /// <param name="program">The struct's debug program.</param>
    /// <param name="value">The struct's new, empty value.</param>
    /// <param name="path">The struct's path.</param>
    private static void ReadRecordedComposite<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, StructValue value, DebugPath? path)
        where TCursor : struct, IReadCursor
    {
        DebugRecorder debug = state.Debug!;
        DebugPath? outer = debug.Path;
        debug.Path = path;
        ReadComposite(ref cursor, ref state, program, value);
        debug.Path = outer;
    }

    /// <summary>
    ///     Reads a union with its views recorded under <paramref name="path"/>, then records the union itself: its whole
    ///     storage range, its value and its raw storage, after the views, as the interpreter does.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the union's first byte.</param>
    /// <param name="state">The operation's state, which holds the recorder.</param>
    /// <param name="program">The union's debug program.</param>
    /// <param name="promoted">Whether the union is an anonymous member whose views belong to its parent.</param>
    /// <param name="path">The union's path: its own, or the enclosing composite's for an anonymous union.</param>
    /// <returns>The union value.</returns>
    private static UnionValue ReadRecordedUnion<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadProgram program, bool promoted, DebugPath? path)
        where TCursor : struct, IReadCursor
    {
        DebugRecorder debug = state.Debug!;
        long start = cursor.Position;
        DebugPath? outer = debug.Path;
        debug.Path = path;
        UnionValue union = ReadUnion(ref cursor, ref state, program, promoted);
        debug.Path = outer;
        debug.RecordUnion(start, cursor.Position, path, union, program.Composite!.Name);
        return union;
    }

    /// <summary>
    ///     Reads a pointer (or one element of a pointer array) and records it under <paramref name="path"/> once it is read:
    ///     a target followed in place is read first, its records under the same path, and a deferred target keeps the path for
    ///     when its struct follows it.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the stored address.</param>
    /// <param name="state">The operation's state, which holds the recorder.</param>
    /// <param name="member">The pointer member.</param>
    /// <param name="target">The pointer's target description.</param>
    /// <param name="deferred">Whether the target is followed after the struct's last member.</param>
    /// <param name="path">The pointer's path, which its target's records share.</param>
    /// <param name="scratch">A buffer of at least 16 bytes.</param>
    /// <returns>The pointer as stored in the result.</returns>
    private static Pointer ReadRecordedPointer<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField member, ReadPointerTarget target, bool deferred, DebugPath? path, Span<byte> scratch)
        where TCursor : struct, IReadCursor
    {
        DebugRecorder debug = state.Debug!;
        long start = cursor.Position;
        int queued = state.PendingPointerCount;
        debug.Target = path;
        Pointer pointer = ReadPointerField(ref cursor, ref state, target, deferred, scratch);
        if (state.PendingPointerCount > queued)
        {
            // The pointer was queued: its target is recorded under this path when the struct follows it.
            List<PendingPointer> pending = state.Pointers.Pending;
            pending[^1] = pending[^1] with { DebugStack = path, };
        }

        debug.Record(start, cursor.Position, path, pointer, member.DisplayTypeSpelling);
        return pointer;
    }

    /// <summary>
    ///     Reads a pointer's struct or union target in a debug parse, as <see cref="ReadPointerComposite{TCursor}"/> does,
    ///     through the composite's debug program, with the target's records under <paramref name="path"/>.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, at the target.</param>
    /// <param name="state">The operation's state, which holds the recorder.</param>
    /// <param name="target">The target description of a debug program; its composite's debug program is taken on first use.</param>
    /// <param name="path">The pointer's path (or a counted target's element path).</param>
    /// <returns>The struct or union value.</returns>
    private static object ReadRecordedPointerComposite<TCursor>(ref TCursor cursor, ref ReadEngineState state, ReadPointerTarget target, DebugPath? path)
        where TCursor : struct, IReadCursor
    {
        ReadProgram program = target.Program ??= state.Slots.Table.DebugReadPrograms.GetComposite(state.Layout.Compilation, target.Composite!).Program!;
        if (program.Kind == ReadProgramKind.Union)
        {
            return ReadRecordedUnion(ref cursor, ref state, program, promoted: false, path);
        }

        var value = new StructValue(program.Shape);
        ReadRecordedComposite(ref cursor, ref state, program, value, path);
        return value;
    }

    /// <summary>
    ///     Makes a multidimensional character array's rows: each innermost row a string - trimmed as the options say, and
    ///     valid UTF-16 for <c>wchar</c>, checked row by row - nested by the outer dimensions, as the interpreter shapes the
    ///     array after reading its characters.
    /// </summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="member">The array member.</param>
    /// <param name="characters">Every row's characters, in order.</param>
    /// <returns>The rows, nested in lists by the outer dimensions.</returns>
    /// <exception cref="CStructReadException">A <c>wchar</c> row is not valid UTF-16.</exception>
    private static List<object?> CharacterRows(ref ReadEngineState state, CompiledField member, char[] characters)
    {
        // The interpreter's row loop: its capacity divides by the row size, as a zero-length row fails there too.
        int[] sizes = CStruct.FixedDimensionSizes(member);
        int rowSize = sizes[^1];
        var rows = new List<object?>(characters.Length / rowSize);
        for (int start = 0; start < characters.Length; start += rowSize)
        {
            string text = state.FixedText(new string(characters, start, rowSize));
            if (member.IsWideCharElement)
            {
                PrimitiveCodecs.ValidateWideText(text, state.Layout.GetWideCharacterEncoding(member));
            }

            rows.Add(text);
        }

        return CStruct.ReshapeFlatArrayValues(rows, sizes[..^1]);
    }
}
