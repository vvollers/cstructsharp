namespace CStructSharp;

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
    long Origin)
{
    /// <summary>Copies every read choice before variable enumeration, stream access, or another caller callback.</summary>
    public static ReadOperationSettings SnapshotReadOptions(ReadOptions? options)
    {
        if (options is null)
        {
            return new ReadOperationSettings(
                PointerAddressingMode.Absolute,
                true,
                64,
                null,
                1_000_000,
                16 * 1024 * 1024,
                64 * 1024 * 1024,
                256,
                0);
        }

        return new ReadOperationSettings(
            options.AddressingMode,
            options.DereferencePointers,
            options.MaxPointerDepth,
            options.MaxPointerTargetBytes,
            options.MaxArrayElements,
            options.MaxStringBytes,
            options.MaxTotalBytesRead,
            options.MaxNestingDepth,
            options.Origin);
    }

    /// <summary>Maps already-snapshotted update traversal choices into the same read operation settings.</summary>
    public static ReadOperationSettings SnapshotTraversalOptions(UpdateOptions options)
    {
        return new ReadOperationSettings(
            options.AddressingMode,
            options.AllowPointerDereference,
            options.MaxTraversalPointerDepth,
            options.MaxTraversalPointerTargetBytes,
            options.MaxTraversalArrayElements,
            options.MaxTraversalStringBytes,
            options.MaxTraversalBytesRead,
            options.MaxTraversalNestingDepth,
            options.Origin);
    }
}
