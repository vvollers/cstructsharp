namespace CStructSharp.Values;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using CStructSharp.Diagnostics;

/// <summary>Maps natural reader results to caller-selected CLR types with cached POCO metadata.</summary>
internal static class TypedValueConverter
{
    /// <summary>The members a mapped POCO type must keep under trimming: a public parameterless constructor plus public properties and fields.</summary>
    internal const DynamicallyAccessedMemberTypes MappedMembers =
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields;

    private static readonly ConcurrentDictionary<Type, ObjectMap> ObjectMaps = new();

    /// <summary>Converts one natural value or reports a stable read-domain failure.</summary>
    public static object? Convert(
        object? value,
        [DynamicallyAccessedMembers(MappedMembers)] Type targetType,
        string? path)
    {
        try
        {
            return ConvertCore(value, targetType, path ?? "<root>");
        }
        catch (CStructReadException exception)
        {
            exception.AttachContext(path);
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException or
                                          InvalidOperationException or MemberAccessException or OverflowException or
                                          TargetInvocationException)
        {
            throw ConversionFailure(value, targetType, path ?? "<root>", exception);
        }
    }

    /// <summary>Whether an implementation exception is one <see cref="Convert"/> reports as a conversion failure.</summary>
    public static bool IsConversionFailure(Exception exception)
    {
        return exception is ArgumentException or InvalidCastException or InvalidOperationException or
               MemberAccessException or OverflowException or TargetInvocationException;
    }

    /// <summary>The conversion failure <see cref="Convert"/> would report for a value of <paramref name="sourceType"/>.</summary>
    public static CStructReadException ConversionFailureFrom(Type sourceType, Type targetType, string path, Exception exception)
    {
        return ConversionFailure(sourceType.FullName, targetType, path, exception);
    }

    /// <summary>The recursive conversion itself, for the typed read plan, which applies the same error normalization as <see cref="Convert"/>.</summary>
    public static object? ConvertValue(
        object? value,
        [DynamicallyAccessedMembers(MappedMembers)] Type targetType,
        string path)
    {
        return ConvertCore(value, targetType, path);
    }

    /// <summary>The cached member map of a POCO type; throws the read-domain failure the general path throws for an unmappable type.</summary>
    public static ObjectMap GetObjectMap([DynamicallyAccessedMembers(MappedMembers)] Type targetType)
    {
        if (ObjectMaps.TryGetValue(targetType, out ObjectMap? map))
        {
            return map;
        }

        map = CreateObjectMap(targetType);
        return ObjectMaps.TryAdd(targetType, map) ? map : ObjectMaps[targetType];
    }

    /// <summary>The list shapes <see cref="ConvertCore"/> materializes as <see cref="List{T}"/>, with their element type.</summary>
    public static bool TryGetListElementTypeOf(Type targetType, [NotNullWhen(true)] out Type? elementType)
    {
        return TryGetListElementType(targetType, out elementType);
    }

    /// <summary>The failure the general path raises when a member setter rejects the converted value.</summary>
    public static CStructReadException SetterFailure(object? value, Type targetType, string path, Exception exception)
    {
        return ConversionFailure(value, targetType, path, exception);
    }

    /// <summary>The message the general path produces for a member the source does not provide.</summary>
    public static CStructReadException MissingMember(string path, Type targetType, string memberName)
    {
        return new CStructReadException(
            $"Cannot map '{path}' to '{targetType.FullName}': source member '{memberName}' is missing.");
    }

    /// <summary>The message the general path produces when several source names differ from a member's only by case.</summary>
    public static CStructReadException AmbiguousMember(string memberName)
    {
        return new CStructReadException(
            $"Member '{memberName}' is ambiguous because multiple source names differ only by case.");
    }

