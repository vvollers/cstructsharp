namespace CStructSharp.Values;

using System;
using System.Collections;
using System.Collections.Generic;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     Reads named and indexed values from the data a write operation is given. Writable data is a
///     <see cref="StructValue"/>/<see cref="UnionValue"/> (parsed or built by the caller), a string-keyed dictionary
///     (including the expando objects the browser bridge produces), or an instance of a class registered through
///     <see cref="ICStructMapped{TSelf}"/>, which <see cref="Materialize"/> turns into a <see cref="StructValue"/> of
///     the layout's shape before any member is read. No reflection is involved.
/// </summary>
internal static class WriteDataBinding
{
    /// <summary>Accepts either a root object or an object that contains the root under its layout name.</summary>
    public static object NormalizeRootData(object data, string rootName)
    {
        if (data is null)
        {
            // A root scalar pointer may legitimately be null. Its compiled field decides whether null is valid.
            return data!;
        }

        return TryGetMemberValue(data, rootName, out object value) ? value : data;
    }

    /// <summary>
    ///     Turns a mapped-class instance into the <see cref="StructValue"/> of <paramref name="composite"/>'s shape
    ///     that its <c>WriteTo</c> fills; every other data object is returned as it is.
    /// </summary>
    public static object Materialize(object data, CompiledCompositeType composite)
    {
        if (data is null || IsMemberSource(data))
        {
            return data!;
        }

        var target = new StructValue(composite.Shape);
        if (!MappedTypes.TryWrite(data, target))
        {
            return data;
        }

        MaterializeNested(target, composite);
        return target;
    }

    /// <summary>
    ///     A mapper stores nested mapped instances as they are; they become struct values here so every consumer
    ///     below - the static write plan included, which never re-enters the struct writer - sees plain members.
    /// </summary>
    private static void MaterializeNested(StructValue target, CompiledCompositeType composite)
    {
        foreach (CompiledField field in composite.Fields)
        {
            if (field.Composite is not { } nested)
            {
                continue;
            }

            if (composite.PromotedFields.Contains(field))
            {
                // A promoted member's children live on the same struct value.
                MaterializeNested(target, nested);
                continue;
            }

            if (field.IsUnnamed || !target.TryGetValue(field.Name, out object? value) || value is null)
            {
                continue;
            }

            object? materialized = field.Array.Kind == CompiledArrayKind.Scalar
                ? MaterializeElement(value, nested)
                : MaterializeElements(value, nested);
            if (!ReferenceEquals(materialized, value))
            {
                target[field.Name] = materialized;
            }
        }
    }

    private static object MaterializeElement(object value, CompiledCompositeType nested)
    {
        return IsMemberSource(value) || value is UnionValue ? value : Materialize(value, nested);
    }

    /// <summary>Rebuilds a collection only when at least one element (at any depth) is a mapped instance.</summary>
    private static object MaterializeElements(object value, CompiledCompositeType nested)
    {
        if (value is string || value is not IEnumerable elements || IsMemberSource(value))
        {
            return value;
        }

        var rebuilt = new List<object?>();
        bool changed = false;
        foreach (object? element in elements)
        {
            object? replacement = element switch
            {
                null => null,
                string or StructValue or UnionValue => element,
                IEnumerable inner when !IsMemberSource(element) => MaterializeElements(inner, nested),
                _ => MaterializeElement(element, nested),
            };
            changed |= !ReferenceEquals(replacement, element);
            rebuilt.Add(replacement);
        }

        return changed ? rebuilt : value;
    }

    /// <summary>Whether <paramref name="data"/> is a shape the writer can read members from (or a registered mapped class).</summary>
    public static bool IsWritable(object data)
    {
        return IsMemberSource(data) || MappedTypes.IsMapped(data.GetType());
    }

    /// <summary>Whether <paramref name="data"/> exposes members directly: a struct value, an expando, or a dictionary.</summary>
    public static bool IsMemberSource(object data)
    {
        return data is IDictionary<string, object?> || data is IDictionary<string, object>;
    }

    /// <summary>
    ///     Follows a public path through caller-provided write data, including array indexes. An N-dimensional
    ///     array index selects one item per supplied index in turn, walking into the caller's own
    ///     nested collection one dimension at a time - the same repeated single-dimension operation every other
    ///     N-D consumer uses.
    /// </summary>
    public static object ResolveDataPath(object data, IReadOnlyList<PathSegment> segments)
    {
        // Walk the object one segment at a time so member lookup and array indexing share the same public path rules.
        object value = data;
        foreach (PathSegment segment in segments)
        {
            // A segment may first select a named child and then one item per index within that child.
            value = GetMemberValueOrThrow(value, segment.Name);
            foreach (int index in segment.Indexes)
            {
                value = GetIndexedValue(value, index);
            }
        }

        return value;
    }

    /// <summary>Reads a named value from a struct value, expando, or dictionary.</summary>
    public static bool TryGetMemberValue(object data, string name, out object value)
    {
        if (data is null)
        {
            value = null!;
            return false;
        }

        if (data is IDictionary<string, object?> dict)
        {
            // Parsed values (StructValue), browser JSON data (ExpandoObject) and plain dictionaries all expose
            // names as direct dictionary keys.
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

        value = null!;
        return false;
    }

    /// <summary>The member names <paramref name="data"/> supplies (for the unknown-member policy).</summary>
    public static IEnumerable<string> EnumerateMemberNames(object data)
    {
        if (data is IDictionary<string, object?> dict)
        {
            return dict.Keys;
        }

        return data is IDictionary<string, object> dictObj ? dictObj.Keys : Array.Empty<string>();
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
    public static object GetMemberValueOrThrow(object data, string name)
    {
        if (TryGetMemberValue(data, name, out object value))
        {
            return value;
        }

        throw new CStructWriteException(
            data is not null && !IsWritable(data)
                ? $"No value was supplied for '{name}': a {data.GetType().Name} is not writable data (use a StructValue, a dictionary, or a class implementing ICStructMapped<T> registered with MappedTypes.Register)."
                : $"No value was supplied for '{name}'.");
    }
}
