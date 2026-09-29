namespace CStructSharp.Tests;

using System.Globalization;
using CStructSharp.Expressions;
using CStructSharp.Fuzzing;
using CStructSharp.Reading;

/// <summary>
///     Checks the layout capture an update makes before and after its change - every value's path and byte range, then
///     the conditional-layout trace, entry for entry - by the compiled engine's debug program
///     (<c>CaptureUpdateLayoutWithEngine</c>) against the golden reference (<see cref="EngineGolden"/>), over one input and
///     source. An update of a conditional root or of a terminated value accepts or rejects its change by this comparison,
///     so the capture must match exactly, failures included. The golden capture is the interpreter's
///     (<c>CaptureUpdateLayout</c>), which a run that compares with the interpreter
///     (<see cref="EngineGolden.ComparesInterpreter"/>) also compares with the engine's.
/// </summary>
internal static class EngineLayoutCapture
{
    /// <summary>
    ///     Asserts that the engine captures the golden layout of <paramref name="root"/> from <paramref name="data"/> read
    ///     through <paramref name="input"/> - the same entries, or the same failure, and the same final stream position; an
    ///     eligible root the engine does not capture renders as a failure. When the run compares with the interpreter,
    ///     the interpreter's capture must match the engine's, and it is the one checked (or recorded). A root without a debug program (one the layout does not
    ///     declare) has no layout: the engine must capture nothing, and nothing is checked against the golden reference.
    /// </summary>
    /// <param name="name">The case, which with the input keys it in the golden reference.</param>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes; each capture reads its own copy.</param>
    /// <param name="input">The stream form, placed where an update's traversal stream would be.</param>
    /// <param name="root">The root to capture.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/>.</param>
    /// <param name="read">The read settings.</param>
    /// <returns>The rendering of the capture, or <c>no layout</c> for a root without a debug program.</returns>
    /// <exception cref="AssertFailedException">The capture differs, or the engine captured a root without a debug program.</exception>
    public static string AssertSame(string name, CStruct layout, byte[] data, EngineInput input, string root, IReadOnlyDictionary<string, int>? variables, ReadOptions read)
    {
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(read);
        LayoutVariableInput integers = LayoutVariableInput.FromIntegers(variables);
        string key = name + " (" + input + ")";
        if (!layout.Compilation.GetRootDebugReadProgram(root).IsEligible)
        {
            // Every root a layout declares has a debug program, so only an undeclared root lacks one: it has no layout to
            // capture, which the interpreter reports as an unknown path.
            using Stream stream = EngineStreams.Open(input, data);
            Assert.IsNull(layout.CaptureUpdateLayoutWithEngine(stream, EngineStreams.StartOf(input), root, integers, settings), key + ": the engine captured a root without a debug program");
            if (EngineGolden.ComparesInterpreter)
            {
                Assert.Throws<Diagnostics.CStructPathException>(() => layout.CaptureUpdateLayout(stream, EngineStreams.StartOf(input), root, integers, settings), key);
            }

            return "no layout";
        }

        string? expected = EngineGolden.ComparesInterpreter ? Render(data, input, (stream, origin) => layout.CaptureUpdateLayout(stream, origin, root, integers, settings)) : null;
        string actual = Render(
            data,
            input,
            (stream, origin) => layout.CaptureUpdateLayoutWithEngine(stream, origin, root, integers, settings) ??
                                throw new InvalidOperationException("The engine did not capture the layout of the eligible root " + root + "."));
        if (expected is null)
        {
            EngineGolden.Check(key, actual);
        }
        else
        {
            EngineGolden.Check(key, expected);
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                Assert.Fail(key + ": the interpreter's (-) and the engine's (+) layout captures differ:\n" + EngineDifferential.Diff(expected, actual));
            }
        }

        return actual;
    }

    /// <summary>
    ///     Runs one capture over a fresh stream under the invariant culture and renders its entries, or its failure, then
    ///     the stream's final position.
    /// </summary>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The stream form.</param>
    /// <param name="capture">The capture, given the stream and the root's position in it.</param>
    /// <returns>The rendering.</returns>
    private static string Render(byte[] data, EngineInput input, Func<Stream, long, (string Path, long Start, long End)[]> capture)
    {
        return EngineGolden.Invariant(
            () =>
            {
                using Stream stream = EngineStreams.Open(input, data);
                long origin = EngineStreams.StartOf(input);
                var output = new CanonicalText();
                output.Capture(
                    "failure",
                    () =>
                    {
                        (string Path, long Start, long End)[] entries = capture(stream, origin);
                        output.Line("entries", entries.Length.ToString(CultureInfo.InvariantCulture));
                        foreach ((string path, long start, long end) in entries)
                        {
                            output.Line("entry", "\"" + path + "\" " + start.ToString(CultureInfo.InvariantCulture) + " " + end.ToString(CultureInfo.InvariantCulture));
                        }
                    });
                output.Line("position", (stream.Position - origin).ToString(CultureInfo.InvariantCulture));
                return output.ToString();
            });
    }
}
