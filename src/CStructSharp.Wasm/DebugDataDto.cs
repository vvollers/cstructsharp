namespace CStructSharpWeb.Wasm;

using System.Text.Json.Serialization;

/// <summary>One value the reader produced in a debug parse (contract v8): its half-open byte range, path, type, and text.</summary>
public sealed class DebugDataDto
{
    [JsonPropertyName("start")]
    public long Start { get; set; }

    [JsonPropertyName("end")]
    public long End { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>The decoded value as invariant text; decimal digits keep exact 64-bit integers.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
