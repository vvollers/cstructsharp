namespace CStructSharp.FixtureTool;

using System.Text.Json.Serialization;

/// <summary>
///     The optional <c>readOptions</c> object of a fixture document: read settings that override the library defaults
///     when the fixture is verified. A null property is left out of the JSON and keeps its default.
/// </summary>
public sealed class FixtureReadOptions
{
    /// <summary>
    ///     Gets or sets the pointer addressing mode: <c>Relative</c> (case-insensitive) selects relative addressing,
    ///     and any other value, or none, selects absolute addressing.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("addressingMode")]
    public string? AddressingMode { get; set; }

    /// <summary>Gets or sets the most elements one array may hold, or null for the library default.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxArrayElements")]
    public int? MaxArrayElements { get; set; }

    /// <summary>Gets or sets the most bytes one read may consume, or null for the library default.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxTotalBytesRead")]
    public long? MaxTotalBytesRead { get; set; }

    /// <summary>Gets or sets the most bytes one string may occupy, or null for the library default.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxStringBytes")]
    public long? MaxStringBytes { get; set; }

    /// <summary>Gets or sets the longest pointer chain a read may follow, or null for the library default.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxPointerDepth")]
    public int? MaxPointerDepth { get; set; }
}
