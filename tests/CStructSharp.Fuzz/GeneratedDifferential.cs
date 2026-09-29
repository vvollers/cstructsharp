namespace CStructSharp.Fuzzing;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     The generated-vs-runtime differential: for one input, every generated layout of the harness reads it with its
///     generated <c>Parse</c> and the runtime reads it with <c>Parse</c>; both must fail the same way (type and
///     message) or both succeed with values whose writes produce the same bytes. A disagreement is a harness
///     failure, never a documented one. The agreed outcome of each layout is rendered for the replay digest.
/// </summary>
internal static class GeneratedDifferential
{
    /// <summary>
    ///     Reads and rewrites one input through the runtime and generated binary, path and pointer-union layouts and
    ///     compares the outcomes per layout, rendering each agreed outcome: the written bytes or the failure.
    /// </summary>
    /// <param name="input">The fuzz input parsed from offset 0 by every layout.</param>
    /// <param name="readOptions">The read limits shared by both paths.</param>
    /// <param name="writeOptions">The write limits shared by both paths.</param>
    /// <param name="output">The rendering that receives one outcome per layout.</param>
    /// <exception cref="InvalidOperationException">
    ///     The paths fail differently, only one fails, or their written bytes differ.
    /// </exception>
    public static void Run(byte[] input, ReadOptions readOptions, WriteOptions writeOptions, CanonicalText output)
    {
        Compare(
            output,
            "binary",
            input,
            () => BinaryLayout.Layout.Serialize("root", BinaryLayout.Layout.Parse(input.AsSpan(), "root", options: readOptions), options: writeOptions),
            () => BinaryLayout.Serialize(BinaryLayout.Parse(input.AsSpan(), readOptions), writeOptions));
        Compare(
            output,
            "path",
            input,
            () => PathLayout.Layout.Serialize("root", PathLayout.Layout.Parse(input.AsSpan(), "root", options: readOptions), options: writeOptions),
            () => PathLayout.Serialize(PathLayout.Parse(input.AsSpan(), readOptions), writeOptions));
        Compare(
            output,
            "pointer-union",
            input,
            () => PointerUnionLayout.Layout.Serialize("node", PointerUnionLayout.Layout.Parse(input.AsSpan(), "node", options: readOptions), options: writeOptions),
            () => PointerUnionLayout.Serialize(PointerUnionLayout.Parse(input.AsSpan(), readOptions), writeOptions));
    }

    /// <summary>
    ///     Runs the runtime and generated read-and-write of one layout, requires the same failure or the same bytes, and
    ///     renders that outcome under the layout's name.
    /// </summary>
    /// <param name="output">The rendering.</param>
    /// <param name="layout">The layout's name, for messages and the rendering.</param>
    /// <param name="input">The input, for messages.</param>
    /// <param name="runtime">Reads and writes the input with the runtime layout.</param>
    /// <param name="generated">Reads and writes the input with the generated layout.</param>
    /// <exception cref="InvalidOperationException">The two paths disagree.</exception>
    private static void Compare(CanonicalText output, string layout, byte[] input, Func<byte[]> runtime, Func<byte[]> generated)
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

            output.Failure(layout, runtimeFailure!);
            return;
        }

        if (!runtimeBytes.AsSpan().SequenceEqual(generatedBytes))
        {
            throw new InvalidOperationException(
                $"Generated and runtime writes differ on layout '{layout}' for input {Convert.ToHexString(input)}: runtime {Convert.ToHexString(runtimeBytes!)}, generated {Convert.ToHexString(generatedBytes!)}.");
        }

        output.Bytes(layout, runtimeBytes);
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
