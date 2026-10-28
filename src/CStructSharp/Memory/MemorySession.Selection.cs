namespace CStructSharp.Memory;

/// <summary>Prepared and non-throwing selection operations.</summary>
public sealed partial class MemorySession
{
    /// <summary>Resolves a path while returning expected schema and memory failures as structured data.</summary>
    /// <param name="region">Finite root storage.</param>
    /// <param name="typeId">Root schema identity.</param>
    /// <param name="path">Relative member/index/pointer path.</param>
    /// <param name="selection">Selected storage when true.</param>
    /// <param name="failure">Structured outcome when false.</param>
    /// <param name="context">Shared operation context; budget exhaustion and cancellation propagate.</param>
    /// <returns>Whether the complete path resolved.</returns>
    public bool TryResolve(MemoryRegion region, string typeId, string path, out MemorySelection? selection, out MemoryReadFailure? failure, MemoryAccessContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(region);
        context?.CancellationToken.ThrowIfCancellationRequested();
        selection = null;
        return this.Schema.TrySelect(typeId, path, out MemoryPath? plan, out failure) && plan!.TryResolve(this, region, out selection, out failure, context);
    }

    /// <summary>Invokes the session's resolver with the active operation context.</summary>
    /// <param name="request">Stored pointer and containing-record coordinates.</param>
    /// <param name="context">Shared cancellation and translation budget.</param>
    /// <returns>The resolver's finite target region.</returns>
    internal MemoryRegion ResolvePointer(PointerRequest request, MemoryAccessContext context) => this.resolver(request, context);
}
