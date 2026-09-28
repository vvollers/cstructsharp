namespace CStructSharpWeb.Wasm;

using System.Text.Json.Serialization;

/// <summary>One value the reader produced in a debug parse (contract v8): its half-open byte range, path, type, and text.</summary>
public sealed class DebugDataDto
{
    /// <summary>The first byte of the value's range, relative to the operation origin.</summary>
    [JsonPropertyName("start")]
    public long Start { get; set; }

    /// <summary>The exclusive end of the value's range, relative to the operation origin.</summary>
    [JsonPropertyName("end")]
    public long End { get; set; }

    /// <summary>The value's path, for example <c>header.length</c>.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>The value's declared type name, or <c>unknown</c> when the reader recorded none.</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>The decoded value as invariant text; decimal digits keep exact 64-bit integers.</summary>
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
