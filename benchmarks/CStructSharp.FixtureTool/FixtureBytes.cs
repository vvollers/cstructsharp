namespace CStructSharp.FixtureTool;

using System.Text.Json.Serialization;

public sealed class FixtureBytes
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "hex";

    [JsonPropertyName("hex")]
    public string? Hex { get; set; }

    [JsonPropertyName("file")]
    public string? File { get; set; }

    [JsonPropertyName("seed")]
    public uint? Seed { get; set; }

    [JsonPropertyName("size")]
    public int? Size { get; set; }
}
