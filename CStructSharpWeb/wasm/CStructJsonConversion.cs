namespace CStructSharpWeb.Wasm;

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using CStructSharp;

/// <summary>Contains the explicit JSON conversion rules used at the browser boundary.</summary>
public partial class CStructExports
{
    /// <summary>Changes parsed dynamic objects into dictionaries with a predictable JSON-object representation.</summary>
    private static Dictionary<string, object?> ConvertExpandoToDictionary(ExpandoObject expando)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> member in (IDictionary<string, object?>)expando)
        {
            result[member.Key] = ConvertParsedValue(member.Value);
        }

        return result;
    }

    /// <summary>Recursively normalizes nested structs, unions, enums, and arrays produced by the core parser.</summary>
    private static object? ConvertParsedValue(object? value)
    {
        return value switch
        {
            ExpandoObject nested => ConvertExpandoToDictionary(nested),
            UnionValue union => ConvertUnionToDictionary(union),
            Pointer pointer => new Dictionary<string, object?>
            {
                ["Address"] = pointer.Address,
                ["Depth"] = pointer.Depth,
                ["IsDereferenced"] = pointer.IsDereferenced,
                ["Value"] = ConvertParsedValue(pointer.Value),
            },
            EnumValueResult enumValue => new Dictionary<string, object?>
            {
                ["Enum"] = enumValue.Enum,
                ["Name"] = enumValue.Name,
                ["Value"] = enumValue.Value,
            },
            IEnumerable<object?> sequence => ConvertSequence(sequence),
            _ => value,
        };
    }

    /// <summary>Creates the single tagged browser representation for a lossless managed union value.</summary>
    private static Dictionary<string, object?> ConvertUnionToDictionary(UnionValue union)
    {
        var members = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> member in union.Members)
        {
            members[member.Key] = ConvertParsedValue(member.Value);
        }

        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["$kind"] = "union",
            ["Union"] = union.UnionName,
            ["RawStorage"] = union.HasRawStorage ? union.RawStorage!.Value.ToArray() : null,
            ["Members"] = members,
            ["SelectedMember"] = union.SelectedMember,
        };
    }

    /// <summary>Normalizes every item in a parsed array without losing its declaration order.</summary>
    private static List<object?> ConvertSequence(IEnumerable<object?> sequence)
    {
        var result = new List<object?>();
        foreach (object? item in sequence)
        {
            result.Add(ConvertParsedValue(item));
        }

        return result;
    }

    /// <summary>Converts browser JSON into the primitive, expando, and list values accepted by the core writer.</summary>
    private static object? ConvertJsonElement(JsonElement element)
    {
        switch (element.ValueKind)
        {
        case JsonValueKind.Object:
            {
                if (element.TryGetProperty("$kind", out JsonElement kind))
                {
                    if (kind.ValueKind != JsonValueKind.String ||
                        !string.Equals(kind.GetString(), "union", StringComparison.Ordinal))
                    {
                        throw new JsonException("Unknown tagged value kind.");
                    }

                    return ConvertJsonUnion(element);
                }

                var expando = new ExpandoObject();
                var members = (IDictionary<string, object?>)expando;
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    members[property.Name] = ConvertJsonElement(property.Value);
                }

                return expando;
            }
        case JsonValueKind.Array:
            {
                var items = new List<object?>();
                foreach (JsonElement item in element.EnumerateArray())
                {
                    items.Add(ConvertJsonElement(item));
                }

                return items;
            }
        case JsonValueKind.String:
            return element.GetString();
        case JsonValueKind.Number:
            if (element.TryGetInt64(out long signed))
            {
                return signed;
            }

            if (element.TryGetUInt64(out ulong unsigned))
            {
                return unsigned;
            }

            if (element.TryGetDecimal(out decimal exactDecimal))
            {
                return exactDecimal;
            }

            return element.GetDouble();
        case JsonValueKind.True:
            return true;
        case JsonValueKind.False:
            return false;
        case JsonValueKind.Null:
        case JsonValueKind.Undefined:
            return null;
        default:
            throw new JsonException("Unsupported JSON token: " + element.ValueKind);
        }
    }

    /// <summary>Validates and converts the tagged browser union shape into the managed explicit value model.</summary>
    private static UnionValue ConvertJsonUnion(JsonElement element)
    {
        if (!element.TryGetProperty("Union", out JsonElement unionNameElement) ||
            unionNameElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(unionNameElement.GetString()))
        {
            throw new JsonException("A tagged union requires a non-empty Union name.");
        }

        string unionName = unionNameElement.GetString()!;
        byte[]? rawStorage = null;
        if (element.TryGetProperty("RawStorage", out JsonElement rawElement) &&
            rawElement.ValueKind != JsonValueKind.Null)
        {
            if (rawElement.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("A tagged union RawStorage value must be Base64 text or null.");
            }

            try
            {
                rawStorage = rawElement.GetBytesFromBase64();
            }
            catch (FormatException exception)
            {
                throw new JsonException("A tagged union RawStorage value must contain valid Base64.", exception);
            }
        }

        if (!element.TryGetProperty("Members", out JsonElement membersElement) ||
            membersElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("A tagged union requires a Members object.");
        }

        string? selectedMember = null;
        if (element.TryGetProperty("SelectedMember", out JsonElement selectedElement) &&
            selectedElement.ValueKind != JsonValueKind.Null)
        {
            if (selectedElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(selectedElement.GetString()))
            {
                throw new JsonException("A tagged union SelectedMember value must be a non-empty string or null.");
            }

            selectedMember = selectedElement.GetString();
        }

        if (selectedMember is null)
        {
            if (rawStorage is null)
            {
                throw new JsonException("An unselected tagged union requires RawStorage.");
            }

            return UnionValue.FromRaw(unionName, rawStorage);
        }

        if (!membersElement.TryGetProperty(selectedMember, out JsonElement selectedValueElement))
        {
            throw new JsonException("The selected tagged union member is absent from Members.");
        }

        object? selectedValue = ConvertJsonElement(selectedValueElement);
        return rawStorage is null
                   ? UnionValue.FromMember(unionName, selectedMember, selectedValue)
                   : UnionValue.FromRaw(unionName, rawStorage).WithSelectedMember(selectedMember, selectedValue);
    }

    /// <summary>Parses one browser JSON value into the dynamic shape used by core write APIs.</summary>
    private static object? ParseJsonValue(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new ExpandoObject();
        }

        using JsonDocument document = JsonDocument.Parse(json);
        return ConvertJsonElement(document.RootElement);
    }

    /// <summary>Serializes a parsed struct or union through the boundary's exact recursive number policy.</summary>
    private static string SerializeParsedValue(object value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteJsonValue(writer, ConvertParsedValue(value));
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Writes supported .NET values without reflection or lossy Int64-to-JavaScript conversion.</summary>
    private static void WriteJsonValue(Utf8JsonWriter writer, object? value)
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
        case byte number:
            writer.WriteNumberValue(number);
            return;
        case sbyte number:
            writer.WriteNumberValue(number);
            return;
        case short number:
            writer.WriteNumberValue(number);
            return;
        case ushort number:
            writer.WriteNumberValue(number);
            return;
        case int number:
            writer.WriteNumberValue(number);
            return;
        case uint number:
            writer.WriteNumberValue(number);
            return;
        case long number:
            WriteJavaScriptSafeInteger(writer, number);
            return;
        case ulong number:
            WriteJavaScriptSafeInteger(writer, number);
            return;
        case BigInteger number:
            WriteJavaScriptSafeInteger(writer, number);
            return;
        case float number:
            writer.WriteNumberValue(number);
            return;
        case double number:
            writer.WriteNumberValue(number);
            return;
        case decimal number:
            writer.WriteNumberValue(number);
            return;
        case byte[] bytes:
            writer.WriteBase64StringValue(bytes);
            return;
        case ExpandoObject dynamicObject:
            WriteJsonValue(writer, ConvertExpandoToDictionary(dynamicObject));
            return;
        case UnionValue unionValue:
            WriteJsonValue(writer, ConvertUnionToDictionary(unionValue));
            return;
        case EnumValueResult enumValue:
            WriteJsonValue(writer, ConvertParsedValue(enumValue));
            return;
        case IDictionary<string, object?> dictionary:
            writer.WriteStartObject();
            foreach (KeyValuePair<string, object?> member in dictionary)
            {
                writer.WritePropertyName(member.Key);
                WriteJsonValue(writer, member.Value);
            }

            writer.WriteEndObject();
            return;
        case IEnumerable<object?> sequence:
            writer.WriteStartArray();
            foreach (object? item in sequence)
            {
                WriteJsonValue(writer, item);
            }

            writer.WriteEndArray();
            return;
        default:
            writer.WriteStringValue(value.ToString());
            return;
        }
    }

    /// <summary>Writes signed integers as decimal text only when JavaScript cannot represent them exactly.</summary>
    private static void WriteJavaScriptSafeInteger(Utf8JsonWriter writer, long value)
    {
        const long maximumSafeInteger = 9_007_199_254_740_991;
        if (value is >= -maximumSafeInteger and <= maximumSafeInteger)
        {
            writer.WriteNumberValue(value);
        }
        else
        {
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Writes unsigned integers as decimal text only when JavaScript cannot represent them exactly.</summary>
    private static void WriteJavaScriptSafeInteger(Utf8JsonWriter writer, ulong value)
    {
        const ulong maximumSafeInteger = 9_007_199_254_740_991;
        if (value <= maximumSafeInteger)
        {
            writer.WriteNumberValue(value);
        }
        else
        {
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Writes exact enum mathematics using the same JavaScript-safe number-or-string convention.</summary>
    private static void WriteJavaScriptSafeInteger(Utf8JsonWriter writer, BigInteger value)
    {
        BigInteger maximumSafeInteger = new(9_007_199_254_740_991L);
        if (value >= -maximumSafeInteger && value <= maximumSafeInteger)
        {
            writer.WriteNumberValue((long)value);
        }
        else
        {
            writer.WriteStringValue(value.ToString(CultureInfo.InvariantCulture));
        }
    }
}
