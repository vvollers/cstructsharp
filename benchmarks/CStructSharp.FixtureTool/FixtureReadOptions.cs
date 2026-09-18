namespace CStructSharp.FixtureTool;

using System.Text.Json.Serialization;

public sealed class FixtureReadOptions
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("addressingMode")]
    public string? AddressingMode { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxArrayElements")]
    public int? MaxArrayElements { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxTotalBytesRead")]
    public long? MaxTotalBytesRead { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxStringBytes")]
    public long? MaxStringBytes { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("maxPointerDepth")]
    public int? MaxPointerDepth { get; set; }
}
