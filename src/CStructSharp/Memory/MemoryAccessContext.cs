namespace CStructSharp.Memory;

/// <summary>The budget for one logical operation: byte, request, and depth limits plus a cancellation token. Not safe for concurrent use.</summary>
/// <remarks>
/// <para>
/// Code that reads untrusted bytes must assume the worst: a corrupt list that loops forever, a pointer chain that
/// never ends, a mapping table that sends every request through ten layers. A budget turns those into a bounded
/// failure instead of a hang. The library passes one context through every layer that participates in an
/// operation (session, mapping adapters, walkers, callbacks), so splitting work across objects never resets the
/// limits. Create a fresh instance for each independent operation.
/// </para>
/// <para>
/// The counters measure library work, not unique addresses or hardware I/O. Re-reading a byte charges it again;
/// validation and staging in a patch charge the same bytes several times. <see cref="Requests"/> counts zero-byte
/// work too (a mapping lookup, a path step, a traversal step), which prevents a loop of individually cheap
/// operations from running unbounded. <see cref="MaxDepth"/> separately limits nested values, pointer steps, and
/// stacked source layers. Cancellation is cooperative: it is checked before each request and cannot interrupt a
/// source that blocks inside a transport call.
/// </para>
/// </remarks>
public sealed class MemoryAccessContext
{
    private int sourceDepth;

    /// <summary>Creates a budget. All limits must be positive; cancellation is checked before each charged request.</summary>
    /// <param name="maxBytes">Maximum bytes requested from leaf sources plus bytes staged for output, counting repeats.</param>
    /// <param name="maxRequests">Maximum number of charged work units: leaf reads, mapping lookups, path and traversal steps.</param>
    /// <param name="maxDepth">Maximum nesting of composite values, pointer steps, and stacked source layers.</param>
    /// <param name="cancellationToken">Token observed before each request; cancellation throws <see cref="OperationCanceledException"/>.</param>
    public MemoryAccessContext(long maxBytes = 64 * 1024 * 1024, int maxRequests = 100_000, int maxDepth = 128, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRequests);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDepth);
        this.MaxBytes = maxBytes;
        this.MaxRequests = maxRequests;
        this.MaxDepth = maxDepth;
        this.CancellationToken = cancellationToken;
    }

    /// <summary>Gets the byte limit; charged bytes include repeated reads of the same address and staged output.</summary>
    public long MaxBytes { get; }

    /// <summary>Gets the request limit; every charged unit of work counts, including zero-byte lookups.</summary>
    public int MaxRequests { get; }

    /// <summary>Gets the maximum nesting depth for composite values, pointer path steps, and source layers.</summary>
    public int MaxDepth { get; }

    /// <summary>Gets the cancellation token that every layer observes.</summary>
    public CancellationToken CancellationToken { get; }

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
    /// <exception cref="MemoryAccessException">The charge would exceed the byte or request limit; the failure is <see cref="MemoryFailure.BudgetExceeded"/>.</exception>
    /// <param name="sourceId">Label of the source charging the work, reported if the budget is exceeded.</param>
    /// <param name="address">Address of the work being charged, reported if the budget is exceeded.</param>
    /// <param name="bytes">Bytes about to be read from a leaf source or staged as output; zero for translation-only work.</param>
    public void Charge(string sourceId, ulong address, int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        this.CancellationToken.ThrowIfCancellationRequested();
        if (this.Requests >= this.MaxRequests || bytes > this.MaxBytes - this.BytesRequested)
        {
            throw new MemoryAccessException(MemoryFailure.BudgetExceeded, sourceId, address, bytes, "Memory operation budget exceeded.");
        }

        this.Requests++;
        this.BytesRequested += bytes;
    }

    /// <summary>Checks cancellation and rejects a nesting depth beyond <see cref="MaxDepth"/>, without charging a request.</summary>
    /// <param name="depth">Current nesting depth of the value, path step, or source layer.</param>
    internal void CheckDepth(int depth)
    {
        this.CancellationToken.ThrowIfCancellationRequested();
        if (depth > this.MaxDepth)
        {
            throw new MemoryAccessException(MemoryFailure.BudgetExceeded, "layout", 0, 0, "Memory traversal depth exceeded.");
        }
    }

    /// <summary>Records entry into a nested source layer so a chain of mappings cannot recurse without limit.</summary>
    internal void EnterSource()
    {
        this.CheckDepth(this.sourceDepth + 1);
        this.sourceDepth++;
    }

    /// <summary>Leaves a nested source layer; called from a finally block so failed reads also unwind the depth.</summary>
    internal void ExitSource() => this.sourceDepth--;
}
