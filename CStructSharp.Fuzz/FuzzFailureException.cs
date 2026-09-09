namespace CStructSharp.Fuzzing;

#pragma warning disable RCS1194 // Binary serialization constructors are not supported by modern .NET exception APIs.
/// <summary>Retains complete replay coordinates when an undocumented exception escapes a target.</summary>
public sealed class FuzzFailureException : Exception
{
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

    public string Target { get; }

    public ulong Seed { get; }

    public int Iteration { get; }

    public string CaseSource { get; }

    public byte[] Input { get; }
}
#pragma warning restore RCS1194
