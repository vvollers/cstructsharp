namespace CStructSharpWeb.Wasm;

using System.Collections.Generic;
using System.Text.Json.Serialization;

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

/// <summary>Describes a browser-visible error without raw inputs or release-build stack traces.</summary>
public sealed class ErrorDetailsDto
{
    public string Code { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public long? Offset { get; set; }

    public string? Path { get; set; }
}

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

/// <summary>Describes bounded browser choices shared by parse, serialize, and update operations.</summary>
public sealed class InteropOptionsDto
{
    public string? AddressingMode { get; set; }

    public bool? Aligned { get; set; }

    public bool? AllowPointerDereference { get; set; }

    public string? BindingMode { get; set; }

    public bool? ClearUnionStorage { get; set; }

    public bool? DereferencePointers { get; set; }

    public bool? LittleEndian { get; set; }

    public int? MaxArrayElements { get; set; }

    public int? MaxDefinitionLength { get; set; }

    public int? MaxExpressionNestingDepth { get; set; }

    public int? MaxExpressionTokens { get; set; }

    public int? MaxLayoutNestingDepth { get; set; }

    public int? MaxNestingDepth { get; set; }

    public int? MaxPointerDepth { get; set; }

    public long? MaxPointerTargetBytes { get; set; }

    public long? MaxStringBytes { get; set; }

    public long? MaxTotalBytesRead { get; set; }

    public long? MaxTotalBytesWritten { get; set; }

    public long? MaxTraversalBytesRead { get; set; }

    public int? MaxTraversalNestingDepth { get; set; }

    public int? MaxTraversalPointerDepth { get; set; }

    public long? MaxTraversalPointerTargetBytes { get; set; }

    public long? MaxTraversalStringBytes { get; set; }

    public string? Origin { get; set; }

    public int? PointerSize { get; set; }

    public bool? RequireExistingPointerTarget { get; set; }

    public string? RootTypeName { get; set; }
}

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
