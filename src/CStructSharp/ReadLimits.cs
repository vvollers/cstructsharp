namespace CStructSharp;

using System.Threading;

/// <summary>
///     The limits of one read as a value: the same bounds as the matching <see cref="ReadOptions"/> members, without an
///     object to allocate. A reader that parses many small records builds one <see cref="ReadLimits"/> (or none, for the
///     defaults) instead of a <see cref="ReadOptions"/> per call.
/// </summary>
/// <remarks>
///     <para>
///         A value with nothing set, including <see langword="default"/>, has the same limits as a new
///         <see cref="ReadOptions"/>. Only the limits a generated <c>Parse</c> over bytes in memory needs are here: the
///         array, string, total-byte and nesting budgets, <see cref="TrimFixedText"/>, <see cref="InvalidText"/> and the
///         cancellation token. Pointer policy (<see cref="ReadOptions.DereferencePointers"/>, the depth and target limits,
///         the addressing mode and the origin) keeps its defaults; a read that needs another policy uses
///         <see cref="ReadOptions"/>.
///     </para>
///     <para>
///         Every limit means what it means on <see cref="ReadOptions"/>, and a generated reader enforces it exactly as it
///         enforces the same limit on <see cref="ReadOptions"/>: a read that reaches an unusable value (a negative limit, a
///         nesting depth below one) fails with <see cref="Diagnostics.CStructReadLimitException"/>, while a read that never
///         meets that limit (a negative array limit and a record without arrays) is not affected. Only the runtime entry
///         points (<c>CStruct.Parse</c> and the like) reject such a value up front with <see cref="System.ArgumentOutOfRangeException"/>.
///     </para>
///     <para>
///         A call that passes the <see langword="default"/> literal (<c>Parse(bytes, default)</c>) is ambiguous between the
///         <see cref="ReadOptions"/> and the <see cref="ReadLimits"/> overload; write <c>null</c> or <c>new ReadLimits()</c>.
///         Views and the stream and sequence readers take <see cref="ReadOptions"/>, because a view's <c>ToObject()</c> and a
///         stream read need the pointer policy and the buffering that only the options carry.
///     </para>
/// </remarks>
public readonly record struct ReadLimits
{
    /// <summary>The default array element limit, shared with <see cref="ReadOptions.MaxArrayElements"/>; each numeric limit is stored as its difference from its default, so the zeroed value is the default one.</summary>
    internal const int DefaultMaxArrayElements = 1_000_000;

    /// <summary>The default string byte limit, shared with <see cref="ReadOptions.MaxStringBytes"/>.</summary>
    internal const long DefaultMaxStringBytes = 16 * 1024 * 1024;

    /// <summary>The default total byte limit, shared with <see cref="ReadOptions.MaxTotalBytesRead"/>.</summary>
    internal const long DefaultMaxTotalBytesRead = 64 * 1024 * 1024;

    /// <summary>The default nesting depth limit, shared with <see cref="ReadOptions.MaxNestingDepth"/>.</summary>
    internal const int DefaultMaxNestingDepth = 256;

    private readonly int arrayElementsOffset;
    private readonly long stringBytesOffset;
    private readonly long totalBytesOffset;
    private readonly int nestingDepthOffset;
    private readonly bool lenientText;

    /// <summary>Gets the greatest number of elements a single traversed array field may contain; see <see cref="ReadOptions.MaxArrayElements"/>.</summary>
    public int MaxArrayElements
    {
        get => unchecked(this.arrayElementsOffset + DefaultMaxArrayElements);
        init => this.arrayElementsOffset = unchecked(value - DefaultMaxArrayElements);
    }

    /// <summary>Gets the greatest encoded-byte length of one terminated string including its terminator; see <see cref="ReadOptions.MaxStringBytes"/>.</summary>
    public long MaxStringBytes
    {
        get => unchecked(this.stringBytesOffset + DefaultMaxStringBytes);
        init => this.stringBytesOffset = unchecked(value - DefaultMaxStringBytes);
    }

    /// <summary>Gets the greatest total bytes one read may consume; see <see cref="ReadOptions.MaxTotalBytesRead"/>.</summary>
    public long MaxTotalBytesRead
    {
        get => unchecked(this.totalBytesOffset + DefaultMaxTotalBytesRead);
        init => this.totalBytesOffset = unchecked(value - DefaultMaxTotalBytesRead);
    }

    /// <summary>Gets the greatest active struct depth one read may reach; see <see cref="ReadOptions.MaxNestingDepth"/>.</summary>
    public int MaxNestingDepth
    {
        get => unchecked(this.nestingDepthOffset + DefaultMaxNestingDepth);
        init => this.nestingDepthOffset = unchecked(value - DefaultMaxNestingDepth);
    }

    /// <summary>Gets whether fixed-capacity text drops its trailing NUL padding; see <see cref="ReadOptions.TrimFixedText"/>.</summary>
    public bool TrimFixedText { get; init; }

    /// <summary>Gets what the read does with text that is not valid in its encoding; see <see cref="ReadOptions.InvalidText"/>.</summary>
    /// <exception cref="System.ArgumentOutOfRangeException">The value is not a member of <see cref="InvalidTextHandling"/>.</exception>
    public InvalidTextHandling InvalidText
    {
        get => this.lenientText ? InvalidTextHandling.Replace : InvalidTextHandling.Fail;
        init => this.lenientText = value switch
        {
            InvalidTextHandling.Fail => false,
            InvalidTextHandling.Replace => true,
            _ => throw new System.ArgumentOutOfRangeException(nameof(value), value, "Not a member of InvalidTextHandling."),
        };
    }

    /// <summary>Gets the token a long read observes; see <see cref="ReadOptions.CancellationToken"/>.</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>Gets whether invalid text is replaced instead of failing, as the readers test it.</summary>
    internal bool LenientText => this.lenientText;

    /// <summary>Copies the limits of a <see cref="ReadOptions"/>, dropping its pointer policy.</summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The limits.</returns>
    public static ReadLimits From(ReadOptions? options)
    {
        if (options is null)
        {
            return default;
        }

        return new ReadLimits
        {
            MaxArrayElements = options.MaxArrayElements,
            MaxStringBytes = options.MaxStringBytes,
            MaxTotalBytesRead = options.MaxTotalBytesRead,
            MaxNestingDepth = options.MaxNestingDepth,
            TrimFixedText = options.TrimFixedText,
            InvalidText = options.InvalidText,
            CancellationToken = options.CancellationToken,
        };
    }

    /// <summary>Builds the equivalent <see cref="ReadOptions"/>, with the default pointer policy.</summary>
    /// <returns>New options with these limits.</returns>
    public ReadOptions ToOptions() => new()
    {
        MaxArrayElements = this.MaxArrayElements,
        MaxStringBytes = this.MaxStringBytes,
        MaxTotalBytesRead = this.MaxTotalBytesRead,
        MaxNestingDepth = this.MaxNestingDepth,
        TrimFixedText = this.TrimFixedText,
        InvalidText = this.InvalidText,
        CancellationToken = this.CancellationToken,
    };
}
