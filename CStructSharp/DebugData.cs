namespace CStructSharp;

using System.Linq;
using System.Text.Json.Serialization;
using CStructSharp.Structure;

/// <summary>Describes the bytes, path, type, and parsed value for one item read in debug mode.</summary>
/// <remarks>
///     Records are produced by <see cref="CStruct.ParseStreamWithDebug(System.IO.Stream)"/> and its overloads.
///     <see cref="CurPos"/> is inclusive, <see cref="EndPos"/> is exclusive, and <see cref="Buffer"/> is a snapshot.
/// </remarks>
public struct DebugData()
{
    /// <summary>Gets or sets the inclusive zero-based start offset of the captured field.</summary>
    [JsonPropertyName("curPos")]
    public long CurPos { get; set; } = 0;

    /// <summary>Gets or sets the exclusive zero-based end offset of the captured field.</summary>
    [JsonPropertyName("endPos")]
    public long EndPos { get; set; } = 0;

    [JsonIgnore]
    internal CStructElement[] DebugStack { get; set; } = [];

    /// <summary>Gets the dot-separated declaration path used while reading the field.</summary>
    [JsonPropertyName("debugStackString")]
    public string DebugStackString
    {
        get => this.DebugStack == null ? string.Empty : string.Join(".", this.DebugStack.Select(o => o.Name.Name));
    }

    /// <summary>Gets or sets the layout type spelling associated with the captured field.</summary>
    [JsonPropertyName("type")]
    public string? TypeName { get; set; } = null;

    /// <summary>Gets or sets the parsed semantic value associated with the captured field.</summary>
    [JsonPropertyName("value")]
    public object? Value { get; set; } = null;

    /// <summary>Gets or sets the exact captured bytes as unsigned integer values suitable for JSON.</summary>
    [JsonPropertyName("buffer")]
    public int[] Buffer { get; set; } = new int[] { };
}
