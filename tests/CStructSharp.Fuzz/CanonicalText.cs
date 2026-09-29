namespace CStructSharp.Fuzzing;

using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The canonical rendering of one operation's observable outcome, one <c>label = text</c> line per fact, so two
///     implementations can be compared as text and a difference shows as a readable line diff. Values carry their CLR
///     type names and member order; failures carry their type, message, error code, member, member type, path, offset,
///     and inner failures; floating-point values carry their bit patterns. The fuzz replay digest hashes it, and the
///     managed engine differential compares it.
/// </summary>
internal sealed class CanonicalText
{
    /// <summary>How deeply nested values are rendered before the rendering stops, guarding against cycles.</summary>
    private const int MaxDepth = 64;

    /// <summary>The rendering so far.</summary>
    private readonly StringBuilder text = new();

    /// <summary>A readable, generic-aware CLR type name, such as <c>PrimitiveArray&lt;UInt16&gt;</c> or <c>List&lt;Object&gt;</c>.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The name.</returns>
    public static string TypeName(Type type)
    {
        if (type.IsArray)
        {
            return TypeName(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (!type.IsGenericType)
        {
            return type.Name;
        }

        string name = type.Name;
        int tick = name.IndexOf('`', StringComparison.Ordinal);
        return (tick < 0 ? name : name[..tick]) + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
    }

    /// <summary>Appends one fact.</summary>
    /// <param name="label">What the fact describes, such as <c>result.header.size</c>.</param>
    /// <param name="value">The rendered fact.</param>
    public void Line(string label, string value) => this.text.Append(label).Append(" = ").Append(value).Append('\n');

    /// <summary>Appends a value and, for composite values, one line per member or element beneath it.</summary>
    /// <param name="label">The value's label.</param>
    /// <param name="value">The value.</param>
    public void Value(string label, object? value) => this.Value(label, value, 0);

    /// <summary>Appends a byte sequence as its length and hexadecimal contents.</summary>
    /// <param name="label">The bytes' label.</param>
    /// <param name="bytes">The bytes.</param>
    public void Bytes(string label, ReadOnlySpan<byte> bytes) => this.Line(label, Hex(bytes));

    /// <summary>Appends debug records: each record's path, range, type name and bytes, then its value.</summary>
    /// <param name="label">The records' label.</param>
    /// <param name="records">The records in the order the operation returned them.</param>
    public void Debug(string label, IReadOnlyList<DebugData> records)
    {
        this.Line(label, TypeName(records.GetType()) + " [" + records.Count + "]");
        for (int index = 0; index < records.Count; index++)
        {
            DebugData record = records[index];
            string item = label + "[" + index + "]";
            string range = "path=" + Quote(record.Path) + " start=" + record.Start + " end=" + record.End + " length=" + record.Length +
                           " type=" + (record.TypeName is null ? "null" : Quote(record.TypeName)) + " bytes=" + Hex(record.Bytes.Span);
            this.Line(item, range);
            this.Value(item + ".value", record.Value);
        }
    }

    /// <summary>
    ///     Appends a failure: its CLR type, message, and, for a library failure, error code, member, member type, path,
    ///     and offset; argument names, layout positions, and inner failures follow. A line break in a message is rendered
    ///     as a line feed, since system messages break lines with the platform's newline.
    /// </summary>
    /// <param name="label">The failure's label.</param>
    /// <param name="failure">The failure.</param>
    public void Failure(string label, Exception failure) => this.Failure(label, failure, 0);

    /// <summary>Returns the rendering.</summary>
    /// <returns>The lines appended so far, each ending in a line feed.</returns>
    public override string ToString() => this.text.ToString();

    /// <summary>Renders bytes as <c>[length] HEX</c>.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text.</returns>
    private static string Hex(ReadOnlySpan<byte> bytes) => bytes.IsEmpty ? "[0]" : "[" + bytes.Length + "] " + Convert.ToHexString(bytes);

    /// <summary>Quotes text, escaping quotes, backslashes and every character outside printable ASCII so NULs and padding stay visible.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The quoted text.</returns>
    private static string Quote(string value)
    {
        var quoted = new StringBuilder(value.Length + 2).Append('"');
        foreach (char character in value)
        {
            if (character is '"' or '\\')
            {
                quoted.Append('\\').Append(character);
            }
            else if (character is < ' ' or > '~')
            {
                quoted.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
            }
            else
            {
                quoted.Append(character);
            }
        }

        return quoted.Append('"').ToString();
    }

    /// <summary>
    ///     Renders a scalar as its type name and invariant text; floating-point values add their bit pattern. A
    ///     <see cref="Guid"/> is a scalar too, rendered in its hyphenated form, so its rendering does not depend on the
    ///     public properties a runtime version gives it.
    /// </summary>
    /// <param name="value">A non-null scalar.</param>
    /// <returns>The text, or <see langword="null"/> when the value is not a scalar.</returns>
    private static string? Scalar(object value)
    {
        string type = TypeName(value.GetType());
        return value switch
        {
            string text => type + " " + Quote(text),
            char character => type + " " + Quote(character.ToString()),
            bool flag => type + " " + (flag ? "true" : "false"),
            double number => type + " " + number.ToString("R", CultureInfo.InvariantCulture) + " bits=" + BitConverter.DoubleToInt64Bits(number).ToString("X16", CultureInfo.InvariantCulture),
            float number => type + " " + number.ToString("R", CultureInfo.InvariantCulture) + " bits=" + BitConverter.SingleToInt32Bits(number).ToString("X8", CultureInfo.InvariantCulture),
            Half number => type + " " + number.ToString(CultureInfo.InvariantCulture) + " bits=" + BitConverter.HalfToInt16Bits(number).ToString("X4", CultureInfo.InvariantCulture),
            Guid guid => type + " " + guid.ToString("D", CultureInfo.InvariantCulture),
            Enum member => type + " " + member + "(" + Convert.ToString(Convert.ChangeType(member, Enum.GetUnderlyingType(member.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + ")",
            BigInteger or Int128 or UInt128 or decimal => type + " " + ((IFormattable)value).ToString(null, CultureInfo.InvariantCulture),
            IFormattable formattable when value.GetType().IsPrimitive => type + " " + formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => null,
        };
    }

    /// <summary>Appends a value at a nesting depth; see <see cref="Value(string, object?)"/>.</summary>
    /// <param name="label">The value's label.</param>
    /// <param name="value">The value.</param>
    /// <param name="depth">How many composite values enclose this one.</param>
    private void Value(string label, object? value, int depth)
    {
        if (value is null)
        {
            this.Line(label, "null");
            return;
        }

        if (depth > MaxDepth)
        {
            this.Line(label, TypeName(value.GetType()) + " (not rendered: deeper than " + MaxDepth + " levels)");
            return;
        }

        if (Scalar(value) is { } scalar)
        {
            this.Line(label, scalar);
            return;
        }

        switch (value)
        {
        case byte[] bytes:
            this.Line(label, "Byte[] " + Hex(bytes));
            return;

        case StructValue structValue:
            this.Line(label, "StructValue {" + structValue.Count + "}");
            foreach (KeyValuePair<string, object?> member in structValue)
            {
                this.Value(label + "." + member.Key, member.Value, depth + 1);
            }

            return;

        case UnionValue union:
            // A parsed union keeps its complete storage and a decoded view per member; a supplied one may select one.
            string storage = union.RawStorage is { } raw ? Hex(raw.Span) : "none";
            this.Line(label, "UnionValue " + Quote(union.UnionName) + " {" + union.Count + "} selected=" + (union.HasSelection ? Quote(union.SelectedMember!) : "none") + " raw=" + storage);
            foreach (KeyValuePair<string, object?> member in union.Members)
            {
                this.Value(label + "." + member.Key, member.Value, depth + 1);
            }

            if (union.HasSelection)
            {
                this.Value(label + ".(selected)", union.SelectedValue, depth + 1);
            }

            return;

        case Values.Pointer pointer:
            this.Line(label, "Pointer address=" + pointer.Address + " depth=" + pointer.Depth + " dereferenced=" + pointer.IsDereferenced + " null=" + pointer.IsNull);
            this.Value(label + "->", pointer.Value, depth + 1);
            return;

        case FlagValueResult flags:
            string flagText = "FlagValueResult " + flags.Enum + " name=" + (flags.Name ?? "null") + " value=" + flags.Value.ToString(CultureInfo.InvariantCulture) +
                              " raw=" + flags.RawBits + " storage=" + flags.StorageType + " bits=" + flags.BitWidth + " signed=" + flags.IsSigned +
                              " names=[" + string.Join(",", flags.Names) + "] remainder=" + flags.Remainder;
            this.Line(label, flagText);
            return;

        case EnumValueResult enumValue:
            string enumText = TypeName(enumValue.GetType()) + " " + enumValue.Enum + " name=" + (enumValue.Name ?? "null") + " value=" +
                              enumValue.Value.ToString(CultureInfo.InvariantCulture) + " raw=" + enumValue.RawBits + " storage=" + enumValue.StorageType +
                              " bits=" + enumValue.BitWidth + " signed=" + enumValue.IsSigned;
            this.Line(label, enumText);
            return;

        case IDictionary dictionary:
            this.Line(label, TypeName(value.GetType()) + " {" + dictionary.Count + "}");
            foreach (DictionaryEntry entry in dictionary)
            {
                this.Value(label + "[" + Quote(Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "null") + "]", entry.Value, depth + 1);
            }

            return;

        case IEnumerable items:
            // PrimitiveArray<T>, List<object?> and CLR arrays render differently only by their type names, which is the point.
            List<object?> elements = items.Cast<object?>().ToList();
            this.Line(label, TypeName(value.GetType()) + " [" + elements.Count + "]");
            for (int index = 0; index < elements.Count; index++)
            {
                this.Value(label + "[" + index + "]", elements[index], depth + 1);
            }

            return;
        }

        // Any other object - a mapped class, a record - renders its public properties and fields in declaration order.
        Type type = value.GetType();
        this.Line(label, TypeName(type));
        foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance).OrderBy(member => member.MetadataToken))
        {
            if (member is PropertyInfo { CanRead: true } property && property.GetIndexParameters().Length == 0)
            {
                this.Value(label + "." + property.Name, property.GetValue(value), depth + 1);
            }
            else if (member is FieldInfo field)
            {
                this.Value(label + "." + field.Name, field.GetValue(value), depth + 1);
            }
        }
    }

    /// <summary>Appends a failure at a nesting depth of inner failures; see <see cref="Failure(string, Exception)"/>.</summary>
    /// <param name="label">The failure's label.</param>
    /// <param name="failure">The failure.</param>
    /// <param name="depth">How many failures enclose this one as their inner failure.</param>
    private void Failure(string label, Exception failure, int depth)
    {
        this.Line(label, "failure " + failure.GetType().FullName);
        this.Line(label + ".message", Quote(failure.Message.Replace("\r\n", "\n", StringComparison.Ordinal)));
        if (failure is CStructException library)
        {
            this.Line(label + ".code", library.Code.ToString());
            this.Line(label + ".member", library.Member is null ? "null" : Quote(library.Member));
            this.Line(label + ".memberType", library.MemberType is null ? "null" : Quote(library.MemberType));
            this.Line(label + ".path", library.Path is null ? "null" : Quote(library.Path));
            this.Line(label + ".offset", library.Offset?.ToString(CultureInfo.InvariantCulture) ?? "null");
        }

        if (failure is CStructLayoutException layout)
        {
            this.Line(label + ".position", layout.Line + ":" + layout.Column);
        }

        if (failure is ArgumentException argument)
        {
            this.Line(label + ".parameter", argument.ParamName ?? "null");
        }

        if (failure.InnerException is { } inner && depth < MaxDepth)
        {
            this.Failure(label + ".inner", inner, depth + 1);
        }
    }
}
