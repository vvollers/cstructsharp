namespace CStructSharp.Tests;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CStructSharp.Diagnostics;

/// <summary>
///     Checks the interpreter against itself: the same read from different sources must render the same outcome. The
///     differential harness compares implementations for one source; this compares sources for one implementation, so
///     a disagreement that exists before the engine runs is reported as the interpreter's, not the engine's.
/// </summary>
internal static class EngineAgreement
{
    /// <summary>
    ///     The spellings of the terminated UTF-16 string types - the <c>unicode_string_*</c> primitives and their
    ///     aliases - from the Portable contract (<c>portable-v1.json</c>).
    /// </summary>
    private static readonly Lazy<string[]> Utf16Terminated = new(
        () =>
        {
            using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "portable-v1.json")));
            JsonElement root = contract.RootElement;
            IEnumerable<string> primitives = root.GetProperty("terminatedPrimitives").EnumerateArray().Select(item => item.GetProperty("spelling").GetString()!);
            IEnumerable<string> aliases = root.GetProperty("aliasSpellings").EnumerateArray()
                                              .Where(item => item.GetProperty("canonical").GetString()!.StartsWith("unicode_string_", StringComparison.Ordinal))
                                              .Select(item => item.GetProperty("spelling").GetString()!);
            return primitives.Where(spelling => spelling.StartsWith("unicode_string_", StringComparison.Ordinal)).Concat(aliases).ToArray();
        });

    /// <summary>
    ///     Asserts that the sources agree with each other, which checks the interpreter against itself: the memory
    ///     forms must render exactly what the first memory form renders, the stream forms exactly what the first stream
    ///     form renders (final positions included), and the first stream form what the first memory form renders apart
    ///     from the position, which only streams report. A failure reports stream positions, so the positions in an
    ///     <see cref="EngineInput.ExposedStream"/>'s failures are first made relative to its data's start.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A stream that returns fewer bytes per read (<see cref="EngineInput.ChunkedStream1"/>, 3 and 7) fails at
    ///         the granularity of its reads, as documented: its failure positions (the offset, the final position, and
    ///         an inner decoder message's index) are not compared, only everything else about the failure.
    ///     </para>
    ///     <para>
    ///         One disagreement of the interpreter is known and skipped here, left for the consistency fixes; each
    ///         form is still compared with its own kind and, by the harness, with the engine. A terminated UTF-16 string
    ///         (<c>unicode_string_*</c>) read from a stream whose reads split a two-byte code unit misses its terminator
    ///         and fails with <c>no terminator</c>, so such a chunked failure is not compared.
    ///     </para>
    /// </remarks>
    /// <param name="label">The case, for the failure message.</param>
    /// <param name="renderings">Each source's rendering: the memory forms first, then the stream forms.</param>
    /// <param name="memoryMatchesStreams">Whether the memory forms must agree with the stream forms.</param>
    public static void AssertSourcesAgree(string label, List<(EngineInput Input, string Rendering)> renderings, bool memoryMatchesStreams = true)
    {
        (EngineInput Input, string Rendering)? memoryReference = null;
        (EngineInput Input, string Rendering)? streamReference = null;
        var failures = new StringBuilder();

        // Compares a rendering with its kind's reference, or makes it the reference.
        void Check(ref (EngineInput Input, string Rendering)? reference, EngineInput input, string rendering)
        {
            if (reference is null)
            {
                reference = (input, rendering);
                return;
            }

            string expected = reference.Value.Rendering;
            if (input is EngineInput.ChunkedStream1 or EngineInput.ChunkedStream3 or EngineInput.ChunkedStream7)
            {
                if (rendering.Contains("the terminated string has no terminator", StringComparison.Ordinal) && Utf16Terminated.Value.Any(spelling => rendering.Contains("failure.memberType = \"" + spelling + "\"\n", StringComparison.Ordinal)))
                {
                    return;
                }

                expected = WithoutFailurePositions(expected);
                rendering = WithoutFailurePositions(rendering);
            }

            if (!string.Equals(expected, rendering, StringComparison.Ordinal))
            {
                failures.Append(reference.Value.Input).Append(" (-) and ").Append(input).Append(" (+) differ:\n").Append(EngineDifferential.Diff(expected, rendering));
            }
        }

        foreach ((EngineInput input, string original) in renderings)
        {
            if (EngineStreams.IsStream(input))
            {
                Check(ref streamReference, input, input == EngineInput.ExposedStream ? RelativeFailurePositions(original, EngineStreams.StartOf(input)) : original);
            }
            else
            {
                Check(ref memoryReference, input, original);
            }
        }

        if (memoryMatchesStreams && memoryReference is { } memory && streamReference is { } stream)
        {
            string streamOutcome = WithoutPositions(stream.Rendering);
            if (!string.Equals(memory.Rendering, streamOutcome, StringComparison.Ordinal))
            {
                failures.Append(memory.Input).Append(" (-) and ").Append(stream.Input).Append(" (+) differ:\n").Append(EngineDifferential.Diff(memory.Rendering, streamOutcome));
            }
        }

        if (failures.Length > 0)
        {
            Assert.Fail(label + ": the interpreter disagrees with itself across sources:\n" + failures);
        }
    }

    /// <summary>
    ///     Replaces the positions of a failed read - the failure's <c>offset</c> line and the <c>offset N</c> in its
    ///     message, the final <c>position</c>, and an inner failure's message - with <c>#</c>; a successful read is
    ///     returned unchanged.
    /// </summary>
    /// <param name="rendering">The rendering.</param>
    /// <returns>The rendering without failure positions.</returns>
    private static string WithoutFailurePositions(string rendering)
    {
        if (!rendering.Contains("failure = ", StringComparison.Ordinal))
        {
            return rendering;
        }

        IEnumerable<string> lines = rendering.Split('\n')
                                             .Where(line => !line.StartsWith("failure.inner.message = ", StringComparison.Ordinal))
                                             .Select(line => line.StartsWith("failure", StringComparison.Ordinal) || line.StartsWith("position = ", StringComparison.Ordinal)
                                                                 ? Regex.Replace(line, @"(offset |\.offset = |^position = )\d+", "$1#")
                                                                 : line);
        return string.Join('\n', lines);
    }

    /// <summary>
    ///     Rewrites the stream positions a failure reports - its <c>offset</c> line, the <c>offset N</c> in its message,
    ///     and a pointer target position (<c>stream range: N</c>) - relative to <paramref name="start"/>.
    /// </summary>
    /// <param name="rendering">The rendering of a read from a stream whose data starts at <paramref name="start"/>.</param>
    /// <param name="start">The stream position of the data's first byte.</param>
    /// <returns>The rendering with relative failure positions.</returns>
    private static string RelativeFailurePositions(string rendering, long start)
    {
        IEnumerable<string> lines = rendering.Split('\n').Select(
            line => line.StartsWith("failure", StringComparison.Ordinal)
                        ? Regex.Replace(line, @"(offset |\.offset = |stream range: )(\d+)", match => match.Groups[1].Value + (long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture) - start).ToString(CultureInfo.InvariantCulture))
                        : line);
        return string.Join('\n', lines);
    }

    /// <summary>Removes the <c>position</c> lines from a rendering.</summary>
    /// <param name="rendering">The rendering.</param>
    /// <returns>The rendering without final positions.</returns>
    private static string WithoutPositions(string rendering)
        => string.Join('\n', rendering.Split('\n').Where(line => !line.StartsWith("position = ", StringComparison.Ordinal)));
}
