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
    /// <param name="data">The caller's write data, or null for a root that may be null.</param>
    /// <param name="rootName">The layout name of the root struct.</param>
    /// <returns>The member named <paramref name="rootName"/> when present; otherwise <paramref name="data"/>.</returns>
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
    /// <param name="data">The caller's write data for one struct, or null.</param>
    /// <param name="composite">The compiled struct whose shape the new value takes.</param>
    /// <returns>
    ///     A new <see cref="StructValue"/> filled by the mapped class, with nested mapped instances converted too;
    ///     otherwise <paramref name="data"/> itself.
    /// </returns>
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

    /// <summary>Materializes one nested element unless it already exposes members or is a union value.</summary>
    /// <param name="value">The nested element supplied by the mapper.</param>
    /// <param name="nested">The compiled struct type of the element.</param>
    /// <returns>The materialized struct value, or <paramref name="value"/> when no conversion applies.</returns>
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
    /// <param name="data">The candidate write data; must not be null.</param>
    /// <returns>True for a member source or an instance of a registered mapped class.</returns>
    public static bool IsWritable(object data)
    {
        return IsMemberSource(data) || MappedTypes.IsMapped(data.GetType());
    }

    /// <summary>Whether <paramref name="data"/> exposes members directly: a struct value, an expando, or a dictionary.</summary>
    /// <param name="data">The candidate write data.</param>
    /// <returns>True when <paramref name="data"/> is a string-keyed dictionary.</returns>
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
    /// <param name="data">The caller's write data, the root the path starts from.</param>
    /// <param name="segments">The parsed path: per segment, a member name and then zero or more indexes.</param>
    /// <returns>The value the whole path selects.</returns>
    /// <exception cref="CStructWriteException">A named member is missing or an index is not supported.</exception>
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
    /// <param name="data">The object to look in; null finds nothing.</param>
    /// <param name="name">The member name, matched as a dictionary key.</param>
    /// <param name="value">The member's value when found (which may itself be null); otherwise null.</param>
    /// <returns>True when <paramref name="data"/> has a member called <paramref name="name"/>.</returns>
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
    /// <param name="data">The caller's write data for one struct.</param>
    /// <returns>The dictionary keys of <paramref name="data"/>, or an empty sequence for any other object.</returns>
    public static IEnumerable<string> EnumerateMemberNames(object data)
    {
        if (data is IDictionary<string, object?> dict)
        {
            return dict.Keys;
        }

        return data is IDictionary<string, object> dictObj ? dictObj.Keys : Array.Empty<string>();
    }

    /// <summary>Gets one item from an array-like value and reports a clear error for an invalid index.</summary>
    /// <param name="value">The array-like value: a list, an array, or a string.</param>
    /// <param name="index">The zero-based element index.</param>
    /// <returns>The element at <paramref name="index"/>; for a string, its character.</returns>
    /// <exception cref="CStructWriteException">The value cannot be indexed, or the array element is null.</exception>
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
    /// <param name="data">The object to look in.</param>
    /// <param name="name">The member name, matched as a dictionary key.</param>
    /// <returns>The member's value.</returns>
    /// <exception cref="CStructWriteException">
    ///     <paramref name="data"/> has no such member; the message says when the object is not writable data.
    /// </exception>
    public static object GetMemberValueOrThrow(object data, string name)
    {
        if (TryGetMemberValue(data, name, out object value))
        {
            return value;
        }

        throw new CStructWriteException(
            data is not null && !IsWritable(data)
                ? $"No value was supplied for '{name}': a {data.GetType().Name} is not writable data (use a StructValue, a dictionary, or a class implementing ICStructMapped<T> registered with MappedTypes.Register)."
                : WriteFailures.NoValueSupplied(name));
    }
}
