namespace CStructSharp.Fuzzing;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     The generated-vs-runtime differential: for one input, every generated layout of the harness reads it with its
///     generated <c>Parse</c> and the runtime reads it with <c>Parse</c>; both must fail the same way (type and
///     message) or both succeed with values whose writes produce the same bytes. A disagreement is a harness
///     failure, never a documented one.
/// </summary>
internal static class GeneratedDifferential
{
    public static void Run(byte[] input, ReadOptions readOptions, WriteOptions writeOptions)
    {
        Compare(
            "binary",
            input,
            () => BinaryLayout.Layout.Serialize("root", BinaryLayout.Layout.Parse(input.AsSpan(), "root", options: readOptions), options: writeOptions),
            () => BinaryLayout.Serialize(BinaryLayout.Parse(input.AsSpan(), readOptions), writeOptions));
        Compare(
            "path",
            input,
            () => PathLayout.Layout.Serialize("root", PathLayout.Layout.Parse(input.AsSpan(), "root", options: readOptions), options: writeOptions),
            () => PathLayout.Serialize(PathLayout.Parse(input.AsSpan(), readOptions), writeOptions));
        Compare(
            "pointer-union",
            input,
            () => PointerUnionLayout.Layout.Serialize("node", PointerUnionLayout.Layout.Parse(input.AsSpan(), "node", options: readOptions), options: writeOptions),
            () => PointerUnionLayout.Serialize(PointerUnionLayout.Parse(input.AsSpan(), readOptions), writeOptions));
    }

    private static void Compare(string layout, byte[] input, Func<byte[]> runtime, Func<byte[]> generated)
    {
        (byte[]? runtimeBytes, CStructException? runtimeFailure) = Attempt(runtime);
        (byte[]? generatedBytes, CStructException? generatedFailure) = Attempt(generated);
        if (runtimeFailure is not null || generatedFailure is not null)
        {
            if (runtimeFailure?.GetType() != generatedFailure?.GetType() || runtimeFailure?.Message != generatedFailure?.Message)
            {
                throw new InvalidOperationException(
                    $"Generated and runtime paths disagree on layout '{layout}' for input {Convert.ToHexString(input)}: runtime {Describe(runtimeFailure)}, generated {Describe(generatedFailure)}.");
            }

            return;
        }

        if (!runtimeBytes.AsSpan().SequenceEqual(generatedBytes))
        {
            throw new InvalidOperationException(
                $"Generated and runtime writes differ on layout '{layout}' for input {Convert.ToHexString(input)}: runtime {Convert.ToHexString(runtimeBytes!)}, generated {Convert.ToHexString(generatedBytes!)}.");
        }
    }

    private static (byte[]? Bytes, CStructException? Failure) Attempt(Func<byte[]> action)
    {
        try
        {
            return (action(), null);
        }
        catch (CStructException exception)
        {
            return (null, exception);
        }
    }

    private static string Describe(CStructException? failure) => failure is null ? "succeeded" : failure.GetType().Name + " '" + failure.Message + "'";
}
