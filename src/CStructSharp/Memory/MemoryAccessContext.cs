namespace CStructSharp.Memory;

/// <summary>The budget for one logical operation: byte, request, nesting, and pointer limits plus a cancellation token. Not safe for concurrent use.</summary>
/// <remarks>
/// <para>
/// Code that reads untrusted bytes must assume the worst: a corrupt list that loops forever, a pointer chain that
/// never ends, a mapping table that sends every request through ten layers. A budget turns those into a bounded
/// failure instead of a hang. The library passes one context through every layer that participates in an
/// operation (session, mapping adapters, walkers, callbacks), so splitting work across objects never resets the
/// limits. Create a fresh instance for each independent operation.
/// </para>
/// <para>
/// Set limits with an object initializer, for example <c>new MemoryAccessContext { MaxTotalBytes = 4096 }</c>;
/// unset limits keep their defaults (64 MiB, 100 000 requests, nesting depth 256, pointer depth 64, the same
/// nesting and pointer defaults as <see cref="ReadOptions"/>). Every limit must be positive: the init accessor
/// throws <see cref="ArgumentOutOfRangeException"/> for zero or a negative value.
/// </para>
/// <para>
/// The counters measure library work, not unique addresses or hardware I/O. Re-reading a byte charges it again;
/// validation and staging in a patch charge the same bytes several times. <see cref="Requests"/> counts zero-byte
/// work too (a mapping lookup, a path step, a traversal step), which prevents a loop of individually cheap
/// operations from running unbounded. <see cref="MaxNestingDepth"/> separately limits nested values and stacked
/// source layers, and <see cref="MaxPointerDepth"/> limits the pointer steps one path follows. Cancellation is
/// cooperative: it is checked before each request and cannot interrupt a source that blocks inside a transport call.
/// </para>
/// </remarks>
public sealed class MemoryAccessContext
{
    private readonly long maxTotalBytes = 64 * 1024 * 1024;
    private readonly int maxRequests = 100_000;
    private readonly int maxNestingDepth = 256;
    private readonly int maxPointerDepth = 64;
    private int sourceDepth;

    /// <summary>Creates a budget with the default limits and no cancellation; set properties in an object initializer to change them.</summary>
    public MemoryAccessContext()
    {
    }

