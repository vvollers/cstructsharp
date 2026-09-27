namespace CStructSharp.Reading;

/// <summary>Stores the immutable read settings copied at an operation boundary without a heap allocation.</summary>
internal readonly record struct ReadOperationSettings(
    PointerAddressingMode AddressingMode,
    bool DereferencePointers,
    int MaxPointerDepth,
    long? MaxPointerTargetBytes,
    int MaxArrayElements,
    long MaxStringBytes,
    long MaxTotalBytesRead,
    int MaxNestingDepth,
    long Origin,
    bool TrimFixedText = false,
    System.Threading.CancellationToken CancellationToken = default,
    ExecutionPath ExecutionPath = ExecutionPath.Fastest)
{
    /// <summary>Copies every read choice before variable enumeration, stream access, or another caller callback.</summary>
    public static ReadOperationSettings SnapshotReadOptions(ReadOptions? options)
    {
        options ??= ReadOptions.Default;
        return new ReadOperationSettings(
            options.AddressingMode,
            options.DereferencePointers,
            options.MaxPointerDepth,
            options.MaxPointerTargetBytes,
            options.MaxArrayElements,
            options.MaxStringBytes,
            options.MaxTotalBytesRead,
            options.MaxNestingDepth,
            options.Origin,
            options.TrimFixedText,
            options.CancellationToken,
            options.ExecutionPath);
    }

    /// <summary>Maps already-snapshotted update traversal choices into the same read operation settings.</summary>
    public static ReadOperationSettings SnapshotTraversalOptions(UpdateOptions options)
    {
        return new ReadOperationSettings(
            options.AddressingMode,
            options.DereferencePointers,
            options.MaxTraversalPointerDepth,
            options.MaxTraversalPointerTargetBytes,
            options.MaxTraversalArrayElements,
            options.MaxTraversalStringBytes,
            options.MaxTraversalBytesRead,
            options.MaxTraversalNestingDepth,
            options.Origin,
            CancellationToken: options.CancellationToken,
            ExecutionPath: options.ExecutionPath);
    }
}
