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
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>The resolver's measuring half: where a member, a struct or a composite the walk passes ends, reading only what its size depends on, and the traversal limits a walk into a composite checks first.</summary>
internal static partial class TargetResolver
{
    /// <summary>
    ///     Measures where a member the walk passes ends, reading only what its size depends on: a struct (or each struct of
    ///     an array of them) member by member, capturing as it goes; terminated text, an unsized character array, LEB128 and a
    ///     variable-size caller's codec by reading each value; anything else from its count and element size, without
    ///     reading or charging its bytes. A terminated array's all-zero terminator element belongs to the member.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="member">The member.</param>
    /// <param name="fieldStart">The member's first byte.</param>
    /// <returns>The position just after the member.</returns>
    /// <exception cref="CStructException">A value measured is short or invalid, or a limit is exceeded.</exception>
    /// <exception cref="OverflowException">The member's extent does not fit a signed position.</exception>
    private static long MeasureFieldEnd<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetMember member, long fieldStart)
        where TCursor : struct, IReadCursor
    {
        CompiledField field = member.Field;

        // A pointer to a struct occupies the pointer width, never the pointee's extent, so pointers fall through to the
        // fixed-size arithmetic below.
        if (field.Composite is { } nested)
        {
            int count = field.Array.Kind == CompiledArrayKind.Scalar ? 1 : Count(ref cursor, ref state, field, member, fieldStart, allDimensions: true);

            // Every element is walked, even when its size is fixed: a later member may count by a value inside one.
            long current = fieldStart;
            string? outerPrefix = state.QualifiedPrefix;
            if (field.HasQualifiedPrefix && field.Array.Kind == CompiledArrayKind.Scalar)
            {
                // A member named through a dotted path (`hdr.n`) republishes its members' values under the prefix.
                state.QualifiedPrefix = outerPrefix is null ? field.QualifiedPrefix : outerPrefix + field.QualifiedPrefix;
            }

            TargetProgram program = NestedProgram(ref state, walk, member, nested);
            if (field.IsPromotedComposite)
            {
                // A promoted member's members belong to the current struct's nesting level.
                cursor.ThrowIfCancellationRequested();
                current = MeasureCompositeEnd(ref cursor, ref state, walk, program, current);
            }
            else
            {
                for (int index = 0; index < count; index++)
                {
                    current = MeasureStructEnd(ref cursor, ref state, walk, program, current);
                }
            }

            state.QualifiedPrefix = outerPrefix;
            return field.Array.Kind == CompiledArrayKind.Terminated ? checked(current + (field.FixedElementSize ?? 0)) : current;
        }

        Span<byte> scratch = stackalloc byte[ScratchSize];
        if (field.Array.Kind == CompiledArrayKind.Scalar && field.Codec.IsTerminatedText)
        {
            cursor.Position = fieldStart;
            _ = ReadEngine.ReadCodecValue(ref cursor, field.Codec, scratch);
            return cursor.Position;
        }

        if (field.Array.Kind == CompiledArrayKind.Flexible)
        {
            if (!field.IsCharacterArray)
            {
                throw new CStructLayoutException("Only character fields can use an unsized array declarator: " + field.Name);
            }

            if (field.TerminatedCodecId < 0)
            {
                throw new InvalidOperationException("Compiled unsized character array has no reader: " + field.Name);
            }

            cursor.Position = fieldStart;
            _ = ReadEngine.ReadCodecValue(ref cursor, PrimitiveCodec.Resolve(field.DisplayTypeSpelling, field.LayoutLittleEndian), scratch);
            return cursor.Position;
        }

        if (ReadsToMeasure(member))
        {
            // Variable-length integers, terminated strings and variable-size caller codecs are measured by reading every
            // value.
            int count = field.Array.Kind == CompiledArrayKind.Scalar ? 1 : Count(ref cursor, ref state, field, member, fieldStart, allDimensions: true);
            cursor.Position = fieldStart;
            for (int index = 0; index < count; index++)
            {
                _ = ReadThroughCodec(ref cursor, ref state, field, scratch);
            }

            return cursor.Position;
        }

        int scalarCount = field.Array.Kind == CompiledArrayKind.Scalar ? 1 : Count(ref cursor, ref state, field, member, fieldStart, allDimensions: true);
        int elementSize = field.FixedElementSize ?? ElementSize(ref state, field);
        if (field.Array.Kind == CompiledArrayKind.Terminated)
        {
            // The terminator element is part of the field's extent.
            scalarCount = CountStoredTerminatedElements(scalarCount);
        }

        return checked(fieldStart + checked(elementSize * scalarCount));
    }

    /// <summary>
    ///     Whether a member's values are read to find where they end: LEB128, terminated text and a caller's codec without a
    ///     fixed size, whose size the data decides. A caller's codec with a fixed size occupies exactly that size.
    /// </summary>
    /// <param name="member">The member.</param>
    /// <returns>Whether each value is read.</returns>
    private static bool ReadsToMeasure(TargetMember member)
    {
        CompiledField field = member.Field;
        PrimitiveCodec codec = field.Codec;
        return codec.IsLeb128 || codec.IsTerminatedText || (codec.IsCustom && !field.FixedElementSize.HasValue);
    }

    /// <summary>Measures one struct or union inside one claimed nesting level (cancellation first, then the depth limit).</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="program">The composite's walk.</param>
    /// <param name="start">Its first byte.</param>
    /// <returns>The position just after it, tail padding included.</returns>
    private static long MeasureStructEnd<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetProgram program, long start)
        where TCursor : struct, IReadCursor
    {
        state.EnterStructure(ref cursor);
        try
        {
            return MeasureCompositeEnd(ref cursor, ref state, walk, program, start);
        }
        finally
        {
            state.StructureDepth--;
        }
    }

    /// <summary>
    ///     Measures a struct or union whose level is claimed. A union's counts are checked against the limits and its size
    ///     taken from the variables, reading nothing; a struct's active members are placed, captured and measured in
    ///     declaration order under its conditional selection and scope, and its end padded to its alignment.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="program">The composite's walk.</param>
    /// <param name="start">Its first byte.</param>
    /// <returns>The position just after it.</returns>
    private static long MeasureCompositeEnd<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetProgram program, long start)
        where TCursor : struct, IReadCursor
    {
        CompiledCompositeType composite = program.Composite;
        if (composite.IsUnion)
        {
            ValidateTraversalLimits(ref cursor, ref state, walk, program);
            int size = composite.Symbol.FixedSize ?? state.Layout.Compilation.SizeQueries.GetCompiledStructSizeInBytes(composite, state.Slots.AsDictionary(), false);
            return checked(start + size);
        }

        var placer = new PlacementCursor(start, state.Layout.Aligned, state.Layout.BitfieldPacking, state.Layout.Compilation.HighBitFirst);
        ConditionalScopeSlots? scope = program.Scope;
        if (scope is not null)
        {
            foreach (int slot in scope.ClearedSlots)
            {
                state.Slots.Set(slot, SlotValue.Undefined);
            }
        }

        Span<int> arms = program.GroupCount <= FrameArena.StackArmLimit ? stackalloc int[FrameArena.StackArmLimit] : new int[program.GroupCount];
        arms[..program.GroupCount].Fill(FrameArena.Undecided);
        int locals = scope is { LocalCount: > 0, } ? state.TakeLocals(scope.LocalCount) : -1;
        try
        {
            TargetMember[] members = program.Members;
            for (int index = 0; index < members.Length; index++)
            {
                TargetMember member = members[index];
                CompiledField field = member.Field;
                if (!IsActive(ref state, program, member, arms))
                {
                    continue;
                }

                (long fieldStart, int bitOffset, int unitSize) = CompositeFieldPlacementCursor.AdvanceToField(ref placer, field);
                if (field.IsZeroWidthBitfield)
                {
                    continue;
                }

                bool readByCapture = Capture(ref cursor, ref state, member, fieldStart, bitOffset, unitSize);
                if (field.BitSize == 0)
                {
                    // A variable-length value the capture read ends where the capture left the cursor; reading it again to
                    // measure it would read and charge its bytes twice.
                    placer.CompleteField(readByCapture && ReadsToMeasure(member) ? cursor.Position : MeasureFieldEnd(ref cursor, ref state, walk, member, fieldStart));
                }

                if (scope is not null)
                {
                    state.CompleteMember(scope, index, locals);
                }
            }

            return placer.Finish(composite.Symbol.Alignment)!.Value;
        }
        finally
        {
            if (locals >= 0)
            {
                state.ReleaseLocals(locals);
            }
        }
    }

    /// <summary>
    ///     Checks the array and nesting work inside a union without reading its bytes: every fixed or runtime count of its
    ///     members (and of their structs' members, whatever arm they sit in) against the element limit, and every nested
    ///     struct against the depth limit. A data-sized array's count needs a position, so only its element type is checked.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor, which carries the cancellation token.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="program">The composite's walk.</param>
    private static void ValidateTraversalLimits<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetProgram program)
        where TCursor : struct, IReadCursor
    {
        foreach (TargetMember member in program.Members)
        {
            CompiledField field = member.Field;
            int count = field.Array.Kind is CompiledArrayKind.Scalar or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated
                            ? 1
                            : Count(ref cursor, ref state, field, member, 0, allDimensions: false);
            if (count == 0 || field.Composite is not { } nested)
            {
                continue;
            }

            TargetProgram nestedProgram = NestedProgram(ref state, walk, member, nested);
            if (field.IsPromotedComposite)
            {
                // A promoted member's members belong to this composite's level.
                ValidateTraversalLimits(ref cursor, ref state, walk, nestedProgram);
                continue;
            }

            state.EnterStructure(ref cursor);
            try
            {
                ValidateTraversalLimits(ref cursor, ref state, walk, nestedProgram);
            }
            finally
            {
                state.StructureDepth--;
            }
        }
    }
}
