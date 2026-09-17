namespace CStructSharp;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     Typed read plan (E2.7): <c>ReadValue&lt;T&gt;</c> of a fully fixed composite binds the static read plan's
///     operations to the target type's members once and then reads each member straight from the bytes into a new
///     instance, skipping the intermediate <see cref="StructValue"/> and its
///     name-based conversion. Values, conversions, member order and every failure message are those of parsing
///     to a <see cref="StructValue"/> and converting it with <see cref="TypedValueConverter"/>.
/// </summary>
public partial class CStruct
{
    private const DynamicallyAccessedMemberTypes TypedPlanMembers =
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields;

    /// <summary>
    ///     Reads a composite at the current position into a new <paramref name="targetType"/> through its typed
    ///     plan when the plan is exactly equivalent to parse-then-convert; returns null (having consumed nothing)
    ///     when it is not.
    /// </summary>
    private object? TryReadTypedPlan(Struct declaration, [DynamicallyAccessedMembers(TypedPlanMembers)] Type targetType, CStructOperationContext state, string path)
    {
        if (state.Debug || StaticReadPlan.DisabledForTesting || declaration.IsUnion)
        {
            return null;
        }

        CompiledCompositeType composite = this.compiledSizeQueries.GetCompiledComposite(declaration);
        if (composite.StaticPlan is not StaticReadPlan plan ||
            state.StructureDepth + plan.NestingDepth > state.MaxNestingDepth || plan.MaximumArrayCount > state.MaxArrayElements ||
            (this.Aligned && state.Stream.Position % composite.Symbol.Alignment != 0))
        {
            return null;
        }

        TypedReadPlan? typedPlan = composite.GetOrAddTypedReadPlan(targetType, static (target, staticPlan) => TypedReadPlan.TryBuild(staticPlan, target));
        if (typedPlan is null || !state.Stream.TryReadSpanWithinBudget(plan.Size, out ReadOnlySpan<byte> bytes))
        {
            return null;
        }

        object result;
        try
        {
            result = this.ExecuteTypedPlan(typedPlan, bytes, state, new TypedPath(path));
        }
        catch (CStructReadException exception)
        {
            exception.AttachContext(path);
            throw;
        }
        catch (Exception exception) when (TypedValueConverter.IsConversionFailure(exception))
        {
            throw TypedValueConverter.ConversionFailureFrom(typeof(StructValue), targetType, path, exception);
        }

        state.CurrentBitOffset = 0;
        state.CurrentBitfieldType = null;
        state.NextPosition = state.Stream.Position;
        return result;
    }

    private object ExecuteTypedPlan(TypedReadPlan plan, ReadOnlySpan<byte> bytes, CStructOperationContext state, TypedPath path)
    {
        state.EnterStructure();
        try
        {
            object target = plan.Map.Create();
            TypedMember[] members = plan.Members;
            for (int index = 0; index < members.Length; index++)
            {
                TypedMember member = members[index];
                if (member.Failure is not null)
                {
                    throw member.Failure(path.ToString());
                }

                StaticReadOperation operation = member.Operation!;
                CompiledField field = operation.Field;
                Type valueType = member.Member.ValueType;
                object? value;
                switch (member.Mode)
                {
                case TypedMemberMode.Scalar:
                    value = this.ReadScalar(operation, bytes, state);
                    if (!valueType.IsInstanceOfType(value))
                    {
                        value = TypedValueConverter.ConvertValue(value, valueType, path.Member(member.SourceName));
                    }

                    break;

                case TypedMemberMode.TypedArrayCopy:
                    value = ((IPrimitiveArray)ReadNumericArray(operation, bytes, state)).ToArray();
                    break;

                case TypedMemberMode.TypedNested:
                    path.Push(member.SourceName, -1);
                    value = this.ExecuteTypedPlan(member.Nested!, bytes.Slice(operation.Offset, operation.NestedPlan!.Size), state, path);
                    path.Pop();
                    break;

                case TypedMemberMode.TypedNestedArray:
                    {
                        if (operation.Count > state.MaxArrayElements)
                        {
                            throw new CStructReadLimitException("Array length exceeds the configured limit: " + field.Declaration.Name.Name);
                        }

                        StaticReadPlan nestedPlan = operation.NestedPlan!;
                        if (member.ElementList is Type listType)
                        {
                            var list = (IList)Activator.CreateInstance(listType)!;
                            for (int element = 0; element < operation.Count; element++)
                            {
                                path.Push(member.SourceName, element);
                                list.Add(this.ExecuteTypedPlan(member.Nested!, bytes.Slice(operation.Offset + (element * nestedPlan.Size), nestedPlan.Size), state, path));
                                path.Pop();
                            }

                            value = list;
                        }
                        else
                        {
                            Array array = Array.CreateInstance(member.ElementType!, operation.Count);
                            for (int element = 0; element < operation.Count; element++)
                            {
                                path.Push(member.SourceName, element);
                                array.SetValue(this.ExecuteTypedPlan(member.Nested!, bytes.Slice(operation.Offset + (element * nestedPlan.Size), nestedPlan.Size), state, path), element);
                                path.Pop();
                            }

                            value = array;
                        }

                        break;
                    }

                default:
                    {
                        // The member's type is not one the plan binds directly (object, a dictionary view, an untyped
                        // list, a mismatched element type): materialize exactly the value the general reader
                        // produces and convert it the general way.
                        value = this.MaterializeOperation(operation, bytes, state);
                        value = TypedValueConverter.ConvertValue(value, valueType, path.Member(member.SourceName));
                        break;
                    }
                }

                try
                {
                    member.Member.Set(target, value);
                }
                catch (Exception exception) when (exception is ArgumentException or MethodAccessException or
                                                  System.Reflection.TargetInvocationException or InvalidCastException or
                                                  InvalidOperationException)
                {
                    throw TypedValueConverter.SetterFailure(value, valueType, path.Member(member.SourceName), exception);
                }
            }

            return target;
        }
        finally
        {
            state.ExitStructure();
        }
    }

