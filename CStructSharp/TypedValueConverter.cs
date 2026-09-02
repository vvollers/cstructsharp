namespace CStructSharp;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;

/// <summary>Maps natural reader results to caller-selected CLR types with cached POCO metadata.</summary>
internal static class TypedValueConverter
{
    private const DynamicallyAccessedMemberTypes MappedMembers =
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
            return ConvertArray(value, elementType, path);
        }

        if (TryGetListElementType(effectiveTarget, out Type? listElementType))
        {
            return ConvertList(value, effectiveTarget, listElementType, path);
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
    private static Array ConvertArray(object value, Type elementType, string path)
    {
        IReadOnlyList<object?> items = MaterializeItems(value, path);
        Array result = Array.CreateInstance(elementType, items.Count);
        for (int index = 0; index < items.Count; index++)
        {
            result.SetValue(
                ConvertCore(items[index], elementType, path + "[" + index.ToString(CultureInfo.InvariantCulture) + "]"),
                index);
        }

        return result;
    }

    /// <summary>Maps one enumerable source to a common generic list abstraction.</summary>
    private static object ConvertList(object value, Type targetType, Type elementType, string path)
    {
        IReadOnlyList<object?> items = MaterializeItems(value, path);
        Type concreteType = typeof(List<>).MakeGenericType(elementType);
        var result = (IList)(Activator.CreateInstance(concreteType) ??
                             throw new InvalidOperationException("Could not create typed list."));
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
        ObjectMap map = ObjectMaps.GetOrAdd(targetType, CreateObjectMap);
        object target = map.Create();
        foreach (MappedMember member in map.Members)
        {
            if (!TryGetSourceMember(source, member.Name, out string? sourceName, out object? sourceValue))
            {
                throw new CStructReadException(
                    $"Cannot map '{path}' to '{targetType.FullName}': source member '{member.Name}' is missing.");
            }

            object? converted = ConvertCore(sourceValue, member.ValueType, path + "." + sourceName);
            try
            {
                member.Set(target, converted);
            }
            catch (Exception exception) when (exception is ArgumentException or MethodAccessException or
                                              TargetInvocationException)
            {
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
                throw new CStructReadException(
                    $"Member '{targetName}' is ambiguous because multiple source names differ only by case.");
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
                members.Add(new MappedMember(property.Name, property.PropertyType, property.SetValue));
            }
        }

        foreach (FieldInfo field in targetType.GetFields(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!field.IsInitOnly && !field.IsStatic)
            {
                members.Add(new MappedMember(field.Name, field.FieldType, field.SetValue));
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

        return new ObjectMap(
            () => constructor.Invoke(null),
            members.ToArray());
    }

    /// <summary>Creates a consistent conversion failure with source and target type information.</summary>
    private static CStructReadException ConversionFailure(
        object? value,
        Type targetType,
        string path,
        Exception? innerException = null)
    {
        string sourceName = value?.GetType().FullName ?? "null";
        string message =
            $"Cannot map '{path}' from '{sourceName}' to '{targetType.FullName}' without an unsupported or lossy conversion.";
        CStructReadException exception = innerException is null
                                             ? new CStructReadException(message)
                                             : new CStructReadException(message, innerException);
        exception.AttachContext(path);
        return exception;
    }

    /// <summary>Stores one target member's type and cached setter.</summary>
    private sealed record MappedMember(string Name, Type ValueType, Action<object, object?> Set);

    /// <summary>Stores cached construction and member metadata for one POCO type.</summary>
    private sealed record ObjectMap(Func<object> Create, IReadOnlyList<MappedMember> Members);

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
