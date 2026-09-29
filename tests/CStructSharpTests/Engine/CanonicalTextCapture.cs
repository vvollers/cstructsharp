namespace CStructSharp.Tests;

using CStructSharp.Fuzzing;

/// <summary>Records the failures of differential operations in a <see cref="CanonicalText"/> rendering.</summary>
internal static class CanonicalTextCapture
{
    /// <summary>
    ///     Runs part of an operation and, when it throws, appends the failure under <paramref name="label"/>. Every
    ///     exception except a test assertion counts, so an unexpected exception type is compared rather than lost.
    /// </summary>
    /// <param name="output">The rendering.</param>
    /// <param name="label">The label of a failure.</param>
    /// <param name="body">The part of the operation, which appends its own results.</param>
    /// <returns>Whether <paramref name="body"/> completed.</returns>
    public static bool Capture(this CanonicalText output, string label, Action body)
    {
        try
        {
            body();
            return true;
        }
        catch (Exception failure) when (failure is not UnitTestAssertException)
        {
            output.Failure(label, failure);
            return false;
        }
    }
}
