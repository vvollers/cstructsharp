namespace CStructSharpWeb.Wasm;

using System.Text.Json.Serialization;

/// <summary>
///     A browser-visible error (contract v8): the stable category code, the library's own message (or the curated
///     category text when diagnostics are redacted), and every location fact the library knows - the binary path and
///     offset, the innermost member, and, for a layout error, the source line and column.
/// </summary>
public sealed class ErrorDetailsDto
{
    /// <summary>
    ///     The stable failure category, such as <c>invalid-layout</c>, <c>read-budget</c>, or <c>invalid-json</c>;
    ///     <c>operation-failed</c> for an unexpected failure.
    /// </summary>
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    ///     The library's own diagnostic, or the curated category text when diagnostics are redacted or the failure
    ///     is not a library or input error.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>The selected path or field path, when the failure names one.</summary>
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    /// <summary>The binary byte offset where the operation stopped, when known; not a layout source position.</summary>
    [JsonPropertyName("offset")]
    public long? Offset { get; set; }

    /// <summary>The innermost field the failure concerns, when known.</summary>
    [JsonPropertyName("member")]
    public string? Member { get; set; }

    /// <summary>The layout type of the innermost field, when known; null when redacted.</summary>
    [JsonPropertyName("memberType")]
    public string? MemberType { get; set; }

    /// <summary>The one-based source line of a layout error, when known.</summary>
    [JsonPropertyName("line")]
    public int? Line { get; set; }

    /// <summary>The one-based source column of a layout error, when known.</summary>
    [JsonPropertyName("column")]
    public int? Column { get; set; }
}
