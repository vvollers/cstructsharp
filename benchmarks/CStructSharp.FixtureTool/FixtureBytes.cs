namespace CStructSharp.FixtureTool;

using System.Text.Json.Serialization;

/// <summary>
///     Describes how a fixture's input payload is produced: inline hexadecimal, a data file next to the fixtures,
///     or a seeded xorshift generator. <see cref="FixtureLoader.MaterializeBytes"/> turns it into bytes.
/// </summary>
public sealed class FixtureBytes
{
    /// <summary>Gets or sets the generator kind: <c>hex</c>, <c>file</c> or <c>xorshift</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "hex";

    /// <summary>Gets or sets the payload as hexadecimal text for the <c>hex</c> kind; omitted when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("hex")]
    public string? Hex { get; set; }

    /// <summary>
    ///     Gets or sets the payload file path, relative to the fixture directory, for the <c>file</c> kind;
    ///     omitted from JSON when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("file")]
    public string? File { get; set; }

    /// <summary>
    ///     Gets or sets the xorshift generator seed for the <c>xorshift</c> kind (zero is treated as one);
    ///     omitted from JSON when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("seed")]
    public uint? Seed { get; set; }

    /// <summary>Gets or sets the byte count to generate for the <c>xorshift</c> kind; omitted when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("size")]
    public int? Size { get; set; }
}
