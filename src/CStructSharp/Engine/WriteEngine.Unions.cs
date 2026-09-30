namespace CStructSharp.Engine;

using System;
using System.Buffers;
using System.IO;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The unions of the compiled engine's writer and the end of a struct with bitfields. A union is written in two
///     steps: its whole storage is staged away from the destination - zeroes (or, under update semantics
///     that keep union storage, the union's existing bytes) with the selected member written over them from the union's
///     first byte, with a budget of its own and a copy of the variables - and then written to the destination once, so a
///     member that cannot be written leaves the destination unchanged.
/// </summary>
internal static partial class WriteEngine
{
    /// <summary>
    ///     Writes a named union from its value: the value must be a
    ///     <see cref="UnionValue"/> of this union, raw storage must have the union's size, and a selected member is staged.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, at the union's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="union">The union's program.</param>
    /// <param name="data">The union's bound value.</param>
    /// <exception cref="CStructWriteException">The value is not a selection of this union, or its member cannot be written.</exception>
    private static void WriteUnion<TDestination>(ref TDestination destination, ref WriteEngineState state, WriteProgram union, object data)
        where TDestination : struct, IWriteDestination
    {
        CompiledCompositeType composite = union.Composite!;
        if (data is not UnionValue unionValue)
        {
            throw new CStructWriteException(WriteFailures.WholeUnionNeedsSelection(composite.Name, "UnionValue.FromRaw or UnionValue.FromMember"));
        }

        if (!string.Equals(unionValue.UnionName, composite.Name, StringComparison.Ordinal))
        {
            throw new CStructWriteException($"Union value '{unionValue.UnionName}' cannot be written as '{composite.Name}'.");
        }

        int size = UnionSize(ref state, composite);
        byte[]? raw = unionValue.HasRawStorage ? unionValue.GetRawStorageArray() : null;
        if (raw is not null && raw.Length != size)
        {
            throw new CStructWriteException(WriteFailures.RawStorageLengthMismatch(composite.Name, size, raw.Length));
        }

        long start = destination.Position;
        long end = checked(start + size);
        if (!unionValue.HasSelection)
        {
            destination.Write(raw!);
            destination.Position = end;
            return;
        }

        string selected = unionValue.SelectedMember!;
        int member = Array.FindIndex(union.Fields, field => string.Equals(field.Name, selected, StringComparison.Ordinal));
        if (member < 0)
        {
            throw new CStructWriteException(WriteFailures.UnknownUnionMember(composite.Name, selected));
        }

        destination.Position = WriteStaged(ref destination, ref state, union, member, unionValue.SelectedValue!, start, size);
    }

    /// <summary>
    ///     Writes an anonymous promoted union from its parent's value: the member written is the widest one the data supplies (a member without a fixed size counts as the widest, the
    ///     first declared among equals; a promoted struct member counts when the data supplies any of its members), staged
    ///     as a named union's selection is. It claims no nesting level and observes no cancellation of its own.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, at the union's placed first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="union">The union's program.</param>
    /// <param name="data">The parent's data, which carries the union's members.</param>
    /// <exception cref="CStructWriteException">No member is supplied, or the member cannot be written.</exception>
    private static void WritePromotedUnion<TDestination>(ref TDestination destination, ref WriteEngineState state, WriteProgram union, object data)
        where TDestination : struct, IWriteDestination
    {
        CompiledCompositeType composite = union.Composite!;
        long start = destination.Position;
        int size = UnionSize(ref state, composite);
        int selected = -1;
        object? selectedValue = null;
        int selectedSize = -1;
        for (int index = 0; index < union.Fields.Length; index++)
        {
            CompiledField member = union.Fields[index];
            int memberSize = member.FixedStorageSize ?? int.MaxValue;
            if (memberSize <= selectedSize)
            {
                continue;
            }

            if (composite.PromotedFields.Contains(member))
            {
                if (CStruct.SuppliesAnyPromotedMember(member, data))
                {
                    (selected, selectedValue, selectedSize) = (index, data, memberSize);
                }
            }
            else if (member.Name.Length > 0 && WriteDataBinding.TryGetMemberValue(data, member.Name, out object? value))
            {
                (selected, selectedValue, selectedSize) = (index, value, memberSize);
            }
        }

        if (selected < 0)
        {
            throw new CStructWriteException("No member of the anonymous union was supplied; provide one of: " + string.Join(", ", composite.Shape.Names));
        }

        destination.Position = WriteStaged(ref destination, ref state, union, selected, selectedValue!, start, size);
    }

