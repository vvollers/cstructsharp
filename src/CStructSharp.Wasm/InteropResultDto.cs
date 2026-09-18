namespace CStructSharpWeb.Wasm;

using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CStructSharp.Diagnostics;

/// <summary>
///     The versioned result envelope every browser operation returns (contract v8, camelCase). A parse operation's
///     <see cref="Data"/> is the selected value itself (the struct, union, array, or scalar the root names); successful
///     parse envelopes are written directly by <see cref="ParsedJsonWriter"/>, so this DTO is serialized only for
///     failures and the compiled-layout handshake.
/// </summary>
public sealed class InteropResultDto
{
    [JsonPropertyName("contractVersion")]
    public int ContractVersion { get; set; }

    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>The root or path the operation selected, as the caller named it or as the bridge chose it.</summary>
    [JsonPropertyName("root")]
    public string? Root { get; set; }

    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    [JsonPropertyName("debug")]
    public List<DebugDataDto> Debug { get; set; } = [];

    [JsonPropertyName("error")]
    public ErrorDetailsDto? Error { get; set; }
}