    /// <summary>Performs recursive conversion after the public error-normalization boundary.</summary>
    private static object? ConvertCore(
        object? value,
        [DynamicallyAccessedMembers(MappedMembers)] Type targetType,
        string path)
    {
        Type? nullableType = Nullable.GetUnderlyingType(targetType);
        Type effectiveTarget = nullableType ?? targetType;
        if (value is null)
        {
            if (!targetType.IsValueType || nullableType is not null)
            {
                return null;
            }

            throw ConversionFailure(value, targetType, path);
        }

        if (effectiveTarget.IsInstanceOfType(value))
        {
            return value;
        }

        if (effectiveTarget == typeof(object))
        {
            return value;
        }

        if (effectiveTarget.IsEnum)
        {
            return ConvertEnum(value, effectiveTarget, path);
        }

        if (IsNumericType(effectiveTarget))
        {
            return ConvertNumeric(UnwrapEnumValue(value), effectiveTarget, path);
        }

        if (effectiveTarget.IsArray)
        {
            Type elementType = effectiveTarget.GetElementType() ??
                               throw new InvalidOperationException("Array target has no element type.");
            return ConvertArray(value, effectiveTarget, DeclaredMappedType(elementType), path);
        }

        if (TryGetListElementType(effectiveTarget, out Type? listElementType))
        {
            return ConvertList(value, effectiveTarget, DeclaredMappedType(listElementType), path);
        }

        if (TryGetStringObjectDictionary(value, out IReadOnlyDictionary<string, object?>? source))
        {
            return ConvertObject(source, effectiveTarget, path);
        }

        throw ConversionFailure(value, targetType, path);
    }

    /// <summary>Maps a self-describing or primitive numeric value to one CLR enum.</summary>
    private static object ConvertEnum(object value, Type enumType, string path)
    {
        Type underlyingType = Enum.GetUnderlyingType(enumType);
        object numeric = ConvertNumeric(UnwrapEnumValue(value), underlyingType, path);
        return Enum.ToObject(enumType, numeric);
    }

    /// <summary>Returns the exact mathematical payload represented by a parsed layout enum.</summary>
    private static object UnwrapEnumValue(object value)
    {
        return value is EnumValueResult enumValue ? enumValue.Value : value;
    }

    /// <summary>Performs checked, culture-independent numeric conversions.</summary>
    private static object ConvertNumeric(object value, Type targetType, string path)
    {
        if (!IsNumericValue(value))
        {
            throw ConversionFailure(value, targetType, path);
        }

        try
        {
            if (targetType == typeof(float))
            {
                return value is BigInteger bigInteger
                           ? (float)bigInteger
                           : System.Convert.ToSingle(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(double))
            {
                return value is BigInteger bigInteger
                           ? (double)bigInteger
                           : System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(decimal))
            {
                return value is BigInteger bigInteger
                           ? (decimal)bigInteger
                           : System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }

            BigInteger integer = ToExactInteger(value);
            if (targetType == typeof(BigInteger))
            {
                return integer;
            }

            if (targetType == typeof(byte))
            {
                return checked((byte)integer);
            }

            if (targetType == typeof(sbyte))
            {
                return checked((sbyte)integer);
            }

            if (targetType == typeof(short))
            {
                return checked((short)integer);
            }

            if (targetType == typeof(ushort))
            {
                return checked((ushort)integer);
            }

            if (targetType == typeof(int))
            {
                return checked((int)integer);
            }

            if (targetType == typeof(uint))
            {
                return checked((uint)integer);
            }

            if (targetType == typeof(long))
            {
                return checked((long)integer);
            }

            if (targetType == typeof(ulong))
            {
                return checked((ulong)integer);
            }
        }
        catch (OverflowException exception)
        {
            throw ConversionFailure(value, targetType, path, exception);
        }

        throw ConversionFailure(value, targetType, path);
    }

    /// <summary>Requires an integral numeric source before converting it to <see cref="BigInteger"/>.</summary>
    private static BigInteger ToExactInteger(object value)
    {
        return value switch
        {
            BigInteger integer => integer,
            byte number => number,
            sbyte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number => number,
            _ => throw new InvalidCastException("The numeric value is not an exact integer."),
        };
    }

    /// <summary>Maps one enumerable source to an array of the requested element type.</summary>
    private static Array ConvertArray(object value, Type arrayType, [DynamicallyAccessedMembers(MappedMembers)] Type elementType, string path)
    {
        IReadOnlyList<object?> items = MaterializeItems(value, path);
        Array result = CreateArray(arrayType, elementType, items.Count);
        for (int index = 0; index < items.Count; index++)
        {
            result.SetValue(
                ConvertCore(items[index], elementType, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]"),
                index);
        }

        return result;
    }

    /// <summary>Maps one enumerable source to a common generic list abstraction.</summary>
    private static object ConvertList(object value, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type targetType, [DynamicallyAccessedMembers(MappedMembers)] Type elementType, string path)
    {
        IReadOnlyList<object?> items = MaterializeItems(value, path);
        var result = (IList)CreateList(targetType, elementType, path);
        for (int index = 0; index < items.Count; index++)
        {
            result.Add(
                ConvertCore(items[index], elementType, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]"));
        }

        return result;
    }

