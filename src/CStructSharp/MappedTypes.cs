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
