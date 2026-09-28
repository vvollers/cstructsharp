namespace CStructSharp.Fuzzing;

/// <summary>Defines the bounded public-operation limits shared by fuzz targets.</summary>
public sealed class FuzzLimits
{
    /// <summary>Gets the longest definition, in characters, that the fuzzed compiler accepts.</summary>
    public int MaxDefinitionLength { get; init; }

    /// <summary>Gets the deepest brace nesting the fuzzed compiler accepts in a definition.</summary>
    public int MaxLayoutNestingDepth { get; init; }

    /// <summary>Gets the deepest syntax-tree or identifier-dependency nesting accepted for one expression.</summary>
    public int MaxExpressionNestingDepth { get; init; }

    /// <summary>Gets the most expression nodes one evaluation session may compile or execute.</summary>
    public int MaxExpressionTokens { get; init; }

    /// <summary>Gets the most elements one array may hold when a fuzz target reads or writes it.</summary>
    public int MaxArrayElements { get; init; }

    /// <summary>Gets the most bytes one string may occupy when a fuzz target reads or writes it.</summary>
    public long MaxStringBytes { get; init; }

    /// <summary>Gets the most bytes one read operation may consume across all of its fields.</summary>
    public long MaxTotalBytesRead { get; init; }

    /// <summary>Gets the deepest value nesting a read or write may traverse.</summary>
    public int MaxNestingDepth { get; init; }

    /// <summary>Gets the longest chain of pointers a read may follow.</summary>
    public int MaxPointerDepth { get; init; }

    /// <summary>Gets the most bytes a read may consume at pointer targets.</summary>
    public long MaxPointerTargetBytes { get; init; }

    /// <summary>Gets the most bytes one write operation may produce.</summary>
    public long MaxTotalBytesWritten { get; init; }
}
