namespace CStructSharp.FixtureTool;

using System.Text.Json.Serialization;

public sealed class FixtureReadOptions
{
    [JsonPropertyName("addressingMode")]
    public string? AddressingMode { get; set; }

    [JsonPropertyName("maxArrayElements")]
    public int? MaxArrayElements { get; set; }

    [JsonPropertyName("maxTotalBytesRead")]
    public long? MaxTotalBytesRead { get; set; }

    [JsonPropertyName("maxStringBytes")]
    public long? MaxStringBytes { get; set; }

    [JsonPropertyName("maxPointerDepth")]
    public int? MaxPointerDepth { get; set; }
}
