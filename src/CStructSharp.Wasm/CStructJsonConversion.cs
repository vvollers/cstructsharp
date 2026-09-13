namespace CStructSharpWeb.Wasm;

using System;
using System.Buffers;
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
    [ThreadStatic]
    private static ArrayBufferWriter<byte>? projectionBuffer;

    private static string SerializeParsedValue(object value)
    {
        // Written straight from the parse result (E3.3): the former dictionary copy of the whole tree, the
        // MemoryStream, and the ToArray() are gone; the pooled buffer is reused across calls on this thread.
        ArrayBufferWriter<byte> buffer = projectionBuffer ??= new ArrayBufferWriter<byte>(16 * 1024);
        buffer.ResetWrittenCount();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteJsonValue(writer, value);
        }

        string json = Encoding.UTF8.GetString(buffer.WrittenSpan);
        if (buffer.Capacity > 4 * 1024 * 1024)
        {
            // Do not pin a multi-megabyte buffer to the thread after one unusually large result.
            projectionBuffer = null;
        }

        return json;
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
        case Guid identifier:
            writer.WriteStringValue(identifier.ToString("D"));
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
        case UnionValue unionValue:
            writer.WriteStartObject();
            writer.WriteString("$kind", "union");
            writer.WriteString("Union", unionValue.UnionName);
            writer.WritePropertyName("RawStorage");
            if (unionValue.HasRawStorage)
            {
                writer.WriteBase64StringValue(unionValue.RawStorage!.Value.Span);
            }
            else
            {
                writer.WriteNullValue();
            }

            writer.WritePropertyName("Members");
            writer.WriteStartObject();
            foreach (KeyValuePair<string, object?> member in unionValue.Members)
            {
                writer.WritePropertyName(member.Key);
                WriteJsonValue(writer, member.Value);
            }

            writer.WriteEndObject();
            writer.WritePropertyName("SelectedMember");
            WriteJsonValue(writer, unionValue.SelectedMember);
            writer.WriteEndObject();
            return;
        case Pointer pointer:
            writer.WriteStartObject();
            writer.WriteNumber("Address", pointer.Address);
            writer.WriteNumber("Depth", pointer.Depth);
            writer.WriteBoolean("IsDereferenced", pointer.IsDereferenced);
            writer.WritePropertyName("Value");
            WriteJsonValue(writer, pointer.Value);
            writer.WriteEndObject();
            return;
        case EnumValueResult enumValue:
            writer.WriteStartObject();
            writer.WriteString("Enum", enumValue.Enum);
            writer.WritePropertyName("Name");
            WriteJsonValue(writer, enumValue.Name);
            writer.WritePropertyName("Value");
            WriteJavaScriptSafeInteger(writer, enumValue.Value);
            writer.WriteEndObject();
            return;
        case StructValue structValue:
            // The parsed-struct enumerator is a struct that walks the slot array; no boxed enumerator per object.
            writer.WriteStartObject();
            foreach (KeyValuePair<string, object?> member in structValue)
            {
                writer.WritePropertyName(member.Key);
                WriteJsonValue(writer, member.Value);
            }

            writer.WriteEndObject();
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

        // Typed parsed arrays (E2.3) are written from their span: no boxing and no per-element type dispatch.
        case PrimitiveArray<byte> array:
            writer.WriteStartArray();
            foreach (byte number in array.Span)
            {
                writer.WriteNumberValue(number);
            }

            writer.WriteEndArray();
            return;
        case PrimitiveArray<sbyte> array:
            writer.WriteStartArray();
            foreach (sbyte number in array.Span)
            {
                writer.WriteNumberValue(number);
            }

            writer.WriteEndArray();
            return;
        case PrimitiveArray<bool> array:
            writer.WriteStartArray();
            foreach (bool flag in array.Span)
            {
                writer.WriteBooleanValue(flag);
            }

            writer.WriteEndArray();
            return;
        case PrimitiveArray<short> array:
            writer.WriteStartArray();
            foreach (short number in array.Span)
            {
                writer.WriteNumberValue(number);
            }

            writer.WriteEndArray();
            return;
        case PrimitiveArray<ushort> array:
            writer.WriteStartArray();
            foreach (ushort number in array.Span)
            {
                writer.WriteNumberValue(number);
            }

            writer.WriteEndArray();
            return;
        case PrimitiveArray<int> array:
            writer.WriteStartArray();
            foreach (int number in array.Span)
            {
                writer.WriteNumberValue(number);
            }

            writer.WriteEndArray();
            return;
        case PrimitiveArray<uint> array:
            writer.WriteStartArray();
            foreach (uint number in array.Span)
            {
                writer.WriteNumberValue(number);
            }

            writer.WriteEndArray();
            return;
        case PrimitiveArray<long> array:
            writer.WriteStartArray();
            foreach (long number in array.Span)
            {
                WriteJavaScriptSafeInteger(writer, number);
            }

            writer.WriteEndArray();
            return;
        case PrimitiveArray<ulong> array:
            writer.WriteStartArray();
            foreach (ulong number in array.Span)
            {
                WriteJavaScriptSafeInteger(writer, number);
            }

            writer.WriteEndArray();
            return;
        case PrimitiveArray<float> array:
            writer.WriteStartArray();
            foreach (float number in array.Span)
            {
                writer.WriteNumberValue(number);
            }

            writer.WriteEndArray();
            return;
        case PrimitiveArray<double> array:
            writer.WriteStartArray();
            foreach (double number in array.Span)
            {
                writer.WriteNumberValue(number);
            }

            writer.WriteEndArray();
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
