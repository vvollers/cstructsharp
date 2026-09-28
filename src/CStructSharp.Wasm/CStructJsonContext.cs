namespace CStructSharpWeb.Wasm;

using System.Text.Json.Serialization;

/// <summary>
///     Describes the browser options object to System.Text.Json without runtime reflection. The bridge uses it only to
///     read input; every result is written by <see cref="InteropJsonWriter"/>.
/// </summary>
[JsonSerializable(typeof(InteropOptionsDto))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = false)]
public partial class CStructJsonContext : JsonSerializerContext
{
}
