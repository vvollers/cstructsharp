namespace CStructSharp.Memory;

/// <summary>One immutable typed field requirement and a caller-owned predicate, evaluated over captured bytes.</summary>
/// <remarks>Compose probes in application order. No callback is reordered, skipped or converted into statistical confidence. Predicates must themselves be safe for concurrent reuse when callers share a probe.</remarks>
/// <typeparam name="T">Exact managed field type.</typeparam>
public sealed class MemoryFieldProbe<T>
{
    private readonly Func<T, bool> predicate;

    /// <summary>Prepares a named requirement without retaining bytes or operation state.</summary>
    /// <param name="ruleId">Stable application rule identifier for retained evidence.</param>
    /// <param name="reader">Schema-bound fixed field reader.</param>
    /// <param name="predicate">Application predicate; invoked only after every storage byte is known. Exceptions propagate.</param>
    public MemoryFieldProbe(string ruleId, MemoryFieldReader<T> reader, Func<T, bool> predicate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ruleId);
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(predicate);
        if (reader.Selection.Offset is null)
        {
            throw new ArgumentException("A local probe requires a fixed field offset.", nameof(reader));
        }

        this.RuleId = ruleId;
        this.Reader = reader;
        this.predicate = predicate;
    }

    /// <summary>Gets the identifier connecting fast checks to retained application evidence.</summary>
    public string RuleId { get; }

    /// <summary>Gets the prepared reader, including required field path and byte extent.</summary>
    public MemoryFieldReader<T> Reader { get; }

    /// <summary>Evaluates exactly this rule, distinguishing unknown storage from rejection.</summary>
    /// <param name="bytes">Stable bytes beginning at the root coordinate.</param>
    /// <param name="context">Shared local work budget and cancellation.</param>
    /// <param name="validity">Optional known-byte intervals of this buffer.</param>
    /// <returns>Local match, mismatch or unavailable evidence.</returns>
    public MemoryProbeOutcome Evaluate(ReadOnlySpan<byte> bytes, MemoryAccessContext context, MemoryValidity? validity = null)
    {
        if (!this.Reader.TryReadBuffer(bytes, context, out T value, validity))
        {
            return MemoryProbeOutcome.Unavailable;
        }

        bool matches = this.predicate(value);
        context.CancellationToken.ThrowIfCancellationRequested();
        return matches ? MemoryProbeOutcome.Match : MemoryProbeOutcome.Mismatch;
    }
}
