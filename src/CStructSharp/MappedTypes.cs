namespace CStructSharp;

using System;
using System.Collections.Concurrent;
using CStructSharp.Introspection;
using CStructSharp.Values;

/// <summary>
///     The registry of classes that implement <see cref="ICStructMapped{TSelf}"/>. Registration captures the type's
///     static mapping members as delegates once, under the generic constraint, so the read and write paths can map
///     an instance from its <see cref="Type"/> alone - no reflection, and therefore no trimming or Native AOT
///     conventions. Generated mappers register themselves in a module initializer; a hand-written mapper does the
///     same, or calls <see cref="Register{T}"/> once at startup (registering twice is harmless).
/// </summary>
public static partial class MappedTypes
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
        Reader<T>.Read = static source => T.ReadFrom(source);
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
    ///     at build time: the exact name, else the case-insensitive match, else the match after underscores are
    ///     ignored (<c>bit_depth</c> for <c>BitDepth</c>) - the generator's rule. When no member matches, or a step
    ///     finds two, it returns <paramref name="propertyName"/> itself, which the following <c>Get</c> reports as
    ///     missing, with the members that exist.
    /// </summary>
    /// <param name="source">The value being mapped.</param>
    /// <param name="propertyName">The mapped property's name.</param>
    /// <returns>The member name to read or write.</returns>
    public static string MemberName(StructValue source, string propertyName)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(propertyName);

        // The answer depends only on the shape's names, so it is computed once per shape and property.
        if (source.Shape.FindMappedName(propertyName) is { } cached)
        {
            return cached;
        }

        string resolved = ResolveMemberName(source.Shape.Names, propertyName);
        source.Shape.AddMappedName(propertyName, resolved);
        return resolved;
    }

    /// <summary>Applies the matching rules of <see cref="MemberName"/> to one shape's member names.</summary>
    /// <param name="names">The shape's member names.</param>
    /// <param name="propertyName">The mapped property's name.</param>
    /// <returns>The shape's own name instance, which the member lookup that follows finds by reference, or <paramref name="propertyName"/>.</returns>
    private static string ResolveMemberName(string[] names, string propertyName)
        => MappedMemberNames.Match(names, propertyName) ?? propertyName;

    /// <summary>Converts a value the reader produced (a pointer target, a union member) to <typeparamref name="T"/> with the rules of <c>Get&lt;T&gt;</c>.</summary>
    /// <typeparam name="T">The destination type.</typeparam>
    /// <param name="value">The value to convert.</param>
    /// <param name="member">The member the value belongs to, for the failure message.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="Diagnostics.CStructReadException">The value cannot be converted without loss.</exception>
    public static T ConvertValue<T>(object? value, string member) => (T)TypedValueConverter.Convert(value, typeof(T), member)!;

    /// <summary>Reads an instance of <paramref name="type"/> from <paramref name="source"/>, when the type is registered.</summary>
    /// <param name="type">The mapped class to build.</param>
    /// <param name="source">The parsed struct whose members fill the instance.</param>
    /// <param name="result">
    ///     Receives the new instance, or <see langword="null"/> when the type is not registered.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="type"/> is registered and an instance was read.</returns>
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
    /// <param name="instance">The mapped object whose members are stored.</param>
    /// <param name="target">
    ///     The struct value that receives the members; it is mutated only when the type is registered.
    /// </param>
    /// <returns><see langword="true"/> when the instance's exact runtime type is registered and was written.</returns>
    internal static bool TryWrite(object instance, StructValue target)
    {
        if (Entries.TryGetValue(instance.GetType(), out Entry? entry))
        {
            entry.Write(instance, target);
            return true;
        }

        return false;
    }

    /// <summary>A registered class's mapping members, captured as delegates for lookup by <see cref="Type"/>.</summary>
    /// <param name="Read">Builds an instance from a parsed struct.</param>
    /// <param name="Write">Stores an instance into a struct value.</param>
    private sealed record Entry(Func<StructValue, object> Read, Action<object, StructValue> Write);

    /// <summary>
    ///     The typed reader of a registered mapped class, reached through its type parameter rather than a lookup by
    ///     <see cref="Type"/>: <c>Get&lt;Point&gt;("origin")</c> knows its target at compile time.
    /// </summary>
    /// <typeparam name="T">The mapped class; unconstrained so that any caller type parameter can ask.</typeparam>
    internal static class Reader<T>
    {
        /// <summary>Gets the reader <see cref="Register{T}"/> stored, or <see langword="null"/> when <typeparamref name="T"/> is not registered.</summary>
        public static Func<StructValue, T>? Read { get; internal set; }
    }
}
