namespace CStructSharp.Fuzzing;

/// <summary>Defines the bounded public-operation limits shared by fuzz targets.</summary>
public sealed class FuzzLimits
{
    public int MaxDefinitionLength { get; init; }

    public int MaxLayoutNestingDepth { get; init; }

    public int MaxExpressionNestingDepth { get; init; }

    public int MaxExpressionTokens { get; init; }

    public int MaxArrayElements { get; init; }

    public long MaxStringBytes { get; init; }

    public long MaxTotalBytesRead { get; init; }

    public int MaxNestingDepth { get; init; }

    public int MaxPointerDepth { get; init; }

    public long MaxPointerTargetBytes { get; init; }

    public long MaxTotalBytesWritten { get; init; }
}
