namespace CStructSharp.Fuzzing;

/// <summary>Pairs one target action with its intentionally documented failure predicate.</summary>
internal sealed record FuzzTarget(
    string Name,
    Action<byte[]> Execute,
    Func<Exception, bool> IsDocumentedFailure);