    /// <summary>
    ///     Gets the byte limit (default 64 MiB): bytes requested from leaf sources plus bytes staged for output,
    ///     counting repeated reads of the same address.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value set is zero or negative.</exception>
    public long MaxTotalBytes
    {
        get => this.maxTotalBytes;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            this.maxTotalBytes = value;
        }
    }

    /// <summary>
    ///     Gets the request limit (default 100 000): every charged unit of work counts, including zero-byte work
    ///     such as mapping lookups, path steps, and traversal steps.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value set is zero or negative.</exception>
    public int MaxRequests
    {
        get => this.maxRequests;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            this.maxRequests = value;
        }
    }

    /// <summary>
    ///     Gets the greatest nesting depth (default 256, as <see cref="ReadOptions.MaxNestingDepth"/>) of composite
    ///     values being read, serialized, or flattened, and of source layers stacked on one another.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value set is zero or negative.</exception>
    public int MaxNestingDepth
    {
        get => this.maxNestingDepth;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            this.maxNestingDepth = value;
        }
    }

    /// <summary>
    ///     Gets the greatest number of pointer <c>.value</c> steps (default 64, as
    ///     <see cref="ReadOptions.MaxPointerDepth"/>) that one operation's path may follow.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value set is zero or negative.</exception>
    public int MaxPointerDepth
    {
        get => this.maxPointerDepth;
        init
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            this.maxPointerDepth = value;
        }
    }

    /// <summary>Gets the cancellation token that every layer observes before each charged request.</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>Gets the bytes charged so far, including work attempted before a failure.</summary>
    public long BytesRequested { get; private set; }

    /// <summary>Gets the requests charged so far.</summary>
    public int Requests { get; private set; }

    /// <summary>Charges one request and <paramref name="bytes"/> bytes, throwing instead when the charge would exceed a limit.</summary>
    /// <remarks>Call this before doing the work, so exceeding a limit prevents the next request rather than being
    /// noticed afterwards. A successful charge stays counted even if the subsequent source operation fails. Leaf
    /// sources pass the requested byte count; translation-only adapters pass zero so bytes are not counted once per
    /// layer. Cancellation is checked first and has its own exception type.</remarks>
    /// <exception cref="OperationCanceledException">The caller has requested cancellation.</exception>
    /// <exception cref="MemoryAccessException">The charge would exceed <see cref="MaxTotalBytes"/> or <see cref="MaxRequests"/>; the failure is <see cref="MemoryFailure.BudgetExceeded"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bytes"/> is negative.</exception>
    /// <param name="sourceId">Label of the source charging the work, reported if the budget is exceeded; null for work not tied to a source, such as staging serialized output.</param>
    /// <param name="address">Address of the work being charged, reported if the budget is exceeded; null for work not tied to an address.</param>
    /// <param name="bytes">Bytes about to be read from a leaf source or staged as output; zero for translation-only work.</param>
    public void Charge(string? sourceId, ulong? address, int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        this.CancellationToken.ThrowIfCancellationRequested();
        if (this.Requests >= this.MaxRequests || bytes > this.MaxTotalBytes - this.BytesRequested)
        {
            throw new MemoryAccessException(MemoryFailure.BudgetExceeded, sourceId, address, bytes, "Memory operation budget exceeded.");
        }

        this.Requests++;
        this.BytesRequested += bytes;
    }

    /// <summary>Checks cancellation and rejects a nesting depth beyond <see cref="MaxNestingDepth"/>, without charging a request.</summary>
    /// <param name="depth">Current nesting depth of the composite value or source layer.</param>
    /// <exception cref="OperationCanceledException">The caller has requested cancellation.</exception>
    /// <exception cref="MemoryAccessException">The depth exceeds <see cref="MaxNestingDepth"/>; the failure is <see cref="MemoryFailure.BudgetExceeded"/> and has no source coordinates.</exception>
    internal void CheckNestingDepth(int depth)
    {
        this.CancellationToken.ThrowIfCancellationRequested();
        if (depth > this.MaxNestingDepth)
        {
            // A depth limit belongs to the traversal, not to any one source address, so the coordinates stay unknown.
            throw new MemoryAccessException(MemoryFailure.BudgetExceeded, null, null, 0, "Memory nesting depth exceeded.");
        }
    }

    /// <summary>Checks cancellation and rejects a pointer step count beyond <see cref="MaxPointerDepth"/>, without charging a request.</summary>
    /// <param name="depth">Number of pointer <c>.value</c> steps the current path has followed, including the one about to be taken.</param>
    /// <exception cref="OperationCanceledException">The caller has requested cancellation.</exception>
    /// <exception cref="MemoryAccessException">The count exceeds <see cref="MaxPointerDepth"/>; the failure is <see cref="MemoryFailure.BudgetExceeded"/> and has no source coordinates.</exception>
    internal void CheckPointerDepth(int depth)
    {
        this.CancellationToken.ThrowIfCancellationRequested();
        if (depth > this.MaxPointerDepth)
        {
            // Like nesting, the pointer limit belongs to the path rather than to one source address.
            throw new MemoryAccessException(MemoryFailure.BudgetExceeded, null, null, 0, "Memory pointer depth exceeded.");
        }
    }

    /// <summary>Records entry into a nested source layer so a chain of mappings cannot recurse without limit.</summary>
    /// <exception cref="MemoryAccessException">Entering would exceed <see cref="MaxNestingDepth"/>; the failure is <see cref="MemoryFailure.BudgetExceeded"/>.</exception>
    internal void EnterSource()
    {
        this.CheckNestingDepth(this.sourceDepth + 1);
        this.sourceDepth++;
    }

    /// <summary>Leaves a nested source layer; called from a finally block so failed reads also unwind the depth.</summary>
    internal void ExitSource() => this.sourceDepth--;
}
