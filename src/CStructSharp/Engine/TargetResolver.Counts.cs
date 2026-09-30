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

/// <summary>The resolver's counting half: where an element starts, how many elements an array holds, and the size of an element without a fixed size.</summary>
internal static partial class TargetResolver
{
    /// <summary>
    ///     Returns the element count of an array shape, checked against the element limit as the engine checks a count
    ///     (<see cref="ReadEngine.CheckCount"/>): a data-sized array counted from the data, a runtime count evaluated (a
    ///     negative count fails naming the array), a fixed shape's outermost count - or, for a walk over every element, the
    ///     product of all its dimensions - and 1 for a scalar.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array shape at the dimension counted.</param>
    /// <param name="member">The member the shape belongs to, whose count program a runtime count evaluates.</param>
    /// <param name="start">The array's first byte, where a data-sized array is counted from.</param>
    /// <param name="allDimensions">Whether a fixed shape counts every element of every dimension.</param>
    /// <returns>The count.</returns>
    /// <exception cref="CStructLayoutException">The array is an unsized character array, which has no count.</exception>
    /// <exception cref="CStructReadException">The count is negative, or a data-sized array does not fit the data.</exception>
    /// <exception cref="CStructReadLimitException">The count exceeds the element limit.</exception>
    private static int Count<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField field, TargetMember member, long start, bool allDimensions)
        where TCursor : struct, IReadCursor
    {
        if (field.Array.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
        {
            return CountDataSized(ref cursor, ref state, field, start);
        }

        Int128 count = field.Array.Kind switch
        {
            CompiledArrayKind.Scalar => 1,
            CompiledArrayKind.Flexible => throw new CStructLayoutException("Flexible array has no fixed storage size: " + field.Name),
            CompiledArrayKind.Runtime => state.Slots.Evaluate(member.Count!, member.CountContext, ExpressionFailureDomain.Read),
            _ => allDimensions ? FixedTotal(field) : field.Array.FixedCount!.Value,
        };
        return ReadEngine.CheckCount(count, field, state.MaxArrayElements);
    }

    /// <summary>
    ///     Returns where one element of an array (or row of a multidimensional one) starts: the array's start for index 0;
    ///     one multiplication when every element has a fixed size; otherwise the elements before it are measured - structs
    ///     member by member, variable-length values by reading them.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="walk">The resolution's path and programs.</param>
    /// <param name="member">The array member.</param>
    /// <param name="field">The array shape at the dimension indexed.</param>
    /// <param name="element">One element of <paramref name="field"/> (the shape one dimension deeper).</param>
    /// <param name="start">The array's (or row's) first byte.</param>
    /// <param name="index">The element's index, already checked against the count.</param>
    /// <returns>The element's first byte.</returns>
    private static long ElementStart<TCursor>(ref TCursor cursor, ref ReadEngineState state, in PathWalk walk, TargetMember member, CompiledField field, CompiledField element, long start, int index)
        where TCursor : struct, IReadCursor
    {
        if (index == 0)
        {
            return start;
        }

        // A fixed element size makes the start one multiplication: resolving an element never continues to a later
        // sibling, and nothing inside a fixed-size element depends on a value captured in an earlier one.
        if (element.FixedStorageSize is int stride)
        {
            return checked(start + ((long)stride * index));
        }

        if (field.Composite is { } nested)
        {
            // One step at this dimension skips as many leaf structs as one element holds.
            TargetProgram program = NestedProgram(ref state, walk, member, nested);
            int leaves = checked(index * (element.Array.TotalFixedElementCount ?? 1));
            long current = start;
            for (int leaf = 0; leaf < leaves; leaf++)
            {
                current = MeasureStructEnd(ref cursor, ref state, walk, program, current);
            }

            return current;
        }

        if (ReadsToMeasure(member))
        {
            Span<byte> scratch = stackalloc byte[ScratchSize];
            cursor.Position = start;
            int values = checked(index * (element.Array.TotalFixedElementCount ?? 1));
            for (int value = 0; value < values; value++)
            {
                _ = ReadThroughCodec(ref cursor, ref state, field, scratch);
            }

            return cursor.Position;
        }

        return checked(start + ((long)ElementSize(ref state, field) * index));
    }

    /// <summary>The product of a fixed array's dimensions, exact in the expression domain, so a total past <see cref="int"/> fails the element limit naming its value.</summary>
    /// <param name="field">The fixed array.</param>
    /// <returns>The total.</returns>
    private static Int128 FixedTotal(CompiledField field)
    {
        Int128 total = 1;
        foreach (CompiledArrayDimension dimension in field.Array.Dimensions)
        {
            total = checked(total * dimension.FixedCount!.Value);
        }

        return total;
    }

    /// <summary>
    ///     Counts a data-sized array from its first byte - whole elements to the end of the input, or elements before the
    ///     first all-zero one (read and charged) - and restores the position the walk was at, whatever happens.
    /// </summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The operation's cursor.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The array.</param>
    /// <param name="start">The array's first byte.</param>
    /// <returns>The element count, without the terminator.</returns>
    private static int CountDataSized<TCursor>(ref TCursor cursor, ref ReadEngineState state, CompiledField field, long start)
        where TCursor : struct, IReadCursor
    {
        int elementSize = field.FixedElementSize ?? throw new InvalidOperationException("Data-sized array has no fixed element size: " + field.Name);
        long position = cursor.Position;
        try
        {
            return field.Array.Kind == CompiledArrayKind.ToEnd
                       ? DynamicArrayExtent.CountToEnd(ref cursor, start, elementSize, state.MaxArrayElements, field.Name)
                       : DynamicArrayExtent.CountTerminated(ref cursor, start, elementSize, state.MaxArrayElements, field.Name);
        }
        finally
        {
            cursor.Position = position;
        }
    }

    /// <summary>The size of one element of a field without a fixed element size: a runtime-sized struct measured from the variables.</summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="field">The field.</param>
    /// <returns>The element size in bytes.</returns>
    /// <exception cref="CStructLayoutException">The element type has no storage size.</exception>
    private static int ElementSize(ref ReadEngineState state, CompiledField field)
        => field.FixedElementSize ?? state.Layout.Compilation.SizeQueries.GetCompiledFieldElementSize(field, state.Slots.AsDictionary(), false);
}
