namespace CStructSharpWeb.Wasm;

using System.Text.Json.Serialization;

/// <summary>
///     The one options object every browser operation accepts (contract v8, camelCase on the wire). Compile-time
///     choices (placement, byte order, pointer width, bitfield rules, compiler limits) and per-operation choices
///     (root, budgets, pointer policy, diagnostics) travel together; the adapter merges what the caller passed to
///     <c>compile</c> with what it passed to the operation.
/// </summary>
public sealed class InteropOptionsDto
{
    [JsonPropertyName("addressingMode")]
    public string? AddressingMode { get; set; }

    [JsonPropertyName("aligned")]
    public bool? Aligned { get; set; }

    [JsonPropertyName("bitfieldAllocation")]
    public string? BitfieldAllocation { get; set; }

    [JsonPropertyName("bitfieldPacking")]
    public string? BitfieldPacking { get; set; }

    [JsonPropertyName("clearUnionStorage")]
    public bool? ClearUnionStorage { get; set; }

    [JsonPropertyName("cLongWidth")]
    public int? CLongWidth { get; set; }

    [JsonPropertyName("dereferencePointers")]
    public bool? DereferencePointers { get; set; }

    [JsonPropertyName("littleEndian")]
    public bool? LittleEndian { get; set; }

    [JsonPropertyName("maxArrayElements")]
    public int? MaxArrayElements { get; set; }

    [JsonPropertyName("maxDefinitionLength")]
    public int? MaxDefinitionLength { get; set; }

    [JsonPropertyName("maxExpressionNestingDepth")]
    public int? MaxExpressionNestingDepth { get; set; }

    [JsonPropertyName("maxExpressionTokens")]
    public int? MaxExpressionTokens { get; set; }

    [JsonPropertyName("maxLayoutNestingDepth")]
    public int? MaxLayoutNestingDepth { get; set; }

    [JsonPropertyName("maxNestingDepth")]
    public int? MaxNestingDepth { get; set; }

    [JsonPropertyName("maxPointerDepth")]
    public int? MaxPointerDepth { get; set; }

    [JsonPropertyName("maxPointerTargetBytes")]
    public long? MaxPointerTargetBytes { get; set; }

    [JsonPropertyName("maxStringBytes")]
    public long? MaxStringBytes { get; set; }

    [JsonPropertyName("maxTotalBytesRead")]
    public long? MaxTotalBytesRead { get; set; }

    [JsonPropertyName("maxTotalBytesWritten")]
    public long? MaxTotalBytesWritten { get; set; }

    [JsonPropertyName("maxTraversalBytesRead")]
    public long? MaxTraversalBytesRead { get; set; }

    [JsonPropertyName("maxTraversalNestingDepth")]
    public int? MaxTraversalNestingDepth { get; set; }

    [JsonPropertyName("maxTraversalPointerDepth")]
    public int? MaxTraversalPointerDepth { get; set; }

    [JsonPropertyName("maxTraversalPointerTargetBytes")]
    public long? MaxTraversalPointerTargetBytes { get; set; }

    [JsonPropertyName("maxTraversalStringBytes")]
    public long? MaxTraversalStringBytes { get; set; }

    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    [JsonPropertyName("pointerSize")]
    public int? PointerSize { get; set; }

    /// <summary>Whether error details keep only the curated category text (no library message, path, or member).</summary>
    [JsonPropertyName("redactDiagnostics")]
    public bool? RedactDiagnostics { get; set; }

    [JsonPropertyName("requireExistingPointerTarget")]
    public bool? RequireExistingPointerTarget { get; set; }

    /// <summary>The root name or nested path an operation selects; the first declared struct when omitted.</summary>
    [JsonPropertyName("root")]
    public string? Root { get; set; }

    [JsonPropertyName("trimFixedText")]
    public bool? TrimFixedText { get; set; }

    [JsonPropertyName("unknownMembers")]
    public string? UnknownMembers { get; set; }
}
