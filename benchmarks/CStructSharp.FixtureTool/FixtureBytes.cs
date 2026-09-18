namespace CStructSharp.FixtureTool;

using System.Text.Json.Serialization;

public sealed class FixtureBytes
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "hex";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("hex")]
    public string? Hex { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("seed")]
    public uint? Seed { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("size")]
    public int? Size { get; set; }
}
