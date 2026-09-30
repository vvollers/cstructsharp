namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Generated;
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
public sealed partial class CStruct
{
    /// <summary>
    ///     Encodes <paramref name="data"/> into <paramref name="bytes"/> through the static plan. Fields publish the layout
    ///     variables later fields of the operation may read through <paramref name="captures"/>: the compiled engine's
    ///     slots, or nothing for a direct root write, which writes nothing afterwards.
    /// </summary>
    /// <typeparam name="TCaptures">The capture sink type, a struct so the plan is compiled per sink.</typeparam>
    /// <param name="plan">The composite's plan.</param>
    /// <param name="composite">The composite being written.</param>
    /// <param name="bytes">Exactly the composite's bytes, with padding already cleared or preserved.</param>
    /// <param name="data">
    ///     The composite's value, already bound by the caller (a root) or by the nested step that reached it. A nested
    ///     struct's value, including a mapped instance held by a dictionary or struct value, is bound through
    ///     <see cref="WriteDataBinding.Bind"/> before its plan runs, as the engine binds it member by member.
    /// </param>
    /// <param name="captures">Where captured variables and the qualified prefix go.</param>
    internal void ExecuteStaticWritePlan<TCaptures>(StaticReadPlan plan, CompiledCompositeType composite, Span<byte> bytes, object data, ref TCaptures captures)
        where TCaptures : struct, IStaticWriteCaptures
    {
        if (this.Aligned)
        {
            // An aligned struct's tail padding is written as zeroes, as the engine's member-by-member write completes it.
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
                    throw new CStructWriteException(WriteFailures.NullForNonPointer(name));
                }

                bool capture = captures.Captures(field);
                switch (operation.Kind)
                {
                case StaticReadKind.Numeric:
                    WriteNumericValue(field, bytes.Slice(operation.Offset, field.Codec.Size), value, name);
                    if (capture)
                    {
                        captures.Capture(name, field, value);
                    }

                    break;

                case StaticReadKind.Enum:
                    {
                        CompiledEnumType compiledEnum = field.Enum!;
                        BigInteger enumValue = EnumFieldValueParser.GetEnumValue(compiledEnum, value);
                        field.Codec.WriteNumeric(bytes.Slice(operation.Offset, field.Codec.Size), compiledEnum.Integer.ToStorageValue(enumValue));
                        if (capture)
                        {
                            captures.Capture(name, field, enumValue);
                        }

                        break;
                    }

                case StaticReadKind.NumericArray:
                    {
                        int size = field.Codec.Size;
                        Span<byte> target = bytes.Slice(operation.Offset, size * operation.Count);
                        if (!WriteValueRules.TryWriteTypedArray(field, target, value, operation.Count))
                        {
                            IList<object> items = WriteValueMaterialization.ConvertToObjectList(value, operation.Count, name);
                            if (items.Count != operation.Count)
                            {
                                throw new CStructWriteException(
                                    WriteFailures.ArrayLengthMismatch(name, operation.Count, items.Count));
                            }

                            for (int element = 0; element < operation.Count; element++)
                            {
                                WriteNumericValue(field, target.Slice(element * size, size), items[element], name);
                            }
                        }

                        if (capture)
                        {
                            captures.Capture(name, field, value);
                        }

                        break;
                    }

                case StaticReadKind.Nested:
                    {
                        // Nested composites are written here without re-entering the struct writer, so each binds its own
                        // value. A sink without variables (a direct root write) keeps no prefix, so setting one changes
                        // nothing there.
                        value = WriteDataBinding.Bind(value, operation.NestedComposite!);
                        string? outerPrefix = captures.QualifiedPrefix;
                        if (field.HasQualifiedPrefix)
                        {
                            captures.QualifiedPrefix = outerPrefix is null ? field.QualifiedPrefix : outerPrefix + field.QualifiedPrefix;
                        }

                        this.ExecuteStaticWritePlan(operation.NestedPlan!, operation.NestedComposite!, bytes.Slice(operation.Offset, operation.NestedPlan!.Size), value, ref captures);
                        captures.QualifiedPrefix = outerPrefix;
                        if (capture)
                        {
                            captures.Capture(name, field, value);
                        }

                        break;
                    }

                case StaticReadKind.NestedArray:
                    {
                        StaticReadPlan nestedPlan = operation.NestedPlan!;
                        IList<object> items = WriteValueMaterialization.ConvertToObjectList(value, operation.Count, name);
                        if (items.Count != operation.Count)
                        {
                            throw new CStructWriteException(
                                WriteFailures.ArrayLengthMismatch(name, operation.Count, items.Count));
                        }

                        for (int element = 0; element < operation.Count; element++)
                        {
                            object item = WriteDataBinding.Bind(
                                items[element] ?? throw new CStructWriteException(WriteFailures.NullComposite(operation.NestedDeclaration!.Name.Name)),
                                operation.NestedComposite!);
                            this.ExecuteStaticWritePlan(nestedPlan, operation.NestedComposite!, bytes.Slice(operation.Offset + (element * nestedPlan.Size), nestedPlan.Size), item, ref captures);
                        }

                        if (capture)
                        {
                            captures.Capture(name, field, value);
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

    /// <summary>The member lookup the engine performs, using the slot directly for a value of this composite's own shape.</summary>
    private static object GetMemberValue(StructValue? sameShape, object data, int slot, string name)
    {
        if (sameShape is not null)
        {
            return sameShape.TryGetSlot(slot, out object? slotValue)
                       ? slotValue!
                       : throw new CStructWriteException(WriteFailures.NoValueSupplied(name));
        }

        return WriteDataBinding.GetMemberValueOrThrow(data, name);
    }

    /// <summary>One numeric value with the engine's conversion-failure translation.</summary>
    private static void WriteNumericValue(CompiledField field, Span<byte> target, object value, string name)
    {
        try
        {
            field.Codec.WriteNumeric(target, value);
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or
                                          FormatException or InvalidCastException)
        {
            throw new CStructWriteException(WriteValueRules.DescribeUnwritableValue(value, field), exception);
        }
    }
}
