namespace CStructSharp.Memory.Metadata;

using CStructSharp.Diagnostics;

/// <summary>Bounds aggregate preparation work, including repeated references to shared native field lists.</summary>
internal sealed class PdbExportBudget
{
    private readonly CancellationToken cancellationToken;
    private long remaining;

    /// <summary>Creates operation-local state so concurrent exports never share counters or cancellation.</summary>
    /// <param name="maximum">Maximum type, member, descriptor and symbol traversal steps.</param>
    /// <param name="cancellationToken">Cancellation for CPU work as well as output traversal.</param>
    internal PdbExportBudget(long maximum, CancellationToken cancellationToken)
    {
        this.remaining = maximum;
        this.cancellationToken = cancellationToken;
    }

    /// <summary>Charges before traversing another node, including aliases that do not allocate a new identity.</summary>
    /// <exception cref="CStructLayoutException">The shared operation budget is exhausted.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    internal void Take()
    {
        this.cancellationToken.ThrowIfCancellationRequested();
        if (this.remaining == 0)
        {
            throw new CStructLayoutException("Native PDB preparation exceeds its shared traversal budget.");
        }

        this.remaining--;
    }
}
