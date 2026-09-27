namespace CStructSharp.Memory.Metadata;

/// <summary>
///     Drives a depth-first walk over a metadata type graph with an explicit stack of steps instead of recursion.
/// </summary>
/// <remarks>
///     A real kernel's type graph nests many thousands of pointer and member hops deep, which says nothing about
///     whether the metadata is well-formed. One call-stack frame per hop would need either a limit small enough to
///     reject legitimate metadata or one large enough to risk an unrecoverable <see cref="StackOverflowException"/> on
///     hostile input. With an explicit stack neither happens; each importer bounds the total work by visiting every type
///     once and by its descriptor budget.
/// </remarks>
internal static class MetadataGraphWalk
{
    /// <summary>Runs steps until none is left; a step pushes the steps it depends on above the ones that must follow them.</summary>
    /// <typeparam name="TStep">The importer's step type.</typeparam>
    /// <param name="start">The first step.</param>
    /// <param name="run">Runs one step, pushing any further steps onto the stack it is given.</param>
    /// <param name="cancellationToken">Checked before each step.</param>
    public static void Run<TStep>(TStep start, Action<TStep, Stack<TStep>> run, CancellationToken cancellationToken)
    {
        var work = new Stack<TStep>();
        work.Push(start);
        while (work.TryPop(out TStep? step))
        {
            cancellationToken.ThrowIfCancellationRequested();
            run(step, work);
        }
    }
}