    /// <summary>Snapshots a non-string enumerable so recursive mapping has stable indexes.</summary>
    private static IReadOnlyList<object?> MaterializeItems(object value, string path)
    {
        if (value is string || value is not IEnumerable enumerable)
        {
            throw ConversionFailure(value, typeof(IEnumerable), path);
        }

        var items = new List<object?>();
        foreach (object? item in enumerable)
        {
            items.Add(item);
        }

        return items;
    }

    /// <summary>Maps a dynamic struct or union view to an ordinary mutable POCO.</summary>
    private static object ConvertObject(
        IReadOnlyDictionary<string, object?> source,
        [DynamicallyAccessedMembers(MappedMembers)] Type targetType,
        string path)
    {
        ObjectMap map = GetObjectMap(targetType);
        object target = map.Create();
        foreach (MappedMember member in map.Members)
        {
            if (!TryGetSourceMember(source, member.Name, out string? sourceName, out object? sourceValue))
            {
                throw MissingMember(path, targetType, member.Name);
            }

            object? converted = ConvertCore(sourceValue, member.ValueType, path + "." + sourceName);
            try
            {
                member.Set(target, converted);
            }
            catch (Exception exception) when (exception is ArgumentException or MethodAccessException or
                                              TargetInvocationException or InvalidCastException or
                                              InvalidOperationException)
            {
                // A compiled setter delegate (see BuildSetter) invokes the target member directly, so an
                // exception the member's own body throws propagates raw instead of wrapped in
                // TargetInvocationException the way reflection's PropertyInfo.SetValue would wrap it - widened
                // here so a member that throws its own exception (validation logic, for example) still reports
                // with this specific member's path, exactly as it did through the reflection-based setter.
                throw ConversionFailure(sourceValue, member.ValueType, path + "." + sourceName, exception);
            }
        }

        return target;
    }

