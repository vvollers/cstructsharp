namespace CStructSharp.FixtureTool;

using System.Dynamic;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;

/// <summary>
///     Serializes a parse result with the same value conventions as the WASM bridge
///     (src/CStructSharp.Wasm/CStructJsonConversion.cs): objects in field order, unions and pointers as tagged
///     objects, enums as {Enum, Name, Value}, byte arrays as Base64, and 64-bit integers beyond the JavaScript safe
///     range as decimal strings. Keeping the shapes identical lets the JS harness compare its JSON.parse(Data)
///     output to the fixture's expected value byte-for-byte. This is a deliberate copy: the bridge project only
///     builds for browser-wasm, so it cannot be referenced from a console tool.
/// </summary>
public static class CanonicalJson
{
    private const long MaximumSafeInteger = 9_007_199_254_740_991;

    public static string Serialize(object? value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(writer, value);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
        case null:
            writer.WriteNullValue();
            return;
        case string text:
            writer.WriteStringValue(text);
            return;
        case bool boolean:
            writer.WriteBooleanValue(boolean);
            return;
        case Guid identifier:
            writer.WriteStringValue(identifier.ToString("D"));
            return;
        case byte n: writer.WriteNumberValue(n); return;
        case sbyte n: writer.WriteNumberValue(n); return;
        case short n: writer.WriteNumberValue(n); return;
        case ushort n: writer.WriteNumberValue(n); return;
        case int n: writer.WriteNumberValue(n); return;
        case uint n: writer.WriteNumberValue(n); return;
        case long n:
            if (n is >= -MaximumSafeInteger and <= MaximumSafeInteger)
            {
                writer.WriteNumberValue(n);
            }
            else
            {
                writer.WriteStringValue(n.ToString(CultureInfo.InvariantCulture));
            }

            return;
        case ulong n:
            if (n <= MaximumSafeInteger)
            {
                writer.WriteNumberValue(n);
            }
            else
            {
                writer.WriteStringValue(n.ToString(CultureInfo.InvariantCulture));
            }

            return;
        case BigInteger n:
            if (n >= -MaximumSafeInteger && n <= MaximumSafeInteger)
            {
                writer.WriteNumberValue((long)n);
            }
            else
            {
                writer.WriteStringValue(n.ToString(CultureInfo.InvariantCulture));
            }

            return;
        case float n: writer.WriteNumberValue(n); return;
        case double n: writer.WriteNumberValue(n); return;
        case decimal n: writer.WriteNumberValue(n); return;
        case char c: writer.WriteStringValue(c.ToString()); return;
        case byte[] bytes:
            writer.WriteBase64StringValue(bytes);
            return;
        case Pointer pointer:
            writer.WriteStartObject();
            writer.WriteNumber("Address", pointer.Address);
            writer.WriteNumber("Depth", pointer.Depth);
            writer.WriteBoolean("IsDereferenced", pointer.IsDereferenced);
            writer.WritePropertyName("Value");
            Write(writer, pointer.Value);
            writer.WriteEndObject();
            return;
        case EnumValueResult enumValue:
            writer.WriteStartObject();
            writer.WriteString("Enum", enumValue.Enum);
            writer.WritePropertyName("Name");
            Write(writer, enumValue.Name);
            writer.WritePropertyName("Value");
            Write(writer, enumValue.Value);
            writer.WriteEndObject();
            return;
        case UnionValue union:
            writer.WriteStartObject();
            writer.WriteString("$kind", "union");
            writer.WriteString("Union", union.UnionName);
            writer.WritePropertyName("RawStorage");
            if (union.HasRawStorage)
            {
                writer.WriteBase64StringValue(union.RawStorage!.Value.Span);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WritePropertyName("Members");
            writer.WriteStartObject();
            foreach (KeyValuePair<string, object?> member in union.Members)
            {
                writer.WritePropertyName(member.Key);
                Write(writer, member.Value);
            }

            writer.WriteEndObject();
            writer.WritePropertyName("SelectedMember");
            Write(writer, union.SelectedMember);
            writer.WriteEndObject();
            return;
        case ExpandoObject expando:
            writer.WriteStartObject();
            foreach (KeyValuePair<string, object?> member in (IDictionary<string, object?>)expando)
            {
                writer.WritePropertyName(member.Key);
                Write(writer, member.Value);
            }

            writer.WriteEndObject();
            return;
        case IDictionary<string, object?> dictionary:
            writer.WriteStartObject();
            foreach (KeyValuePair<string, object?> member in dictionary)
            {
                writer.WritePropertyName(member.Key);
                Write(writer, member.Value);
            }

            writer.WriteEndObject();
            return;
        case IEnumerable<object?> sequence:
            writer.WriteStartArray();
            foreach (object? item in sequence)
            {
                Write(writer, item);
            }

            writer.WriteEndArray();
            return;
        default:
            throw new NotSupportedException("Unsupported parsed value type: " + value.GetType());
        }
    }
}
