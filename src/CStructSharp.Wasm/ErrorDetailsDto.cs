namespace CStructSharpWeb.Wasm;

/// <summary>Describes a browser-visible error without raw inputs or release-build stack traces.</summary>
public sealed class ErrorDetailsDto
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public long? Offset { get; set; }

    public string? Path { get; set; }
}
