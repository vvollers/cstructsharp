namespace CStructSharp.Memory;

/// <summary>Confirmed copied prefix and the reason an exact request stopped.</summary>
/// <param name="BytesRead">Successfully copied bytes; the destination suffix is untouched.</param>
/// <param name="Failure">Null on complete input, otherwise the source failure at the first unavailable byte. Cancellation and budgets throw.</param>
public readonly record struct MemoryReadResult(int BytesRead, MemoryReadFailure? Failure)
{
    /// <summary>Gets whether every requested byte was copied.</summary>
    public bool IsComplete => this.Failure is null;
}
