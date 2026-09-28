namespace CStructSharp.Fuzzing;

#pragma warning disable RCS1194 // Binary serialization constructors are not supported by modern .NET exception APIs.
/// <summary>Retains complete replay coordinates when an undocumented exception escapes a target.</summary>
public sealed class FuzzFailureException : Exception
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="FuzzFailureException"/> class with the coordinates needed to
    ///     replay the failing case.
    /// </summary>
    /// <param name="target">The name of the fuzz target that failed.</param>
    /// <param name="seed">The corpus seed that drives the stable mutation sequence.</param>
    /// <param name="iteration">The retained-seed index or mutation iteration that produced the input.</param>
    /// <param name="source">The retained seed identifier, or <c>mutation</c> for a generated input.</param>
    /// <param name="input">The failing input bytes; the exception keeps a copy.</param>
    /// <param name="innerException">The undocumented exception that escaped the target.</param>
    public FuzzFailureException(
        string target,
        ulong seed,
        int iteration,
        string source,
        byte[] input,
        Exception innerException)
        : base(
            $"Managed fuzz target '{target}' failed. Replay seed=0x{seed:X16}, iteration={iteration}, " +
            $"source={source}, input={Convert.ToHexString(input)}.",
            innerException)
    {
        this.Target = target;
        this.Seed = seed;
        this.Iteration = iteration;
        this.CaseSource = source;
        this.Input = input.ToArray();
    }

    /// <summary>Gets the name of the fuzz target that failed.</summary>
    public string Target { get; }

    /// <summary>Gets the corpus seed that drives the stable mutation sequence.</summary>
    public ulong Seed { get; }

    /// <summary>Gets the retained-seed index or mutation iteration that produced the input.</summary>
    public int Iteration { get; }

    /// <summary>Gets the retained seed identifier, or <c>mutation</c> for a generated input.</summary>
    public string CaseSource { get; }

    /// <summary>Gets a copy of the failing input bytes.</summary>
    public byte[] Input { get; }
}
#pragma warning restore RCS1194
