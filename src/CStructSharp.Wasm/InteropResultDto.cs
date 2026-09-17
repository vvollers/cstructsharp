namespace CStructSharpWeb.Wasm;

using System.Collections.Generic;
using System.Text.Json;
using CStructSharp.Diagnostics;

/// <summary>
///     Describes the versioned result envelope returned by every browser operation. Since contract v7 a parse
///     operation's <see cref="Data"/> is the parsed value itself (a JSON object), not JSON text; successful parse
///     envelopes are written directly by <see cref="ParsedJsonWriter"/>, so this DTO is serialized only for
///     failures and the compiled-layout handshake.
/// </summary>
public sealed class InteropResultDto
{
    public int ContractVersion { get; set; }

    public JsonElement? Data { get; set; }

    public List<DebugDataDto> DebugData { get; set; } = [];

    public ErrorDetailsDto? Error { get; set; }

    public string Operation { get; set; } = string.Empty;

    public bool Success { get; set; }
}