    /// <summary>Finds an exact source key first and then one unambiguous case-insensitive key.</summary>
    private static bool TryGetSourceMember(
        IReadOnlyDictionary<string, object?> source,
        string targetName,
        out string sourceName,
        out object? value)
    {
        if (source.TryGetValue(targetName, out value))
        {
            sourceName = targetName;
            return true;
        }

        string? match = null;
        foreach (string candidate in source.Keys)
        {
            if (!string.Equals(candidate, targetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (match is not null)
            {
                throw AmbiguousMember(targetName);
            }

            match = candidate;
        }

        if (match is not null)
        {
            sourceName = match;
            value = source[match];
            return true;
        }

        sourceName = string.Empty;
        value = null;
        return false;
    }

    /// <summary>Recognizes dynamic struct and lossless union dictionaries without copying their entries.</summary>
    private static bool TryGetStringObjectDictionary(
        object value,
        [NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? result)
    {
        if (value is IReadOnlyDictionary<string, object?> readOnly)
        {
            result = readOnly;
            return true;
        }

        if (value is IDictionary<string, object?> mutable)
        {
            result = new DictionaryView(mutable);
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>Returns whether one type is a supported numeric destination.</summary>
    private static bool IsNumericType(Type type)
    {
        return type == typeof(byte) ||
               type == typeof(sbyte) ||
               type == typeof(short) ||
               type == typeof(ushort) ||
               type == typeof(int) ||
               type == typeof(uint) ||
               type == typeof(long) ||
               type == typeof(ulong) ||
               type == typeof(float) ||
               type == typeof(double) ||
               type == typeof(decimal) ||
               type == typeof(BigInteger);
    }

    /// <summary>Returns whether one runtime value belongs to a supported numeric source domain.</summary>
    private static bool IsNumericValue(object value)
    {
        Type type = value.GetType();
        return IsNumericType(type);
    }

    /// <summary>Recognizes the common list targets whose concrete representation can be <see cref="List{T}"/>.</summary>
    private static bool TryGetListElementType(Type targetType, [NotNullWhen(true)] out Type? elementType)
    {
        if (!targetType.IsGenericType)
        {
            elementType = null;
            return false;
        }

        Type definition = targetType.GetGenericTypeDefinition();
        if (definition != typeof(List<>) &&
            definition != typeof(IList<>) &&
            definition != typeof(ICollection<>) &&
            definition != typeof(IEnumerable<>) &&
            definition != typeof(IReadOnlyList<>) &&
            definition != typeof(IReadOnlyCollection<>))
        {
            elementType = null;
            return false;
        }

        elementType = targetType.GetGenericArguments()[0];
        return true;
    }

    /// <summary>
    ///     Compiles a property setter once, at <see cref="ObjectMap" /> build time, instead of invoking
    ///     <see cref="PropertyInfo.SetValue(object?, object?)" /> reflectively on every mapped value. The
    ///     compiled delegate calls the setter directly, so an exception the setter's own body throws propagates
    ///     unwrapped - <see cref="ConvertObject" />'s catch filter is widened accordingly.
    /// </summary>
    private static Action<object, object?> BuildPropertySetter(PropertyInfo property)
    {
        ParameterExpression target = Expression.Parameter(typeof(object), "target");
        ParameterExpression value = Expression.Parameter(typeof(object), "value");
        MethodCallExpression call = Expression.Call(
            Expression.Convert(target, property.DeclaringType!),
            property.SetMethod!,
            Expression.Convert(value, property.PropertyType));
        return Expression.Lambda<Action<object, object?>>(call, target, value).Compile();
    }

    /// <summary>Compiles a field setter once, mirroring <see cref="BuildPropertySetter" /> for public instance fields.</summary>
    private static Action<object, object?> BuildFieldSetter(FieldInfo field)
    {
        ParameterExpression target = Expression.Parameter(typeof(object), "target");
        ParameterExpression value = Expression.Parameter(typeof(object), "value");
        BinaryExpression assign = Expression.Assign(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            Expression.Convert(value, field.FieldType));
        return Expression.Lambda<Action<object, object?>>(assign, target, value).Compile();
    }

    /// <summary>Builds immutable constructor and setter metadata once per target CLR type.</summary>
    private static ObjectMap CreateObjectMap(
        [DynamicallyAccessedMembers(MappedMembers)] Type targetType)
    {
        if (targetType.IsAbstract || targetType.IsInterface || targetType.IsValueType)
        {
            throw new CStructReadException(
                $"Type '{targetType.FullName}' is not a mutable reference-type POCO.");
        }

        ConstructorInfo? constructor = targetType.GetConstructor(Type.EmptyTypes);
        if (constructor is null)
        {
            throw new CStructReadException(
                $"Type '{targetType.FullName}' needs a public parameterless constructor for typed reading.");
        }

        var members = new List<MappedMember>();
        foreach (PropertyInfo property in targetType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (property.SetMethod is { IsPublic: true, } && property.GetIndexParameters().Length == 0)
            {
                members.Add(new MappedMember(property.Name, DeclaredMappedType(property.PropertyType), BuildPropertySetter(property)));
            }
        }

        foreach (FieldInfo field in targetType.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!field.IsInitOnly && !field.IsStatic)
            {
                members.Add(new MappedMember(field.Name, DeclaredMappedType(field.FieldType), BuildFieldSetter(field)));
            }
        }

        if (members.Count == 0)
        {
            throw new CStructReadException(
                $"Type '{targetType.FullName}' has no public writable properties or fields.");
        }

        IGrouping<string, MappedMember>? duplicate = members
            .GroupBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new CStructReadException(
                $"Type '{targetType.FullName}' has ambiguous writable members named '{duplicate.Key}'.");
        }

        // A compiled factory replaces reflection's per-object invoke; a constructor that throws surfaces its
        // own exception instead of a TargetInvocationException wrapper.
        Func<object> create = Expression.Lambda<Func<object>>(Expression.New(constructor)).Compile();
        return new ObjectMap(create, members.ToArray());
    }

    /// <summary>
    ///     Creates the array a member declares. The declared array type is part of the compiled program, so on
    ///     .NET 9+ it is created from that type without dynamic code; .NET 8 has only the element-type overload.
    /// </summary>
    internal static Array CreateArray(Type arrayType, Type elementType, int length)
    {
#if NET9_0_OR_GREATER
        return Array.CreateInstanceFromArrayType(arrayType, length);
#else
        return Array.CreateInstance(elementType, length);
#endif
    }

    /// <summary>
    ///     Creates the list a member declares: a concrete <see cref="List{T}"/> through its own constructor, or, for a
    ///     collection interface, a <see cref="List{T}"/> instantiated at run time - which needs dynamic code, so a
    ///     Native AOT application declares the member as <c>List&lt;T&gt;</c> or <c>T[]</c> instead.
    /// </summary>
    internal static object CreateList([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type targetType, Type elementType, string path)
    {
        if (!targetType.IsInterface)
        {
            return Activator.CreateInstance(targetType) ?? throw new InvalidOperationException("Could not create typed list.");
        }

        if (RuntimeFeature.IsDynamicCodeSupported)
        {
            return CreateListOf(elementType);
        }

        throw new CStructReadException(
            $"Cannot map '{path}' to '{targetType.FullName}' without dynamic code: declare the member as List<{elementType.Name}> or {elementType.Name}[] when publishing with Native AOT.");
    }

    /// <summary>The run-time <see cref="List{T}"/> instantiation behind a collection-interface member; JIT only.</summary>
    [RequiresDynamicCode("Instantiates List<T> for an element type known only at run time.")]
    internal static object CreateListOf(Type elementType)
    {
        return Activator.CreateInstance(ListTypeOf(elementType)) ?? throw new InvalidOperationException("Could not create typed list.");
    }

    /// <summary>The <see cref="List{T}"/> type for a run-time element type; JIT only.</summary>
    [RequiresDynamicCode("Instantiates List<T> for an element type known only at run time.")]
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    internal static Type ListTypeOf(Type elementType)
    {
        return typeof(List<>).MakeGenericType(elementType);
    }

    /// <summary>Creates a consistent conversion failure with source and target type information.</summary>
    private static CStructReadException ConversionFailure(
        object? value,
        Type targetType,
        string path,
        Exception? innerException = null)
    {
        return ConversionFailure(value?.GetType().FullName, targetType, path, innerException);
    }

    private static CStructReadException ConversionFailure(
        string? sourceTypeName,
        Type targetType,
        string path,
        Exception? innerException)
    {
        string sourceName = sourceTypeName ?? "null";
        string message =
            $"Cannot map '{path}' from '{sourceName}' to '{targetType.FullName}' without an unsupported or lossy conversion.";
        CStructReadException exception = innerException is null
                                             ? new CStructReadException(message)
                                             : new CStructReadException(message, innerException);
        exception.AttachContext(path);
        return exception;
    }

    /// <summary>
    ///     The trimming boundary of POCO mapping. The root type of a typed read is annotated, so the trimmer keeps
    ///     its public members and constructor; the types of those members (a nested class, an array or list element)
    ///     and the runtime type of an object handed to a write are reached only through reflection metadata, which
    ///     the trimmer cannot follow. The documented contract (typed-values guide, "Trimming and Native AOT") is that
    ///     an application preserves those classes itself - <c>[DynamicallyAccessedMembers]</c> on the class, or a
    ///     trimmer root descriptor - so this is the one place the analysis is told to trust the declared type.
    /// </summary>
    [UnconditionalSuppressMessage("Trimming", "IL2068:Return value does not satisfy DynamicallyAccessedMembersAttribute", Justification = "Nested mapped types are preserved by the application per the documented contract; the root type is annotated.")]
    [return: DynamicallyAccessedMembers(MappedMembers)]
    internal static Type DeclaredMappedType(Type declared)
    {
        return declared;
    }

    /// <summary>Stores one target member's type and cached setter.</summary>
    internal sealed record MappedMember(string Name, [property: DynamicallyAccessedMembers(MappedMembers)][param: DynamicallyAccessedMembers(MappedMembers)] Type ValueType, Action<object, object?> Set);

    /// <summary>Stores cached construction and member metadata for one POCO type, in reflection order (properties, then fields).</summary>
    internal sealed record ObjectMap(Func<object> Create, IReadOnlyList<MappedMember> Members);

    /// <summary>Adapts an expando dictionary to a read-only view without copying it.</summary>
    private sealed class DictionaryView : IReadOnlyDictionary<string, object?>
    {
        private readonly IDictionary<string, object?> source;

        public DictionaryView(IDictionary<string, object?> source)
        {
            this.source = source;
        }

        public IEnumerable<string> Keys => this.source.Keys;

        public IEnumerable<object?> Values => this.source.Values;

        public int Count => this.source.Count;

        public object? this[string key] => this.source[key];

        public bool ContainsKey(string key)
        {
            return this.source.ContainsKey(key);
        }

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            return this.source.GetEnumerator();
        }

        public bool TryGetValue(string key, out object? value)
        {
            return this.source.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }
}
