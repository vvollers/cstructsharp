namespace FastImpact;

using System.Runtime.CompilerServices;

/// <summary>Consumes typed results without per-call boxing or reflection.</summary>
/// <typeparam name="T">The exact result type.</typeparam>
public static class Sink<T>
{
    private static T? last;

    /// <summary>Keeps each result observable to the JIT.</summary>
    /// <param name="value">The result whose production must remain in the measured loop.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Consume(T value) => last = value;
}
