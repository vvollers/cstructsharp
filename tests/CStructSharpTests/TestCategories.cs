namespace CStructSharp.Tests;

/// <summary>
///     Names of the MSTest categories that group tests across classes.
/// </summary>
internal static class TestCategories
{
    /// <summary>
    ///     Tests that assert how many bytes an operation allocates, measured with
    ///     <see cref="GC.GetAllocatedBytesForCurrentThread"/>. Every such test is also marked
    ///     <see cref="DoNotParallelizeAttribute"/>, so the whole group runs serially after the parallel tests: work
    ///     on other threads (the parallel suite, coverage instrumentation, tiered compilation) then cannot add
    ///     one-off allocations to the measured thread. A new allocation-asserting test takes both attributes.
    /// </summary>
    public const string Allocation = "Allocation";
}
