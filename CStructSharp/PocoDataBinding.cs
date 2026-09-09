namespace CStructSharp;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.Reflection;

/// <summary>Reads named and indexed values from caller-supplied POCO, dictionary, or dynamic data.</summary>
internal static class PocoDataBinding
{
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

    /// <summary>Follows a public path through caller-provided write data, including array indexes.</summary>
    public static object ResolveDataPath(object data, IReadOnlyList<PathSegment> segments, PocoBindingMode bindingMode)
    {
        // Walk the object one segment at a time so member lookup and array indexing share the same public path rules.
        object value = data;
        foreach (PathSegment segment in segments)
        {
            // A segment may first select a named child and then one item within that child.
            value = GetMemberValueOrThrow(value, segment.Name, bindingMode);
            if (segment.Indexes.Count > 1)
            {
                // TODO(LANG-05): multidimensional POCO-input indexing is not yet available; only 0 or 1 index per
                // segment is handled today.
                throw new CStructPathException(
                    "Multiple indices in one path segment require a multidimensional field, not yet available: " +
                    segment.Name);
            }

            if (segment.Indexes.Count == 1)
            {
                value = GetIndexedValue(value, segment.Indexes[0]);
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
        PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) ??
                                 type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property != null && property.CanRead)
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

        FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance) ??
                           type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (field != null)
        {
            // Public fields are the final POCO fallback when no matching property exists.
            value = field.GetValue(data)!;
            return true;
        }

        value = null!;
        return false;
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
}
