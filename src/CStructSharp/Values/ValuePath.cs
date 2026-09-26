namespace CStructSharp.Values;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using CStructSharp.Diagnostics;

/// <summary>
///     Walks a parsed value tree with the same path grammar the operations use (<c>a.b[2].c</c>, with
///     <c>.value</c>/<c>.address</c> after a pointer), for <see cref="StructValue.Get{T}"/> and
///     <see cref="UnionValue.Get{T}"/>.
/// </summary>
/// <remarks>
///     A successful walk allocates nothing: struct members are looked up by the path's own characters, and the
///     "consumed so far" text that failure messages quote is rebuilt only when a segment fails.
/// </remarks>
internal static class ValuePath
{
    private static readonly char[] Separators = ['.', '['];

    /// <summary>The body of <see cref="StructValue.Get{T}"/> and <see cref="UnionValue.Get{T}"/>.</summary>
    /// <typeparam name="T">The destination type.</typeparam>
    /// <param name="root">The struct or union the path is relative to.</param>
    /// <param name="path">A member name, or a dotted and indexed path.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="CStructPathException">The path is malformed or selects nothing.</exception>
    /// <exception cref="CStructReadException">The value cannot be converted to <typeparamref name="T"/> without loss.</exception>
    public static T Get<T>(object root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (TryResolveTypedElement(root, path, out T? element))
        {
            return element!;
        }

        if (!TryResolve(root, path, out object? value, out string? failure))
        {
            throw new CStructPathException(failure);
        }

        return TypedValueConverter.Convert<T>(value, path);
    }

    /// <summary>The body of both <c>TryGet</c> overloads of <see cref="StructValue"/> and <see cref="UnionValue"/>.</summary>
    /// <typeparam name="T">The destination type.</typeparam>
    /// <param name="root">The struct or union the path is relative to.</param>
    /// <param name="path">A member name, or a dotted and indexed path.</param>
    /// <param name="describeFailure">Whether a missing path produces a <see cref="CStructPathException"/> in <paramref name="failure"/>; the two-argument <c>TryGet</c> skips building it.</param>
    /// <param name="value">The converted value, or the default when the read failed.</param>
    /// <param name="failure">The unthrown path or conversion failure, or <see langword="null"/>.</param>
    /// <returns>Whether <paramref name="value"/> holds the member.</returns>
    public static bool TryGet<T>(object root, string path, bool describeFailure, [MaybeNullWhen(false)] out T value, out CStructException? failure)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (TryResolveTypedElement(root, path, out value))
        {
            failure = null;
            return true;
        }

        if (!TryResolve(root, path, out object? natural, out string? missing, describeFailure))
        {
            value = default;
            failure = describeFailure ? new CStructPathException(missing) : null;
            return false;
        }

