namespace CStructSharpWeb.Wasm;

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Text;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Values;

/// <summary>Contains the explicit JSON conversion rules used at the browser boundary.</summary>
public partial class CStructExports
{
    private static readonly byte[] ParseEnvelopeHead = Encoding.UTF8.GetBytes("{\"ContractVersion\":" + InteropContractVersion + ",\"Operation\":\"parse\",\"Success\":true,\"Data\":");
    private static readonly byte[] ParseEnvelopeDebugData = ",\"DebugData\":"u8.ToArray();
    private static readonly byte[] ParseEnvelopeTail = ",\"Error\":null}"u8.ToArray();
    private static readonly byte[] EmptyArray = "[]"u8.ToArray();

    [ThreadStatic]
    private static ParsedJsonWriter? projectionWriter;

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

    /// <summary>
    ///     Writes the whole successful parse envelope in one pass (E3.3(b), contract v7): the parsed value is
    ///     projected straight into the envelope as a JSON value - no intermediate Data string, no escaping pass, one
    ///     JSON.parse on the JavaScript side.
    /// </summary>
    private static string SerializeParseEnvelope(object result, List<DebugDataDto> debugData)
    {
        ParsedJsonWriter writer = projectionWriter ??= new ParsedJsonWriter(16 * 1024);
        writer.Reset();
        writer.WriteRawBytes(ParseEnvelopeHead);
        writer.WriteValue(result);
        writer.WriteRawBytes(ParseEnvelopeDebugData);
        if (debugData.Count == 0)
        {
            writer.WriteRawBytes(EmptyArray);
        }
        else
        {
            writer.WriteRawBytes(JsonSerializer.SerializeToUtf8Bytes(debugData, CStructJsonContext.Default.ListDebugDataDto));
        }

        writer.WriteRawBytes(ParseEnvelopeTail);
        return FinishProjection(writer);
    }

    /// <summary>Serializes a parsed struct or union alone (benchmark projection cases).</summary>
    private static string SerializeParsedValue(object value)
    {
        ParsedJsonWriter writer = projectionWriter ??= new ParsedJsonWriter(16 * 1024);
        writer.Reset();
        writer.WriteValue(value);
        return FinishProjection(writer);
    }

    private static string FinishProjection(ParsedJsonWriter writer)
    {
        string json = Encoding.UTF8.GetString(writer.WrittenSpan);
        if (writer.Capacity > 4 * 1024 * 1024)
        {
            // Do not pin a multi-megabyte buffer to the thread after one unusually large result.
            projectionWriter = null;
        }

        return json;
    }
}
