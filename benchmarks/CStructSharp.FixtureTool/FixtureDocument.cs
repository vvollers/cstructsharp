namespace CStructSharp.FixtureTool;

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>One benchmark fixture as written by benchmarks/fixtures/generate-fixtures.mjs.</summary>
public sealed class FixtureDocument
{
    /// <summary>
    ///     JSON options for reading and writing fixture files: indented camelCase output that keeps null members,
    ///     with a nesting limit deep enough for the recorded expected values of deeply nested fixtures.
    /// </summary>
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        MaxDepth = 4096,
    };

    /// <summary>Gets or sets the unique fixture name; the file is stored as <c>cases/&lt;id&gt;.json</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the benchmark scenario the fixture belongs to, such as <c>S-NESTED</c>.</summary>
    [JsonPropertyName("scenario")]
    public string Scenario { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the labels that select the fixture for benchmark groups (such as <c>warm</c> or
    ///     <c>stream</c>) or relax checks (<c>partial-consume</c> allows a parse to leave trailing bytes).
    /// </summary>
    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    /// <summary>Gets or sets an optional explanation of the fixture; omitted from JSON when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("notes")]
    public string? Notes { get; set; }

    /// <summary>Gets or sets the CStruct layout source that the fixture compiles and parses.</summary>
    [JsonPropertyName("definition")]
    public string Definition { get; set; } = string.Empty;

    /// <summary>
    ///     Gets or sets the layout sources of a compile-only fixture; when present, each one is compiled and no
    ///     bytes are parsed. Omitted from JSON when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("definitions")]
    public List<string>? Definitions { get; set; }

    /// <summary>Gets or sets the layout compile settings: pointer size, alignment and byte order.</summary>
    [JsonPropertyName("options")]
    public FixtureOptions Options { get; set; } = new();

    /// <summary>Gets or sets the name of the struct to parse; defaults to <c>root</c>.</summary>
    [JsonPropertyName("root")]
    public string Root { get; set; } = "root";

    /// <summary>Gets or sets the external variable values passed to the parse, keyed by variable name.</summary>
    [JsonPropertyName("variables")]
    public Dictionary<string, int> Variables { get; set; } = new();

    /// <summary>Gets or sets the read limits and pointer addressing mode; null uses the default read options.</summary>
    [JsonPropertyName("readOptions")]
    public FixtureReadOptions? ReadOptions { get; set; }

    /// <summary>Gets or sets how the input payload is produced; null for fixtures that only compile.</summary>
    [JsonPropertyName("bytes")]
    public FixtureBytes? Bytes { get; set; }

    /// <summary>Gets or sets the payload length in bytes; null when the fixture has no payload.</summary>
    [JsonPropertyName("byteLength")]
    public long? ByteLength { get; set; }

    /// <summary>
    ///     Gets or sets the exception type name the parse must throw, such as a <c>CStructException</c>
    ///     subtype; null when the parse must succeed.
    /// </summary>
    [JsonPropertyName("expectedError")]
    public string? ExpectedError { get; set; }

    /// <summary>
    ///     Gets or sets the canonical JSON of the parsed value when it is small enough to store inline;
    ///     null for failing parses and for results that are recorded only by hash.
    /// </summary>
    [JsonPropertyName("expected")]
    public JsonNode? Expected { get; set; }

    /// <summary>
    ///     Gets or sets the lowercase hexadecimal SHA-256 of the canonical JSON result, which verification compares
    ///     against; null when the parse fails or nothing is parsed.
    /// </summary>
    [JsonPropertyName("expectedSha256")]
    public string? ExpectedSha256 { get; set; }

    /// <summary>Gets or sets the character length of the canonical JSON result; null without a result.</summary>
    [JsonPropertyName("expectedJsonLength")]
    public long? ExpectedJsonLength { get; set; }
}
