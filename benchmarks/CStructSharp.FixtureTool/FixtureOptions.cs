namespace CStructSharp.FixtureTool;

using System.Text.Json.Serialization;

/// <summary>A fixture's layout compile settings, read from the <c>options</c> object of its JSON document.</summary>
public sealed class FixtureOptions
{
    /// <summary>Gets or sets the pointer size in bytes; defaults to 8.</summary>
    [JsonPropertyName("pointerSize")]
    public byte PointerSize { get; set; } = 8;

    /// <summary>
    ///     Gets or sets a value indicating whether members use natural alignment and padding; defaults to packed.
    /// </summary>
    [JsonPropertyName("aligned")]
    public bool Aligned { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether multi-byte values are little-endian; defaults to true.
    /// </summary>
    [JsonPropertyName("littleEndian")]
    public bool LittleEndian { get; set; } = true;
}
