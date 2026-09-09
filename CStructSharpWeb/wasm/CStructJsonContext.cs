namespace CStructSharpWeb.Wasm;

using System.Collections.Generic;
using System.Text.Json.Serialization;

/// <summary>Describes the JSON types used by the browser bridge without runtime reflection.</summary>
[JsonSerializable(typeof(InteropResultDto))]
[JsonSerializable(typeof(List<DebugDataDto>))]
[JsonSerializable(typeof(DebugDataDto))]
[JsonSerializable(typeof(ErrorDetailsDto))]
[JsonSerializable(typeof(InteropOptionsDto))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
public partial class CStructJsonContext : JsonSerializerContext
{
}
