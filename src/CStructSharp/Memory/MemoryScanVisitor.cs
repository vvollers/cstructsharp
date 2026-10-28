namespace CStructSharp.Memory;

/// <summary>Processes borrowed known bytes synchronously, retaining only owned results.</summary>
/// <param name="window">Borrowed bytes and the interval owning candidate starts.</param>
/// <param name="context">The same operation budget for CPU checks, pointer follows and transport.</param>
/// <returns>True to continue, false to stop with VisitorStopped. Exceptions, cancellation and exhausted budgets propagate.</returns>
public delegate bool MemoryScanVisitor(MemoryScanWindow window, MemoryAccessContext context);
