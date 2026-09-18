namespace CStructSharp.Values;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using CStructSharp.Diagnostics;

/// <summary>
///     Walks a parsed value tree with the same path grammar the operations use (<c>a.b[2].c</c>, with
///     <c>.value</c>/<c>.address</c> after a pointer), for <see cref="StructValue.Get{T}"/> and
///     <see cref="UnionValue.Get{T}"/>.
/// </summary>
internal static class ValuePath
{
    /// <summary>Resolves <paramref name="path"/> below <paramref name="root"/>, or reports the segment that failed.</summary>
    /// <param name="root">The value the path is relative to.</param>
    /// <param name="path">A member name, or a dotted and indexed path.</param>
    /// <param name="value">The selected value, which may itself be <see langword="null"/>.</param>
    /// <param name="failure">Why the path does not select anything; <see langword="null"/> on success.</param>
    /// <returns><see langword="true"/> when every segment resolved.</returns>
    public static bool TryResolve(object? root, string path, out object? value, [NotNullWhen(false)] out string? failure)
    {
        object? current = root;
        int position = 0;
        string consumed = string.Empty;
        while (position < path.Length)
        {
            if (path[position] == '.')
            {
                if (position == 0 || position == path.Length - 1 || path[position + 1] == '.' || path[position + 1] == '[')
                {
                    return Fail($"Path '{path}' is malformed at position {position}.", out value, out failure);
                }

                position++;
                continue;
            }

            if (path[position] == '[')
            {
                int close = path.IndexOf(']', position);
                if (close < 0 || !int.TryParse(path.AsSpan(position + 1, close - position - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                {
                    return Fail($"Path '{path}' has an invalid index at position {position}; indices are non-negative integers in square brackets.", out value, out failure);
                }

                if (!TryIndex(current, index, out current, out string? reason))
                {
                    return Fail($"Path '{path}' cannot index '{consumed}': {reason}", out value, out failure);
                }

                consumed = string.Concat(consumed, "[", index.ToString(CultureInfo.InvariantCulture), "]");
                position = close + 1;
                continue;
            }

            int end = position;
            while (end < path.Length && path[end] != '.' && path[end] != '[')
            {
                end++;
            }

            string name = path[position..end];
            if (!TryMember(current, name, out current, out string? memberReason))
            {
                string owner = consumed.Length == 0 ? "the value" : $"'{consumed}'";
                return Fail($"Path '{path}' cannot select '{name}' in {owner}: {memberReason}", out value, out failure);
            }

            consumed = consumed.Length == 0 ? name : consumed + "." + name;
            position = end;
        }

        value = current;
        failure = null;
        return true;
    }

    private static bool Fail(string message, out object? value, out string failure)
    {
        value = null;
        failure = message;
        return false;
    }

    private static bool TryMember(object? container, string name, out object? value, [NotNullWhen(false)] out string? reason)
    {
        switch (container)
        {
        case StructValue structValue:
            if (structValue.TryGetValue(name, out value))
            {
                reason = null;
                return true;
            }

            reason = "the struct has no such member (members: " + Describe(structValue.Keys) + ").";
            return false;
        case UnionValue union:
            if (union.TryGetValue(name, out value))
            {
                reason = null;
                return true;
            }

            reason = $"union '{union.UnionName}' has no such member (members: " + Describe(union.Keys) + ").";
            return false;
        case Pointer pointer:
            switch (name)
            {
            case "value":
                if (!pointer.IsDereferenced)
                {
                    value = null;
                    reason = "the pointer was not dereferenced (read with DereferencePointers enabled).";
                    return false;
                }

                value = pointer.Value;
                reason = null;
                return true;
            case "address":
                value = pointer.Address;
                reason = null;
                return true;
            default:
                value = null;
                reason = "a pointer has only 'value' and 'address'.";
                return false;
            }

        case null:
            value = null;
            reason = "the value is null.";
            return false;
        default:
            value = null;
            reason = $"a {Describe(container.GetType())} has no members.";
            return false;
        }
    }

    private static bool TryIndex(object? container, int index, out object? value, [NotNullWhen(false)] out string? reason)
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
            reason = $"index {index} is outside the {list.Count} element(s).";
            return false;
        case string text:
            if (index < text.Length)
            {
                value = text[index];
                reason = null;
                return true;
            }

            value = null;
            reason = $"index {index} is outside the {text.Length} character(s).";
            return false;
        case null:
            value = null;
            reason = "the value is null.";
            return false;
        default:
            value = null;
            reason = $"a {Describe(container.GetType())} is not an array.";
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