    private object ReadScalar(StaticReadOperation operation, ReadOnlySpan<byte> bytes, CStructOperationContext state)
    {
        CompiledField field = operation.Field;
        switch (operation.Kind)
        {
        case StaticReadKind.Numeric:
            return field.Codec.ReadNumeric(bytes.Slice(operation.Offset, field.Codec.Size));
        case StaticReadKind.Enum:
            return this.CreateEnumValue((CstructEnum)field.Type.Symbol.Declaration!, field.Codec.ReadNumeric(bytes.Slice(operation.Offset, field.Codec.Size)));
        default:
            if (operation.Count > state.MaxArrayElements)
            {
                throw new CStructReadLimitException("Array length exceeds the configured limit: " + field.Declaration.Name.Name);
            }

            return ReadLatin1Characters(bytes.Slice(operation.Offset, operation.Count));
        }
    }

    private static IList<object?> ReadNumericArray(StaticReadOperation operation, ReadOnlySpan<byte> bytes, CStructOperationContext state)
    {
        CompiledField field = operation.Field;
        if (operation.Count > state.MaxArrayElements)
        {
            throw new CStructReadLimitException("Array length exceeds the configured limit: " + field.Declaration.Name.Name);
        }

        return operation.Count == 0
                   ? new List<object?>(0)
                   : PrimitiveArrayReader.Decode(bytes.Slice(operation.Offset, operation.Count * field.Codec.Size), field.Codec, operation.Count);
    }

    /// <summary>The value the general reader stores for this operation: a boxed scalar, a typed array, a <see cref="StructValue"/>, or a list of them.</summary>
    private object MaterializeOperation(StaticReadOperation operation, ReadOnlySpan<byte> bytes, CStructOperationContext state)
    {
        switch (operation.Kind)
        {
        case StaticReadKind.NumericArray:
            return ReadNumericArray(operation, bytes, state);
        case StaticReadKind.Nested:
            {
                var nested = new StructValue(operation.NestedComposite!.Shape);
                this.ExecuteStaticPlan(operation.NestedPlan!, bytes.Slice(operation.Offset, operation.NestedPlan!.Size), nested, state);
                return nested;
            }

        case StaticReadKind.NestedArray:
            {
                if (operation.Count > state.MaxArrayElements)
                {
                    throw new CStructReadLimitException("Array length exceeds the configured limit: " + operation.Field.Declaration.Name.Name);
                }

                var elements = new List<object?>(operation.Count);
                StaticReadPlan nestedPlan = operation.NestedPlan!;
                for (int element = 0; element < operation.Count; element++)
                {
                    var nested = new StructValue(operation.NestedComposite!.Shape);
                    elements.Add(nested);
                    this.ExecuteStaticPlan(nestedPlan, bytes.Slice(operation.Offset + (element * nestedPlan.Size), nestedPlan.Size), nested, state);
                }

                return elements;
            }

        default:
            return this.ReadScalar(operation, bytes, state);
        }
    }

    /// <summary>
    ///     The path of the object being filled, kept as a stack of member/element segments and rendered only when a
    ///     message needs it - the general path spells <c>root.items[3].left.kind</c> the same way.
    /// </summary>
    private sealed class TypedPath
    {
        private readonly string root;
        private readonly List<(string Name, int Index)> segments = new();

        public TypedPath(string root)
        {
            this.root = root;
        }

        public void Push(string name, int index)
        {
            this.segments.Add((name, index));
        }

        public void Pop()
        {
            this.segments.RemoveAt(this.segments.Count - 1);
        }

        /// <summary>The path of a member of the current object.</summary>
        public string Member(string name)
        {
            return this.ToString() + "." + name;
        }

        public override string ToString()
        {
            var builder = new System.Text.StringBuilder(this.root);
            foreach ((string name, int index) in this.segments)
            {
                builder.Append('.').Append(name);
                if (index >= 0)
                {
                    builder.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
                }
            }

            return builder.ToString();
        }
    }
}
