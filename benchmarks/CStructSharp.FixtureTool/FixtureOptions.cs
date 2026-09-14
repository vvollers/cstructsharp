namespace CStructSharp.FixtureTool;

using System.Text.Json.Serialization;

public sealed class FixtureOptions
{
    [JsonPropertyName("pointerSize")]
    public byte PointerSize { get; set; } = 8;

    [JsonPropertyName("aligned")]
    public bool Aligned { get; set; }

    [JsonPropertyName("littleEndian")]
    public bool LittleEndian { get; set; } = true;
}
