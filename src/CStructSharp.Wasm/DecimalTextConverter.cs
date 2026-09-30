namespace CStructSharpWeb.Wasm;

using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

/// <summary>
///     Reads an option that travels as decimal text but that JavaScript callers may also pass as a plain number: a
///     JSON string is taken as is, and a JSON number is taken as its literal text (<c>10</c> becomes <c>"10"</c>,
///     <c>1.5</c> becomes <c>"1.5"</c>). The option's own parser then accepts or rejects the text, so a number and its
///     decimal text always give the same result and the same failure.
/// </summary>
/// <remarks>
///     Keeping the literal text, instead of converting the number, means a fraction or an exponent reaches the parser
///     unchanged and is rejected with the option's message rather than rounded. JSON <c>null</c> never reaches the
///     converter; the property stays null.
/// </remarks>
internal sealed class DecimalTextConverter : JsonConverter<string?>
{
    /// <summary>Reads a JSON string or number as text.</summary>
    /// <param name="reader">The reader, positioned on the value.</param>
    /// <param name="typeToConvert">The property type (<see cref="string"/>).</param>
    /// <param name="options">The serializer options.</param>
    /// <returns>The string's value, or the number's literal text.</returns>
    /// <exception cref="JsonException">The value is neither a string nor a number.</exception>
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.HasValueSequence
                                        ? Encoding.UTF8.GetString(reader.ValueSequence.ToArray())
                                        : Encoding.UTF8.GetString(reader.ValueSpan),
            _ => throw new JsonException($"Expected a string or a number, found {reader.TokenType}."),
        };
    }

    /// <summary>Writes the text as a JSON string.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="value">The text.</param>
    /// <param name="options">The serializer options.</param>
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
