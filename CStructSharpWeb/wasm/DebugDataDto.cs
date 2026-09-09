namespace CStructSharpWeb.Wasm;

/// <summary>Describes one field read for the browser debug panel.</summary>
public sealed class DebugDataDto
{
    public string? Buffer { get; set; }

    public long CurPos { get; set; }

    public string DebugStackString { get; set; } = string.Empty;

    public long EndPos { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Value { get; set; }
}
