namespace CStructSharp;

/// <summary>
/// Adds bounded read-side traversal and in-place replacement choices to the normal write options. Updates validate
/// against sparse staging before commit and cannot extend the existing destination stream.
/// </summary>
/// <remarks>
///     Update operations snapshot both these traversal settings and the inherited <see cref="WriteOptions"/> values.
///     Library-detectable validation failures occur before destination commit and preserve the caller's stream position.
/// </remarks>
public sealed class UpdateOptions : WriteOptions
{
    /// <summary>Creates the default bounded update, traversal, and replacement policy.</summary>
    public UpdateOptions()
    {
    }

    /// <summary>Gets whether an update path may pass through a pointer's <c>.value</c> target.</summary>
    public bool AllowPointerDereference { get; init; } = true;

    /// <summary>
    ///     Gets whether an update through <c>.value</c> requires a non-null pointer target.
    /// </summary>
    public bool RequireExistingPointerTarget { get; init; } = true;

    /// <summary>
    ///     Gets whether an updated union is cleared before its selected member is written.
    /// </summary>
    public bool ClearUnionStorage { get; init; } = true;

    /// <summary>Gets the greatest nested pointer depth that update-path traversal may follow.</summary>
    public int MaxTraversalPointerDepth { get; init; } = 64;

    /// <summary>
    ///     Gets the greatest fixed-size target, in bytes, that update-path traversal may reach through one
    ///     pointer. Variable-length targets are rejected when a value is configured.
    /// </summary>
    public long? MaxTraversalPointerTargetBytes { get; init; }

    /// <summary>
    ///     Gets the greatest encoded-byte length update-path traversal may scan in one terminated string,
    ///     including its complete terminator.
    /// </summary>
    public long MaxTraversalStringBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>Gets the greatest total number of bytes update-path traversal may physically read.</summary>
    public long MaxTraversalBytesRead { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the greatest active struct depth update-path traversal may enter.</summary>
    public int MaxTraversalNestingDepth { get; init; } = 256;
}