    /// <summary>
    ///     Stages one union member into a fresh extent of zeroes - or of the union's existing bytes, read back from the
    ///     destination, when the write keeps union storage - and writes the extent at the position. The member is written standalone from the extent's first byte by its segment of the
    ///     union's program, into a staging buffer with a budget of its own, under the operation's depth, with a copy of the
    ///     variables (nothing it captures escapes) and no qualified prefix. A failure that means the member cannot be
    ///     written - an invalid operation, argument, arithmetic, format, cast or unsupported operation - is
    ///     wrapped as such; the destination is written only after the member succeeded and the union's end is known to be
    ///     representable (an end past the largest position fails before the destination is touched).
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination, at the union's first byte.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="union">The union's program.</param>
    /// <param name="member">The member's index.</param>
    /// <param name="value">The member's value; for a promoted member, the data that carries its members.</param>
    /// <param name="start">The union's first byte in the destination, where the position is.</param>
    /// <param name="size">The union's size in bytes.</param>
    /// <returns>The union's end: its first byte plus its size.</returns>
    /// <exception cref="CStructReadException">The write keeps union storage and the destination does not hold the whole extent.</exception>
    /// <exception cref="OverflowException">The union's end is past the largest position.</exception>
    private static long WriteStaged<TDestination>(ref TDestination destination, ref WriteEngineState state, WriteProgram union, int member, object value, long start, int size)
        where TDestination : struct, IWriteDestination
    {
        byte[] storage = ArrayPool<byte>.Shared.Rent(size);
        try
        {
            if (state.PreservesUnionStorage)
            {
                // The existing extent must be read whole (a short read fails with the end of the stream as its cause),
                // and the position returns to the union's first byte either way.
                try
                {
                    destination.Stream.ReadExactly(storage.AsSpan(0, size));
                }
                catch (EndOfStreamException exception)
                {
                    throw new CStructReadException(WriteFailures.IncompleteUnionStorage, exception);
                }
                finally
                {
                    destination.Position = start;
                }
            }
            else
            {
                storage.AsSpan(0, size).Clear();
            }

            // The token is observed once more before the member is staged.
            state.CancellationToken.ThrowIfCancellationRequested();
            using (var staging = MemoryWriteBuffer.ForStaging(storage, size, state.Options))
            {
                var stagingDestination = new MemoryWriteDestination(staging);
                int saved = state.SaveSlots();
                string? prefix = state.QualifiedPrefix;
                int depth = state.StructureDepth;
                state.QualifiedPrefix = null;
                try
                {
                    RunFrame(ref stagingDestination, ref state, union, value, union.UnionEntries[member]);
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or ArithmeticException or
                                                  FormatException or InvalidCastException or NotSupportedException)
                {
                    string name = union.Name.Length > 0 ? union.Name + "." + union.Fields[member].Name : union.Fields[member].Name;
                    throw new CStructWriteException($"Cannot write selected union member '{name}'.", exception);
                }

                state.RestoreSlots(saved);
                state.QualifiedPrefix = prefix;
                state.StructureDepth = depth;
            }

            long end = checked(start + size);
            destination.Write(storage.AsSpan(0, size));
            return end;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(storage);
        }
    }

    /// <summary>
    ///     A union's size in bytes for a write: its fixed size, or the size its members'
    ///     expressions give under the operation's current variables.
    /// </summary>
    /// <param name="state">The operation's state.</param>
    /// <param name="composite">The union.</param>
    /// <returns>The size.</returns>
    private static int UnionSize(ref WriteEngineState state, CompiledCompositeType composite)
        => composite.Symbol.FixedSize ?? state.Layout.Compilation.SizeQueries.GetCompiledStructSizeInBytes(composite, state.Slots.ToDictionary(), false);

    /// <summary>
    ///     Ends a struct with bitfields: the position moves to where the runtime cursor ended (past a
    ///     unit a bitfield reserved), and in an aligned layout the tail padding up to the struct's alignment is written as
    ///     zeroes - or, under update semantics, moved past.
    /// </summary>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="destination">The operation's destination.</param>
    /// <param name="state">The operation's state.</param>
    /// <param name="placer">The frame's placement cursor.</param>
    /// <param name="alignment">The struct's alignment.</param>
    private static void FinishPlaced<TDestination>(ref TDestination destination, ref WriteEngineState state, ref PlacementCursor placer, int alignment)
        where TDestination : struct, IWriteDestination
    {
        long current = placer.Current!.Value;
        destination.Position = current;
        if (!state.Layout.Aligned)
        {
            return;
        }

        long end = placer.Finish(alignment)!.Value;
        if (end == current)
        {
            return;
        }

        int padding = checked((int)(end - current));
        if (state.UpdateSemantics)
        {
            destination.Position += padding;
        }
        else
        {
            destination.WriteZeroes(padding);
        }
    }
}
