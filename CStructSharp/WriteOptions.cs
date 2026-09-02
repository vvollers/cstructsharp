namespace CStructSharp;

/// <summary>Lists which public members are allowed when writing ordinary .NET objects.</summary>
public enum PocoBindingMode
{
    /// <summary>Any public readable property or public field may supply a layout value.</summary>
    PublicReadable,

    /// <summary>Only public read/write properties and public fields may supply a layout value.</summary>
    PublicReadWrite,
}

/// <summary>Controls serialization and stream-writing operations performed by <see cref="CStruct"/>.</summary>
/// <remarks>
///     Every operation snapshots these values before writing. Budgets are per public operation. Stream operations
///     use the stream's current position as their output origin; caller-owned memory uses coordinate zero.
/// </remarks>
public class WriteOptions
{
    /// <summary>Creates the default bounded write and object-binding policy.</summary>
    public WriteOptions()
    {
    }

    /// <summary>Gets whether written pointer values are absolute stream positions or offsets from <see cref="Origin"/>.</summary>
    public PointerAddressingMode AddressingMode { get; init; } = PointerAddressingMode.Absolute;

    /// <summary>Gets which readable .NET properties can supply layout field values.</summary>
    public PocoBindingMode BindingMode { get; init; } = PocoBindingMode.PublicReadable;

    /// <summary>Gets the greatest number of elements one array field may write.</summary>
    public int MaxArrayElements { get; init; } = 1_000_000;

    /// <summary>
    ///     Gets the greatest encoded-byte length one string field may write, including fixed-buffer padding or
    ///     a terminated string's complete terminator.
    /// </summary>
    public long MaxStringBytes { get; init; } = 16 * 1024 * 1024;

    /// <summary>
    ///     Gets the greatest total number of bytes one operation may physically submit to its stream.
    ///     Rewrites of shared storage count again, and extending a seekable stream across a gap is charged by extent.
    /// </summary>
    public long MaxTotalBytesWritten { get; init; } = 64 * 1024 * 1024;

    /// <summary>Gets the greatest active struct or union depth one write operation may enter.</summary>
    public int MaxNestingDepth { get; init; } = 256;

    /// <summary>
    ///     Gets the base position subtracted, with checked arithmetic, from relative pointer values before
    ///     they are written. The resulting non-null offset must be positive and fit the configured pointer width.
    ///     Null address zero is stored directly and does not use this origin.
    /// </summary>
    public long Origin { get; init; }
}
