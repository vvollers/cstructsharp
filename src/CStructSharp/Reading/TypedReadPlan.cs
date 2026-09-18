namespace CStructSharp.Reading;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     A static read plan bound to one POCO type: the type's member map plus, per member, the plan
///     operation it reads from and how (see <see cref="TypedMemberMode"/>). Built once per composite and type.
/// </summary>
internal sealed class TypedReadPlan
{
    private TypedReadPlan(TypedValueConverter.ObjectMap map, TypedMember[] members)
    {
        this.Map = map;
        this.Members = members;
    }

    public TypedValueConverter.ObjectMap Map { get; }

    public TypedMember[] Members { get; }

    /// <summary>Binds a POCO type to a plan; null when the general path would not build a member map for the type at all.</summary>
    public static TypedReadPlan? TryBuild(StaticReadPlan plan, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] Type targetType)
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
    private static bool IsPocoTarget([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type type)
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
                    elementType = TypedValueConverter.DeclaredMappedType(valueType.GetElementType()!);
                }
                else if (TypedValueConverter.TryGetListElementTypeOf(valueType, out Type? listElement))
                {
                    elementType = TypedValueConverter.DeclaredMappedType(listElement);
                    if (!valueType.IsInterface)
                    {
                        listType = valueType;
                    }
                    else if (RuntimeFeature.IsDynamicCodeSupported)
                    {
                        listType = TypedValueConverter.ListTypeOf(listElement);
                    }
                    else
                    {
                        // Without dynamic code a List<T> for a run-time element type cannot be instantiated; the
                        // general converter reports the Native AOT guidance for this member.
                        elementType = null;
                    }
                }

                TypedReadPlan? nested = elementType is not null && IsPocoTarget(elementType) ? TryBuild(resolved.NestedPlan!, elementType) : null;
                return nested is null
                           ? new TypedMember { Member = member, SourceName = sourceName, Operation = resolved, Mode = TypedMemberMode.Materialize }
                           : new TypedMember { Member = member, SourceName = sourceName, Operation = resolved, Mode = TypedMemberMode.TypedNestedArray, Nested = nested, ElementType = elementType, ArrayType = valueType.IsArray ? valueType : null, ElementList = listType };
            }

        default:
            return new TypedMember { Member = member, SourceName = sourceName, Operation = resolved, Mode = TypedMemberMode.Scalar };
        }
    }
}
