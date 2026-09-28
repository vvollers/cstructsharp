namespace CStructSharpWeb.Wasm;

/// <summary>
///     A browser-visible error: the stable category code, the library's own message (or the curated category text
///     when diagnostics are redacted), and every location fact the library knows - the binary path and offset, the
///     innermost member, and, for a layout error, the source line and column. The envelope's <c>error</c> object
///     carries these fields under the camelCase names <c>code</c>, <c>message</c>, <c>path</c>, <c>offset</c>,
///     <c>member</c>, <c>memberType</c>, <c>line</c>, and <c>column</c>, always all eight, in that order.
/// </summary>
internal sealed class ErrorDetailsDto
{
    /// <summary>
    ///     The stable failure category, such as <c>invalid-layout</c>, <c>read-budget</c>, or <c>invalid-json</c>;
    ///     <c>operation-failed</c> for an unexpected failure.
    /// </summary>
    public string Code { get; init; } = string.Empty;

    /// <summary>
    ///     The library's own diagnostic, or the curated category text when diagnostics are redacted or the failure
    ///     is not a library or input error.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>The selected path or field path, when the failure names one.</summary>
    public string? Path { get; init; }

    /// <summary>The binary byte offset where the operation stopped, when known; not a layout source position.</summary>
    public long? Offset { get; init; }

    /// <summary>The innermost field the failure concerns, when known.</summary>
    public string? Member { get; init; }

    /// <summary>The layout type of the innermost field, when known; null when redacted.</summary>
    public string? MemberType { get; init; }

    /// <summary>The one-based source line of a layout error, when known.</summary>
    public int? Line { get; init; }

    /// <summary>The one-based source column of a layout error, when known.</summary>
    public int? Column { get; init; }
}
