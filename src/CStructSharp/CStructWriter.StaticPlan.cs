namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Streams;
using CStructSharp.Values;
using CStructSharp.Writing;

/// <summary>
///     Static write plan: a fully fixed composite is encoded into one block of its exact size - member
///     values looked up once, numbers written with <see cref="PrimitiveCodec.WriteNumeric"/> at their compile-time
///     offsets - and the block is written to the destination in one call. The plan is the same operation list the
///     static read plan uses; only its execution differs.
/// </summary>
[SuppressMessage("StyleCop.CSharp.OrderingRules", "SA1204:StaticElementsMustAppearBeforeInstanceElements", Justification = "helpers follow the executor they serve")]
public partial class CStruct
{
    /// <summary>
    ///     Writes <paramref name="composite"/> through its static plan when that is exactly equivalent to the
    ///     general writer; returns false (having written nothing) when it is not.
    /// </summary>
    /// <remarks>
    ///     Equivalence conditions: no update semantics (an update skips padding instead of zero-filling it and stages
    ///     through the sparse stream), a seekable destination whose existing bytes under the block can be read back
    ///     so padding keeps whatever it held, and limits (nesting depth, array elements, total bytes) that the
    ///     general writer would report at an inner field checked up front so the plan never fails after writing.
    ///     Values are converted before any byte reaches the destination: a conversion failure leaves the stream
    ///     untouched instead of partially written.
    /// </remarks>
    private bool TryWriteStaticPlan(CompiledCompositeType composite, object data, CStructElementWriterState state)
    {
        if (state.Options is UpdateOptions || StaticReadPlan.DisabledForTesting || composite.StaticPlan is not { SupportsWrite: true } plan ||
            plan.Size > StaticReadPlan.MaximumBlockSize || state.StructureDepth + plan.NestingDepth > state.MaxNestingDepth ||
            plan.MaximumArrayCount > state.Options.MaxArrayElements)
        {
            return false;
        }

        WriteBudgetStream stream = state.BudgetStream;
        if (stream.IsSparseUpdate || !stream.CanSeek)
        {
            return false;
        }

        long position = stream.Position;
        if (this.Aligned && position % composite.Symbol.Alignment != 0)
        {
            // The general writer aligns members to absolute stream positions; the plan's offsets are relative to
            // the struct start, which coincide only when the struct itself starts on its alignment boundary.
            return false;
        }

        long existing = Math.Min(plan.Size, Math.Max(0, stream.Length - position));
        int chargedBytes = this.Aligned ? plan.ChargedAlignedBytes : plan.ChargedFieldBytes;
        if ((existing > 0 && !stream.CanRead) || !stream.CanAffordBlock(plan.Size, chargedBytes) ||
            (stream.Inner is FixedBufferStream fixedBuffer && position + plan.Size > fixedBuffer.Capacity))
        {
            // A span destination that cannot hold the whole block keeps the general writer's field-by-field
            // behavior (the fields that fit are written before the capacity failure is reported).
            return false;
        }

        byte[] block = ArrayPool<byte>.Shared.Rent(plan.Size);
        try
        {
            Span<byte> span = block.AsSpan(0, plan.Size);
            int preserved = 0;
            while (preserved < existing)
            {
                int read = stream.Read(span.Slice(preserved, (int)existing - preserved));
                if (read <= 0)
                {
                    break;
                }

                preserved += read;
            }

            span[preserved..].Clear();
            stream.Position = position;
            this.ExecuteStaticWritePlan(plan, composite, span, data, state);
            stream.WriteBlock(span, chargedBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(block);
        }

        state.CurrentBitOffset = 0;
        state.CurrentBitfieldType = null;
        state.NextPosition = Math.Max(state.NextPosition, stream.Position);
        return true;
    }

    private void ExecuteStaticWritePlan(StaticReadPlan plan, CompiledCompositeType composite, Span<byte> bytes, object data, CStructElementWriterState state)
    {
        if (this.Aligned)
        {
            // The general writer materializes an aligned struct's tail padding as zeroes (CompleteStructTailPadding).
            bytes[plan.TailStart..].Clear();
        }

        StructValue? sameShape = data is StructValue structValue && ReferenceEquals(structValue.Shape, composite.Shape) ? structValue : null;
        StaticReadOperation[] operations = plan.Operations;
        for (int index = 0; index < operations.Length; index++)
        {
            try
            {
                StaticReadOperation operation = operations[index];
                CompiledField field = operation.Field;
                string name = field.Declaration.Name.Name;
                object value = GetMemberValue(sameShape, data, operation.Slot, name);
                if (value is null)
                {
                    throw new CStructWriteException("Null is valid only for a scalar pointer field: " + name);
                }

                bool capture = field.CapturesLayoutVariable || state.CaptureAllLayoutVariables;
                switch (operation.Kind)
                {
                case StaticReadKind.Numeric:
                    WriteNumericValue(field, bytes.Slice(operation.Offset, field.Codec.Size), value, name);
                    if (capture)
                    {
                        WriterVariableProjection.UpdateVariablesFromValue(state, name, value);
                        state.PublishQualified(name);
                    }

                    break;

                case StaticReadKind.Enum:
                    {
                        CompiledEnumType compiledEnum = field.Enum!;
                        BigInteger enumValue = EnumFieldValueParser.GetEnumValue(compiledEnum, value);
                        field.Codec.WriteNumeric(bytes.Slice(operation.Offset, field.Codec.Size), compiledEnum.Integer.ToStorageValue(enumValue));
                        if (capture)
                        {
                            this.UpdateExactLayoutVariable(state.Variables, name, enumValue);
                            state.PublishQualified(name);
                        }

                        break;
                    }

                case StaticReadKind.NumericArray:
                    {
                        int size = field.Codec.Size;
                        Span<byte> target = bytes.Slice(operation.Offset, size * operation.Count);
                        if (!TryWriteTypedArray(field, target, value, operation.Count))
                        {
                            IList<object> items = WriteValueMaterialization.ConvertToObjectList(value, operation.Count, name);
                            if (items.Count != operation.Count)
                            {
                                throw new CStructWriteException(
                                    $"Array length mismatch for {name}: expected {operation.Count}, got {items.Count}.");
                            }

                            for (int element = 0; element < operation.Count; element++)
                            {
                                WriteNumericValue(field, target.Slice(element * size, size), items[element], name);
                            }
                        }

                        if (capture)
                        {
                            WriterVariableProjection.UpdateVariablesFromValue(state, name, value);
                            state.PublishQualified(name);
                        }

                        break;
                    }

                case StaticReadKind.Nested:
                    {
                        string? outerPrefix = state.QualifiedPrefix;
                        if (field.HasQualifiedPrefix)
                        {
                            state.QualifiedPrefix = outerPrefix is null ? field.QualifiedPrefix : outerPrefix + field.QualifiedPrefix;
                        }

                        this.ExecuteStaticWritePlan(operation.NestedPlan!, operation.NestedComposite!, bytes.Slice(operation.Offset, operation.NestedPlan!.Size), value, state);
                        state.QualifiedPrefix = outerPrefix;
                    }

                    if (capture)
                    {
                        WriterVariableProjection.UpdateVariablesFromValue(state, name, value);
                        state.PublishQualified(name);
                    }

                    break;

                case StaticReadKind.NestedArray:
                    {
                        StaticReadPlan nestedPlan = operation.NestedPlan!;
                        IList<object> items = WriteValueMaterialization.ConvertToObjectList(value, operation.Count, name);
                        if (items.Count != operation.Count)
                        {
                            throw new CStructWriteException(
                                $"Array length mismatch for {name}: expected {operation.Count}, got {items.Count}.");
                        }

                        for (int element = 0; element < operation.Count; element++)
                        {
                            object item = items[element] ??
                                          throw new CStructWriteException(
                                              "Null is not valid for struct or union value: " + operation.NestedDeclaration!.Name.Name);
                            this.ExecuteStaticWritePlan(nestedPlan, operation.NestedComposite!, bytes.Slice(operation.Offset + (element * nestedPlan.Size), nestedPlan.Size), item, state);
                        }

                        if (capture)
                        {
                            WriterVariableProjection.UpdateVariablesFromValue(state, name, value);
                            state.PublishQualified(name);
                        }

                        break;
                    }

                default:
                    throw new InvalidOperationException("Static write plan cannot encode operation kind " + operation.Kind);
                }
            }
            catch (CStructException exception) when (exception.NoteMember(operations[index].Field.Name, operations[index].Field.DisplayTypeSpelling))
            {
                // Never entered: the filter records the innermost field, as the field-by-field writer does.
                throw;
            }
        }
    }

    /// <summary>The member lookup the general writer performs, using the slot directly for a value of this composite's own shape.</summary>
    private static object GetMemberValue(StructValue? sameShape, object data, int slot, string name)
    {
        if (sameShape is not null)
        {
            return sameShape.TryGetSlot(slot, out object? slotValue)
                       ? slotValue!
                       : throw new CStructWriteException($"No value was supplied for '{name}'.");
        }

        return WriteDataBinding.GetMemberValueOrThrow(data, name);
    }

    /// <summary>One numeric value with the general writer's conversion-failure translation.</summary>
    private static void WriteNumericValue(CompiledField field, Span<byte> target, object value, string name)
    {
        try
        {
            field.Codec.WriteNumeric(target, value);
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or
                                          FormatException or InvalidCastException)
        {
            throw new CStructWriteException(DescribeUnwritableValue(value, field), exception);
        }
    }

    /// <summary>
    ///     Bulk path for a <see cref="PrimitiveArray{T}"/> whose element type is the codec's own CLR type (the value
    ///     a parse produced) and whose length matches: the elements are encoded straight from the typed storage.
    ///     Anything else - including a length mismatch, so its message stays the general writer's - takes the
    ///     element loop.
    /// </summary>
    private static bool TryWriteTypedArray(CompiledField field, Span<byte> target, object value, int count)
    {
        PrimitiveCodec codec = field.Codec;
        switch (value)
        {
        case PrimitiveArray<byte> bytes when codec.Kind == PrimitiveCodecKind.UInt8 && bytes.Count == count:
            bytes.Span.CopyTo(target);
            return true;
        case PrimitiveArray<sbyte> sbytes when codec.Kind == PrimitiveCodecKind.Int8 && sbytes.Count == count:
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(sbytes.Span).CopyTo(target);
            return true;
        case PrimitiveArray<short> shorts when codec.Kind == PrimitiveCodecKind.Int16 && shorts.Count == count:
            CopyEndian(shorts.Span, target, codec.LittleEndian);
            return true;
        case PrimitiveArray<ushort> ushorts when codec.Kind == PrimitiveCodecKind.UInt16 && ushorts.Count == count:
            CopyEndian(ushorts.Span, target, codec.LittleEndian);
            return true;
        case PrimitiveArray<int> ints when codec.Kind == PrimitiveCodecKind.Int32 && ints.Count == count:
            CopyEndian(ints.Span, target, codec.LittleEndian);
            return true;
        case PrimitiveArray<uint> uints when codec.Kind == PrimitiveCodecKind.UInt32 && uints.Count == count:
            CopyEndian(uints.Span, target, codec.LittleEndian);
            return true;
        case PrimitiveArray<long> longs when codec.Kind == PrimitiveCodecKind.Int64 && longs.Count == count:
            CopyEndian(longs.Span, target, codec.LittleEndian);
            return true;
        case PrimitiveArray<ulong> ulongs when codec.Kind == PrimitiveCodecKind.UInt64 && ulongs.Count == count:
            CopyEndian(ulongs.Span, target, codec.LittleEndian);
            return true;
        case PrimitiveArray<float> floats when codec.Kind == PrimitiveCodecKind.Float32 && floats.Count == count:
            CopyEndian(floats.Span, target, codec.LittleEndian);
            return true;
        case PrimitiveArray<double> doubles when codec.Kind == PrimitiveCodecKind.Float64 && doubles.Count == count:
            CopyEndian(doubles.Span, target, codec.LittleEndian);
            return true;
        default:
            return false;
        }
    }

    /// <summary>Copies fixed-width integers into the layout's byte order; a same-endian copy is one memcpy.</summary>
    private static void CopyEndian<T>(ReadOnlySpan<T> source, Span<byte> target, bool littleEndian)
        where T : unmanaged
    {
        ReadOnlySpan<byte> raw = System.Runtime.InteropServices.MemoryMarshal.AsBytes(source);
        if (littleEndian == BitConverter.IsLittleEndian)
        {
            raw.CopyTo(target);
            return;
        }

        raw.CopyTo(target);
        int size = System.Runtime.CompilerServices.Unsafe.SizeOf<T>();
        for (int offset = 0; offset < target.Length; offset += size)
        {
            target.Slice(offset, size).Reverse();
        }
    }
}
