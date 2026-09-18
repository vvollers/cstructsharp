namespace CStructSharp.FixtureTool;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>One benchmark fixture as written by benchmarks/fixtures/generate-fixtures.mjs.</summary>
public sealed class FixtureDocument
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 4096,
    };

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("scenario")]
    public string Scenario { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("definition")]
    public string Definition { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("definitions")]
    public List<string>? Definitions { get; set; }

    [JsonPropertyName("options")]
    public FixtureOptions Options { get; set; } = new();

    [JsonPropertyName("root")]
    public string Root { get; set; } = "root";

    [JsonPropertyName("variables")]
    public Dictionary<string, int> Variables { get; set; } = new();

    [JsonPropertyName("readOptions")]
    public FixtureReadOptions? ReadOptions { get; set; }

    [JsonPropertyName("bytes")]
    public FixtureBytes? Bytes { get; set; }

    [JsonPropertyName("byteLength")]
    public long? ByteLength { get; set; }

    [JsonPropertyName("expectedError")]
    public string? ExpectedError { get; set; }

    [JsonPropertyName("expected")]
    public JsonNode? Expected { get; set; }

    [JsonPropertyName("expectedSha256")]
    public string? ExpectedSha256 { get; set; }

    [JsonPropertyName("expectedJsonLength")]
    public long? ExpectedJsonLength { get; set; }
}
