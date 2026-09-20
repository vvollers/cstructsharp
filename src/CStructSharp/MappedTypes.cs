namespace CStructSharp;

using System;
using System.Collections.Concurrent;
using CStructSharp.Values;

/// <summary>
///     The registry of classes that implement <see cref="ICStructMapped{TSelf}"/>. Registration captures the type's
///     static mapping members as delegates once, under the generic constraint, so the read and write paths can map
///     an instance from its <see cref="Type"/> alone - no reflection, and therefore no trimming or Native AOT
///     conventions. Generated mappers register themselves in a module initializer; a hand-written mapper does the
///     same, or calls <see cref="Register{T}"/> once at startup (registering twice is harmless).
/// </summary>
public static class MappedTypes
{
    private static readonly ConcurrentDictionary<Type, Entry> Entries = new();

    /// <summary>Registers <typeparamref name="T"/> so its instances can be read and written by type.</summary>
    /// <typeparam name="T">A class implementing <see cref="ICStructMapped{TSelf}"/>.</typeparam>
    public static void Register<T>()
        where T : ICStructMapped<T>
    {
        Entries[typeof(T)] = new Entry(
            source => T.ReadFrom(source)!,
            (instance, target) => T.WriteTo((T)instance, target));
    }

    /// <summary>Whether <paramref name="type"/> was registered as a mapped class.</summary>
    /// <param name="type">The type to look up.</param>
    /// <returns><see langword="true"/> when registered.</returns>
    public static bool IsMapped(Type type)
    {
        return Entries.ContainsKey(type);
    }

    /// <summary>
    ///     Finds the layout member a mapped property maps to, for a generated mapper that did not resolve its layout
    ///     at build time: the exact name, else the single case-insensitive match, else the single match after
    ///     underscores are ignored (<c>bit_depth</c> for <c>BitDepth</c>), else <paramref name="propertyName"/>
    ///     itself (which the following <c>Get</c> reports as missing, with the members that exist).
    /// </summary>
    /// <param name="source">The value being mapped.</param>
    /// <param name="propertyName">The mapped property's name.</param>
    /// <returns>The member name to read or write.</returns>
    public static string MemberName(StructValue source, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(propertyName);
        string[] names = source.Shape.Names;
        if (Array.IndexOf(names, propertyName) >= 0)
        {
            return propertyName;
        }

        string? found = null;
        foreach (string name in names)
        {
            if (string.Equals(name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (found is not null)
                {
                    return propertyName;
                }

                found = name;
            }
        }

        if (found is not null)
        {
            return found;
        }

        foreach (string name in names)
        {
            if (name.IndexOf('_') >= 0 && string.Equals(name.Replace("_", string.Empty), propertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (found is not null)
                {
                    return propertyName;
                }

                found = name;
            }
        }

        return found ?? propertyName;
    }

    /// <summary>Converts a value the reader produced (a pointer target, a union member) to <typeparamref name="T"/> with the rules of <c>Get&lt;T&gt;</c>.</summary>
    /// <typeparam name="T">The destination type.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <param name="member">The member the value belongs to, for the failure message.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="Diagnostics.CStructReadException">The value cannot be converted without loss.</exception>
    public static T ConvertValue<T>(object? value, string member) => (T)TypedValueConverter.Convert(value, typeof(T), member)!;

    /// <summary>Reads an instance of <paramref name="type"/> from <paramref name="source"/>, when the type is registered.</summary>
    internal static bool TryRead(Type type, StructValue source, out object? result)
    {
        if (Entries.TryGetValue(type, out Entry? entry))
        {
            result = entry.Read(source);
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>Fills <paramref name="target"/> from <paramref name="instance"/>, when its type is registered.</summary>
    internal static bool TryWrite(object instance, StructValue target)
    {
        if (Entries.TryGetValue(instance.GetType(), out Entry? entry))
        {
            entry.Write(instance, target);
            return true;
        }

        return false;
    }

    private sealed record Entry(Func<StructValue, object> Read, Action<object, StructValue> Write);
}