        try
        {
            value = TypedValueConverter.Convert<T>(natural, path);
            failure = null;
            return true;
        }
        catch (CStructReadException exception)
        {
            value = default;
            failure = exception;
            return false;
        }
    }

    /// <summary>Resolves <paramref name="path"/> below <paramref name="root"/>, or reports the segment that failed.</summary>
    /// <param name="root">The value the path is relative to.</param>
    /// <param name="path">A member name, or a dotted and indexed path.</param>
    /// <param name="value">The selected value, which may itself be <see langword="null"/>.</param>
    /// <param name="failure">Why the path does not select anything; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when every segment resolved.</returns>
    /// <param name="describe">Whether a failure builds its message; <see langword="false"/> leaves <paramref name="failure"/> empty, for callers that discard it.</param>
    public static bool TryResolve(object? root, string path, out object? value, [NotNullWhen(false)] out string? failure, bool describe = true)
    {
        // A bare member name - what a mapper's Get<T>("count") asks for - needs no walk.
        if (path.Length > 0 && path.IndexOfAny(Separators) < 0)
        {
            if (TryMember(root, path, 0, path.Length, describe, out value, out string? directReason))
            {
                failure = null;
                return true;
            }

            return Fail(describe ? $"Path '{path}' cannot select '{path}' in the value: {directReason}" : string.Empty, out value, out failure);
        }

        return TryWalk(root, path, path.Length, describe, out value, out failure);
    }

    /// <summary>
    ///     Reads an element of a typed numeric array without boxing it, when <paramref name="path"/> ends in an index
    ///     and the array before it stores <typeparamref name="T"/>. Any other shape of path or value returns
    ///     <see langword="false"/>, and the caller resolves the path the ordinary way (which also reports failures).
    /// </summary>
    /// <typeparam name="T">The requested element type.</typeparam>
    /// <param name="root">The value the path is relative to.</param>
    /// <param name="path">The path, for example <c>samples[3]</c>.</param>
    /// <param name="value">The element when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the element was read directly.</returns>
    public static bool TryResolveTypedElement<T>(object? root, string path, [MaybeNullWhen(false)] out T value)
    {
        int open = path.Length > 2 && path[path.Length - 1] == ']' ? path.LastIndexOf('[') : -1;
        if (open > 0 &&
            int.TryParse(path.AsSpan(open + 1, path.Length - open - 2), NumberStyles.None, CultureInfo.InvariantCulture, out int index) &&
            TryWalk(root, path, open, describe: false, out object? container, out _) &&
            container is ITypedElements<T> elements &&
            elements.TryGetElement(index, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Resolves the first <paramref name="limit"/> characters of <paramref name="path"/> segment by segment.</summary>
    private static bool TryWalk(object? root, string path, int limit, bool describe, out object? value, [NotNullWhen(false)] out string? failure)
    {
        object? current = root;
        int position = 0;
        while (position < limit)
        {
            if (path[position] == '.')
            {
                if (position == 0 || position == path.Length - 1 || path[position + 1] == '.' || path[position + 1] == '[')
                {
                    return Fail(describe ? $"Path '{path}' is malformed at position {position}." : string.Empty, out value, out failure);
                }

                position++;
                continue;
            }

            if (path[position] == '[')
            {
                int close = path.IndexOf(']', position);
                if (close < 0 || !int.TryParse(path.AsSpan(position + 1, close - position - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                {
                    return Fail(describe ? $"Path '{path}' has an invalid index at position {position}; indices are non-negative integers in square brackets." : string.Empty, out value, out failure);
                }

                if (!TryIndex(current, index, describe, out current, out string? reason))
                {
                    return Fail(describe ? $"Path '{path}' cannot index '{Consumed(path, position)}': {reason}" : string.Empty, out value, out failure);
                }

                position = close + 1;
                continue;
            }

            int end = position;
            while (end < path.Length && path[end] != '.' && path[end] != '[')
            {
                end++;
            }

            if (!TryMember(current, path, position, end - position, describe, out current, out string? memberReason))
            {
                if (!describe)
                {
                    return Fail(string.Empty, out value, out failure);
                }

                string consumed = Consumed(path, position);
                string owner = consumed.Length == 0 ? "the value" : $"'{consumed}'";
                return Fail($"Path '{path}' cannot select '{path[position..end]}' in {owner}: {memberReason}", out value, out failure);
            }

            position = end;
        }

        value = current;
        failure = null;
        return true;
    }

    /// <summary>
    ///     Rebuilds the normalized text of the segments before <paramref name="limit"/> (<c>a.b[2]</c>), as failure
    ///     messages quote it. Only called for a prefix that already resolved, so its syntax is valid.
    /// </summary>
    private static string Consumed(string path, int limit)
    {
        var consumed = new StringBuilder();
        int position = 0;
        while (position < limit)
        {
            if (path[position] == '.')
            {
                position++;
                continue;
            }

            if (path[position] == '[')
            {
                int close = path.IndexOf(']', position);
                int index = int.Parse(path.AsSpan(position + 1, close - position - 1), NumberStyles.None, CultureInfo.InvariantCulture);
                consumed.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
                position = close + 1;
                continue;
            }

            int end = position;
            while (end < path.Length && path[end] != '.' && path[end] != '[')
            {
                end++;
            }

            if (consumed.Length > 0)
            {
                consumed.Append('.');
            }

            consumed.Append(path, position, end - position);
            position = end;
        }

        return consumed.ToString();
    }

    /// <summary>Reports a failed walk: no value, and <paramref name="message"/> as the reason.</summary>
    private static bool Fail(string message, out object? value, out string failure)
    {
        value = null;
        failure = message;
        return false;
    }

    /// <summary>Selects the member named by a section of <paramref name="path"/>.</summary>
    private static bool TryMember(object? container, string path, int start, int length, bool describe, out object? value, [NotNullWhen(false)] out string? reason)
    {
        switch (container)
        {
        case StructValue structValue:
            // A whole-string name (a mapper's member name, usually the shape's own instance) takes the reference scan.
            if (start == 0 && length == path.Length ? structValue.TryGetValue(path, out value) : structValue.TryGetValue(path, start, length, out value))
            {
                reason = null;
                return true;
            }

            reason = describe ? "the struct has no such member (members: " + Describe(structValue.Keys) + ")." : string.Empty;
            return false;
        case UnionValue union:
            if (union.TryGetValue(path.Substring(start, length), out value))
            {
                reason = null;
                return true;
            }

            reason = describe ? $"union '{union.UnionName}' has no such member (members: " + Describe(union.Keys) + ")." : string.Empty;
            return false;
        case Pointer pointer:
            if (IsSegment(path, start, length, "value"))
            {
                if (!pointer.IsDereferenced)
                {
                    value = null;
                    reason = describe ? "the pointer was not dereferenced (read with DereferencePointers enabled)." : string.Empty;
                    return false;
                }

                value = pointer.Value;
                reason = null;
                return true;
            }

            if (IsSegment(path, start, length, "address"))
            {
                value = pointer.Address;
                reason = null;
                return true;
            }

            value = null;
            reason = describe ? "a pointer has only 'value' and 'address'." : string.Empty;
            return false;
        case null:
            value = null;
            reason = describe ? "the value is null." : string.Empty;
            return false;
        default:
            value = null;
            reason = describe ? $"a {Describe(container.GetType())} has no members." : string.Empty;
            return false;
        }
    }

    /// <summary>Whether the section of <paramref name="path"/> is exactly <paramref name="name"/>.</summary>
    private static bool IsSegment(string path, int start, int length, string name)
    {
        return length == name.Length && string.CompareOrdinal(path, start, name, 0, length) == 0;
    }

    /// <summary>Selects element <paramref name="index"/> of an array value or a character of a string.</summary>
    private static bool TryIndex(object? container, int index, bool describe, out object? value, [NotNullWhen(false)] out string? reason)
    {
        switch (container)
        {
        case IList list:
            if (index < list.Count)
            {
                value = list[index];
                reason = null;
                return true;
            }

            value = null;
            reason = describe ? $"index {index} is outside the {list.Count} element(s)." : string.Empty;
            return false;
        case string text:
            if (index < text.Length)
            {
                value = text[index];
                reason = null;
                return true;
            }

            value = null;
            reason = describe ? $"index {index} is outside the {text.Length} character(s)." : string.Empty;
            return false;
        case null:
            value = null;
            reason = describe ? "the value is null." : string.Empty;
            return false;
        default:
            value = null;
            reason = describe ? $"a {Describe(container.GetType())} is not an array." : string.Empty;
            return false;
        }
    }

    private static string Describe(IEnumerable<string> names)
    {
        return string.Join(", ", names);
    }

    private static string Describe(Type type)
    {
        return type == typeof(StructValue) ? "struct"
             : type == typeof(UnionValue) ? "union"
             : type == typeof(Pointer) ? "pointer"
             : type == typeof(EnumValueResult) ? "enum value"
             : type.Name;
    }
}
