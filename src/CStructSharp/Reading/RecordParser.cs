namespace CStructSharp.Reading;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Generated;
using CStructSharp.Streams;
using CStructSharp.Values;

/// <summary>
///     The runtime's side of a record sequence: the <see cref="RecordReader{T}"/> that parses one record as its own
///     region with the stream core over a pinned slice, and the synchronous stream form that runs the stream reader
///     over the caller's stream one record per step. The sequence rules live in <see cref="RecordSequence"/>.
/// </summary>
/// <remarks>
///     In the memory, sequence, and asynchronous forms a stored absolute pointer address counts from the record's
///     own first byte; the synchronous stream form counts from the stream's first byte, as <c>Parse(Stream)</c>.
/// </remarks>
internal static class RecordParser
{
    /// <summary>The reader that parses one record of <paramref name="root"/> from a slice of the input.</summary>
    public static RecordReader<StructValue> Reader(CStruct layout, RecordRoot root, LayoutVariableInput variables)
    {
        return (ReadOnlyMemory<byte> source, int offset, int index, long shift, ReadOptions? options, out int consumed) => ParseAt(layout, source, offset, index, shift, root, variables, options, out consumed);
    }

    /// <summary>The records of a seekable stream, read with the stream reader from the current position to the end; the stream is left after the last record read, or where a failed read stopped.</summary>
    public static IEnumerable<StructValue> FromStream(CStruct layout, Stream stream, RecordRoot root, LayoutVariableInput variables, ReadOptions? options)
    {
        for (int index = 0; stream.Position < stream.Length; index++)
        {
            long start = stream.Position;
            long remaining = stream.Length - start;
            if (root.Size is { } size && remaining < size)
            {
                throw RecordSequence.Partial(remaining, size, root.Name, index, start);
            }

            StructValue record;
            try
            {
                record = layout.ParseRecordCore(stream, root.Name, variables, options);
            }
            catch (CStructException failure)
            {
                RecordSequence.Complete(failure, index, 0);
                throw;
            }

            if (stream.Position == start)
            {
                throw RecordSequence.Empty(root.Name, index, start);
            }

            yield return record;
        }
    }

    /// <summary>Parses the record at <paramref name="offset"/> as its own region; a failure names the record and carries the input's coordinate.</summary>
    private static unsafe StructValue ParseAt(CStruct layout, ReadOnlyMemory<byte> source, int offset, int index, long shift, RecordRoot root, LayoutVariableInput variables, ReadOptions? options, out int consumed)
    {
        try
        {
            fixed (byte* pointer = &MemoryMarshal.GetReference(source.Span))
            {
                using var region = new FixedBufferStream(pointer + offset, source.Length - offset, writable: false);
                StructValue record = layout.ParseRecordCore(region, root.Name, variables, options);
                consumed = (int)region.Position;
                return record;
            }
        }
        catch (CStructException failure)
        {
            RecordSequence.Complete(failure, index, shift + offset);
            throw;
        }
    }
}
