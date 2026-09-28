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
    /// <summary>
    ///     <see cref="Convert(object?, Type, string?)"/> for a type known at compile time, with the common cases taken
    ///     without the type tests: a value that already has the type, a registered mapped class read through its typed
    ///     reader, and a parsed primitive array copied into the matching array. Each is the rule the general conversion
    ///     applies to that input, with the same failure decoration; every other input takes the general conversion.
    /// </summary>
    /// <typeparam name="T">The requested type.</typeparam>
    /// <param name="value">The natural value.</param>
    /// <param name="path">The value's path, for failure messages.</param>
    /// <returns>The converted value.</returns>
    public static T Convert<T>(object? value, string path)
    {
        if (value is T typed)
        {
            return typed;
        }

        if (value is StructValue composite && MappedTypes.Reader<T>.Read is { } read)
        {
            return ReadMapped(read, composite, path);
        }

        if (value is IPrimitiveArray primitive && ArrayElement<T>.Type is { } elementType && primitive.ElementType == elementType)
        {
            return (T)(object)primitive.ToArray();
        }

        return (T)Convert(value, typeof(T), path)!;
    }

    /// <summary>Runs a mapped class's reader with the failure decoration the general conversion gives it.</summary>
    private static T ReadMapped<T>(Func<StructValue, T> read, StructValue composite, string path)
    {
        try
        {
            try
            {
                return read(composite);
            }
            catch (CStructException exception)
            {
                // A mapper reads members relative to its struct; report where that struct sits in the caller's read.
                exception.PrefixPath(path);
                throw;
            }
        }
        catch (CStructReadException exception)
        {
            exception.AttachContext(path);
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException or
                                          InvalidOperationException or OverflowException)
        {
            throw ConversionFailure(composite, typeof(T), path, exception);
        }
    }

    /// <summary>Converts one natural value or reports a stable read-domain failure.</summary>
    /// <param name="value">The natural value produced by the reader, or <see langword="null"/>.</param>
    /// <param name="targetType">The CLR type the caller asked for.</param>
    /// <param name="path">The element path named in failures, or <see langword="null"/> for the root.</param>
    /// <returns>The converted value, or <see langword="null"/> for a null value and a nullable target.</returns>
    /// <exception cref="CStructReadException">The conversion is unsupported or lossy.</exception>
    public static object? Convert(
        object? value,
        Type targetType,
        string? path)
    {
        try
        {
            return ConvertCore(value, targetType, new ElementPath(path ?? "<root>"));
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
        in ElementPath path)
    {
        Type? nullableType = Nullable.GetUnderlyingType(targetType);
        Type effectiveTarget = nullableType ?? targetType;
        if (value is null)
        {
            if (!targetType.IsValueType || nullableType is not null)
            {
                return null;
            }

            throw ConversionFailure(value, targetType, path.ToString());
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
                exception.PrefixPath(path.ToString());
                throw;
            }
        }

        if (MappedTypes.IsMapped(effectiveTarget))
        {
            throw new CStructReadException(
                $"Cannot map '{path}' to '{effectiveTarget.FullName}': the value is a {value.GetType().Name}, not a struct.");
        }

        throw ConversionFailure(value, targetType, path.ToString());
    }

    /// <summary>Maps a self-describing or primitive numeric value to one CLR enum.</summary>
    private static object ConvertEnum(object value, Type enumType, in ElementPath path)
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
    private static object ConvertNumeric(object value, Type targetType, in ElementPath path)
    {
        if (!IsNumericValue(value))
        {
            throw ConversionFailure(value, targetType, path.ToString());
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
            throw ConversionFailure(value, targetType, path.ToString(), exception);
        }

        throw ConversionFailure(value, targetType, path.ToString());
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
    private static Array ConvertArray(object value, Type arrayType, Type elementType, in ElementPath path)
    {
        // A parsed primitive array whose element type is the requested one copies its typed storage: no boxing.
        if (value is IPrimitiveArray primitive && primitive.ElementType == elementType)
        {
            return primitive.ToArray();
        }

        string basePath = path.ToString();
        IReadOnlyList<object?> items = MaterializeItems(value, basePath);
        Array result = CreateArray(arrayType, elementType, items.Count);
        for (int index = 0; index < items.Count; index++)
        {
            // The element path is formatted only by a failure.
            result.SetValue(ConvertCore(items[index], elementType, new ElementPath(basePath, index)), index);
        }

        return result;
    }

    /// <summary>Takes a parsed list as it is, or snapshots any other non-string enumerable so recursive mapping has stable indexes.</summary>
    private static IReadOnlyList<object?> MaterializeItems(object value, string path)
    {
        if (value is IReadOnlyList<object?> list)
        {
            return list;
        }

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
    /// <param name="arrayType">The declared single-dimensional array type, such as <c>int[]</c>.</param>
    /// <param name="elementType">The element type of <paramref name="arrayType"/>, used on .NET 8.</param>
    /// <param name="length">The number of elements.</param>
    /// <returns>A new zero-filled array of <paramref name="arrayType"/>.</returns>
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

    /// <summary>
    ///     A value path whose element index is appended only when a diagnostic needs the text, so converting an
    ///     array's elements does not build one string per element.
    /// </summary>
    private readonly struct ElementPath
    {
        private readonly string basePath;
        private readonly int index;

        public ElementPath(string basePath)
        {
            this.basePath = basePath;
            this.index = -1;
        }

        public ElementPath(string basePath, int index)
        {
            this.basePath = basePath;
            this.index = index;
        }

        public override string ToString()
            => this.index < 0 ? this.basePath : this.basePath + "[" + this.index.ToString(CultureInfo.InvariantCulture) + "]";
    }

    /// <summary>The element type of a one-dimensional array type, computed once per <typeparamref name="T"/>; null for any other type.</summary>
    /// <typeparam name="T">The requested type.</typeparam>
    private static class ArrayElement<T>
    {
        /// <summary>Gets the element type, or <see langword="null"/> when <typeparamref name="T"/> is not a one-dimensional array.</summary>
        public static Type? Type { get; } = typeof(T).IsSZArray ? typeof(T).GetElementType() : null;
    }
}
