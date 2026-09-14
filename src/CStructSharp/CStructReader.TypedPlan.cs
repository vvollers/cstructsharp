namespace CStructSharp;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CStructSharp.Structure;
using CstructEnum = CStructSharp.Structure.Enum;

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

    private readonly ConcurrentDictionary<(StaticReadPlan Plan, Type Target), TypedReadPlan?> typedReadPlans = new();

    private enum TypedMemberMode : byte
    {
        Scalar,
        TypedArrayCopy,
        TypedNested,
        TypedNestedArray,
        Materialize,
    }

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

        TypedReadPlan? typedPlan = this.typedReadPlans.GetOrAdd((plan, targetType), key => TypedReadPlan.TryBuild(key.Plan, key.Target));
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

    /// <summary>One target member bound to one plan operation, or to the failure the general path raises for it.</summary>
    private sealed class TypedMember
    {
        public TypedValueConverter.MappedMember Member { get; init; } = null!;

        public string SourceName { get; init; } = string.Empty;

        public StaticReadOperation? Operation { get; init; }

        public TypedMemberMode Mode { get; init; }

        public TypedReadPlan? Nested { get; init; }

        public Type? ElementType { get; init; }

        /// <summary>The concrete <see cref="List{T}"/> type to build when the member is a list shape; null for an array.</summary>
        public Type? ElementList { get; init; }

        public Func<string, CStructReadException>? Failure { get; init; }
    }

    private sealed class TypedReadPlan
    {
        private TypedReadPlan(TypedValueConverter.ObjectMap map, TypedMember[] members)
        {
            this.Map = map;
            this.Members = members;
        }

        public TypedValueConverter.ObjectMap Map { get; }

        public TypedMember[] Members { get; }

        /// <summary>Binds a POCO type to a plan; null when the general path would not build a member map for the type at all.</summary>
        public static TypedReadPlan? TryBuild(StaticReadPlan plan, [DynamicallyAccessedMembers(TypedPlanMembers)] Type targetType)
        {
            if (!IsPocoTarget(targetType))
            {
                return null;
            }

            TypedValueConverter.ObjectMap map;
            try
            {
                map = TypedValueConverter.GetObjectMap(targetType);
            }
            catch (CStructReadException)
            {
                return null;
            }

            var members = new TypedMember[map.Members.Count];
            for (int index = 0; index < members.Length; index++)
            {
                members[index] = Bind(plan, targetType, map.Members[index]);
            }

            return new TypedReadPlan(map, members);
        }

        /// <summary>
        ///     Whether the general converter would map a parsed struct to this type through its member map, rather
        ///     than return the <see cref="StructValue"/> itself, treat it as a collection, or fail on the type.
        /// </summary>
        private static bool IsPocoTarget(Type type)
        {
            Type effective = Nullable.GetUnderlyingType(type) ?? type;
            return effective.IsClass && !effective.IsAbstract && effective != typeof(object) && effective != typeof(string) &&
                   !effective.IsAssignableFrom(typeof(StructValue)) && !effective.IsArray &&
                   !TypedValueConverter.TryGetListElementTypeOf(effective, out _) && effective.GetConstructor(Type.EmptyTypes) is not null;
        }

        private static TypedMember Bind(StaticReadPlan plan, Type targetType, TypedValueConverter.MappedMember member)
        {
            // The same resolution the general path applies to a parsed value's keys: an exact name first, then a
            // single case-insensitive match; the plan's operations are the composite's member names in order.
            StaticReadOperation? exact = null;
            StaticReadOperation? insensitive = null;
            bool ambiguous = false;
            foreach (StaticReadOperation operation in plan.Operations)
            {
                string name = operation.Field.Declaration.Name.Name;
                if (string.Equals(name, member.Name, StringComparison.Ordinal))
                {
                    exact = operation;
                    break;
                }

                if (string.Equals(name, member.Name, StringComparison.OrdinalIgnoreCase))
                {
                    ambiguous |= insensitive is not null;
                    insensitive = operation;
                }
            }

            StaticReadOperation? resolved = exact ?? insensitive;
            if (exact is null && ambiguous)
            {
                return new TypedMember { Member = member, Failure = _ => TypedValueConverter.AmbiguousMember(member.Name) };
            }

            if (resolved is null)
            {
                return new TypedMember { Member = member, Failure = path => TypedValueConverter.MissingMember(path, targetType, member.Name) };
            }

            string sourceName = resolved.Field.Declaration.Name.Name;
            Type valueType = member.ValueType;
            switch (resolved.Kind)
            {
            case StaticReadKind.NumericArray:
                {
                    // A parsed numeric array converts element by element into an array of the codec's own type; the
                    // typed copy yields the same elements. Every other target shape converts the parsed value.
                    Type codecType = PrimitiveArrayReader.GetElementType(resolved.Field.Codec);
                    bool exactArray = valueType.IsArray && valueType.GetElementType() == codecType && resolved.Count > 0;
                    return new TypedMember { Member = member, SourceName = sourceName, Operation = resolved, Mode = exactArray ? TypedMemberMode.TypedArrayCopy : TypedMemberMode.Materialize };
                }

            case StaticReadKind.Nested:
                {
                    TypedReadPlan? nested = IsPocoTarget(valueType) ? TryBuild(resolved.NestedPlan!, valueType) : null;
                    return new TypedMember { Member = member, SourceName = sourceName, Operation = resolved, Mode = nested is null ? TypedMemberMode.Materialize : TypedMemberMode.TypedNested, Nested = nested };
                }

            case StaticReadKind.NestedArray:
                {
                    Type? elementType = null;
                    Type? listType = null;
                    if (valueType.IsArray)
                    {
                        elementType = valueType.GetElementType();
                    }
                    else if (TypedValueConverter.TryGetListElementTypeOf(valueType, out Type? listElement))
                    {
                        elementType = listElement;
                        listType = typeof(List<>).MakeGenericType(listElement);
                    }

                    TypedReadPlan? nested = elementType is not null && IsPocoTarget(elementType) ? TryBuild(resolved.NestedPlan!, elementType) : null;
                    return nested is null
                               ? new TypedMember { Member = member, SourceName = sourceName, Operation = resolved, Mode = TypedMemberMode.Materialize }
                               : new TypedMember { Member = member, SourceName = sourceName, Operation = resolved, Mode = TypedMemberMode.TypedNestedArray, Nested = nested, ElementType = elementType, ElementList = listType };
                }

            default:
                return new TypedMember { Member = member, SourceName = sourceName, Operation = resolved, Mode = TypedMemberMode.Scalar };
            }
        }
    }
}
