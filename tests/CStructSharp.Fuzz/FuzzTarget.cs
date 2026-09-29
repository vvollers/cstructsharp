namespace CStructSharp.Fuzzing;

/// <summary>Pairs one target action with its intentionally documented failure predicate.</summary>
/// <param name="Name">The target name, one of <see cref="FuzzTargets.Names"/>.</param>
/// <param name="Execute">
///     Runs the target on one input and appends what it produced to the rendering (<see cref="CanonicalText"/>), which the
///     replay digest hashes; a rejected input throws instead.
/// </param>
/// <param name="IsDocumentedFailure">Whether an exception the target threw is an expected rejection of the input.</param>
internal sealed record FuzzTarget(
    string Name,
    Action<byte[], CanonicalText> Execute,
    Func<Exception, bool> IsDocumentedFailure);
