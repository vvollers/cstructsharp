namespace CStructSharp;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;

/// <summary>Reads named and indexed values from caller-supplied POCO, dictionary, or dynamic data.</summary>
internal static class PocoDataBinding
{
    /// <summary>
    ///     Caches the reflective member resolution for one (type, requested name) pair - the case-sensitive-then-
    ///     case-insensitive property/field lookup a POCO write already re-ran on every single field of every
    ///     single call, even though the result never changes for a given type and requested spelling. Both
    ///     <see cref="CachedMember.Property"/> and <see cref="CachedMember.Field"/> are null for a name that
    ///     resolves to neither, so a genuine miss is cached too, not just a hit.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type Type, string Name), CachedMember> MemberCache = new();

    /// <summary>Accepts either a root object or an object that contains the root under its layout name.</summary>
    public static object NormalizeRootData(object data, string rootName, PocoBindingMode bindingMode)
    {
        if (data is null)
        {
            // A root scalar pointer may legitimately be null. Its compiled field decides whether null is valid.
            return data!;
        }

        return TryGetMemberValue(data, rootName, bindingMode, out object value) ? value : data;
    }

    /// <summary>
    ///     Follows a public path through caller-provided write data, including array indexes. An N-dimensional
    ///     array (LANG-05) index selects one item per supplied index in turn, walking into the caller's own
    ///     nested collection one dimension at a time - the same repeated single-dimension operation every other
    ///     N-D consumer uses (ADR-016 decision 5).
    /// </summary>
    public static object ResolveDataPath(object data, IReadOnlyList<PathSegment> segments, PocoBindingMode bindingMode)
    {
        // Walk the object one segment at a time so member lookup and array indexing share the same public path rules.
        object value = data;
        foreach (PathSegment segment in segments)
        {
            // A segment may first select a named child and then one item per index within that child.
            value = GetMemberValueOrThrow(value, segment.Name, bindingMode);
            foreach (int index in segment.Indexes)
            {
                value = GetIndexedValue(value, index);
            }
        }

        return value;
    }

    /// <summary>Reads a named value from an expando, dictionary, public property, or public field.</summary>
    public static bool TryGetMemberValue(object data, string name, PocoBindingMode bindingMode, out object value)
    {
        if (data is null)
        {
            value = null!;
            return false;
        }

        if (data is ExpandoObject expando)
        {
            // Parsed and browser JSON data use ExpandoObject, where names are direct dictionary keys.
            var dict = (IDictionary<string, object?>)expando;
            bool found = dict.TryGetValue(name, out object? memberValue);
            value = memberValue!;
            return found;
        }

        if (data is IDictionary<string, object> dictObj)
        {
            // Plain dictionaries are supported for callers that do not use dynamic objects.
            bool found = dictObj.TryGetValue(name, out object? memberValue);
            value = memberValue!;
            return found;
        }

        // Match the dynamic API first, then let ordinary objects participate through simple public members.
        Type type = data.GetType();
        CachedMember cached = MemberCache.GetOrAdd((type, name), static key => ResolveMember(key.Type, key.Name));

        if (cached.Property is PropertyInfo property && property.CanRead)
        {
            // PublicReadWrite intentionally excludes read-only properties for callers that want a stricter POCO contract.
            if (bindingMode == PocoBindingMode.PublicReadWrite && !property.CanWrite)
            {
                value = null!;
                return false;
            }

            // Presence and value are separate facts. The compiled field writer decides whether null is valid.
            value = property.GetValue(data)!;
            return true;
        }

        if (cached.Field is FieldInfo field)
        {
            // Public fields are the final POCO fallback when no matching property exists.
            value = field.GetValue(data)!;
            return true;
        }

        value = null!;
        return false;
    }

    /// <summary>
    ///     Resolves the public property and field a name maps to, case-sensitively first, then case-insensitively.
    ///     Both are resolved unconditionally (not just the property, falling back to the field only when the
    ///     property lookup fails) because the caller falls back to <see cref="CachedMember.Field"/> whenever the
    ///     resolved property exists but is not readable (a write-only property), not only when no property was
    ///     found at all - resolving both up front lets one cached result serve every future call correctly.
    /// </summary>
    private static CachedMember ResolveMember(Type type, string name)
    {
        PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) ??
                                 type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance) ??
                           type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        return new CachedMember(property, field);
    }

    /// <summary>Gets one item from an array-like value and reports a clear error for an invalid index.</summary>
    public static object GetIndexedValue(object value, int index)
    {
        return value switch
        {
            IList<object> list => list[index],
            Array array => array.GetValue(index) ?? throw new CStructWriteException("Null array element."),
            string str => str[index],
            _ => throw new CStructWriteException("Index not supported on value: " + value.GetType().Name),
        };
    }

    /// <summary>Gets a required field from an object and explains which layout field is missing when it cannot be found.</summary>
    public static object GetMemberValueOrThrow(object data, string name, PocoBindingMode bindingMode)
    {
        if (TryGetMemberValue(data, name, bindingMode, out object value))
        {
            return value;
        }

        throw new CStructWriteException("Field not found in data: " + name);
    }

    /// <summary>One cached member-resolution outcome; both members are null when the name resolves to neither.</summary>
    private readonly record struct CachedMember(PropertyInfo? Property, FieldInfo? Field);
}
