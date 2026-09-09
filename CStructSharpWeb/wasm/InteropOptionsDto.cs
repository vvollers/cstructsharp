namespace CStructSharpWeb.Wasm;

/// <summary>Describes bounded browser choices shared by parse, serialize, and update operations.</summary>
public sealed class InteropOptionsDto
{
    public string? AddressingMode { get; set; }

    public bool? Aligned { get; set; }

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
