namespace CStructSharpWeb.Wasm;

using System.Collections.Generic;

/// <summary>Describes the versioned result envelope returned by every browser operation.</summary>
public sealed class InteropResultDto
{
    public int ContractVersion { get; set; }

    public string? Data { get; set; }

    public List<DebugDataDto> DebugData { get; set; } = [];

    public ErrorDetailsDto? Error { get; set; }

    public string Operation { get; set; } = string.Empty;

    public bool Success { get; set; }
}
