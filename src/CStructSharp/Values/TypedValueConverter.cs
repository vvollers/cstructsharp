namespace CStructSharp.Values;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using CStructSharp.Diagnostics;

/// <summary>Maps natural reader results to caller-selected CLR types without reflection: scalars, enums, arrays, values, and registered mapped classes.</summary>
internal static class TypedValueConverter
{
    /// <summary>Converts one natural value or reports a stable read-domain failure.</summary>
    public static object? Convert(
        object? value,
        Type targetType,
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
                                          InvalidOperationException or OverflowException)
        {
            throw ConversionFailure(value, targetType, path ?? "<root>", exception);
        }
    }

    /// <summary>Performs recursive conversion after the public error-normalization boundary.</summary>
    private static object? ConvertCore(
        object? value,
        Type targetType,
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
            return ConvertArray(value, effectiveTarget, elementType, path);
        }

        // A class registered through ICStructMapped<T> maps from the parsed composite by its own generated (or
        // hand-written) ReadFrom; anything else is a conversion the library does not know.
        if (value is StructValue composite)
        {
            try
            {
                if (MappedTypes.TryRead(effectiveTarget, composite, out object? mapped))
                {
                    return mapped;
                }
            }
            catch (CStructException exception)
            {
                // A mapper reads members relative to its struct; report where that struct sits in the caller's read.
                exception.PrefixPath(path);
                throw;
            }
        }

        if (MappedTypes.IsMapped(effectiveTarget))
        {
            throw new CStructReadException(
                $"Cannot map '{path}' to '{effectiveTarget.FullName}': the value is a {value.GetType().Name}, not a struct.");
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
    private static Array ConvertArray(object value, Type arrayType, Type elementType, string path)
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
}
