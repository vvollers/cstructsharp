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
    /// <summary>The wire contract version the envelope follows; always 8.</summary>
    [JsonPropertyName("contractVersion")]
    public int ContractVersion { get; set; }

    /// <summary>
    ///     The operation that produced the envelope: <c>parse</c>, <c>resolveAddress</c>, or <c>compile</c> (the
    ///     compiled-layout handshake).
    /// </summary>
    [JsonPropertyName("operation")]
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    ///     Whether the operation succeeded: <see cref="Data"/> is set and <see cref="Error"/> is null on success,
    ///     and the reverse on failure.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>The root or path the operation selected, as the caller named it or as the bridge chose it.</summary>
    [JsonPropertyName("root")]
    public string? Root { get; set; }

    /// <summary>
    ///     The operation's result on success: an empty object for the compiled-layout handshake, or the resolved
    ///     byte position for <c>resolveAddress</c>. Null on failure.
    /// </summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; set; }

    /// <summary>The byte ranges a debug parse recorded; empty for every other operation and on failure.</summary>
    [JsonPropertyName("debug")]
    public List<DebugDataDto> Debug { get; set; } = [];

    /// <summary>The failure details when <see cref="Success"/> is false; null on success.</summary>
    [JsonPropertyName("error")]
    public ErrorDetailsDto? Error { get; set; }
}
