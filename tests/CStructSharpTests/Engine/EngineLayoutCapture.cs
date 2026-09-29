namespace CStructSharp.Tests;

using System.Globalization;
using CStructSharp.Expressions;
using CStructSharp.Fuzzing;
using CStructSharp.Reading;

/// <summary>
///     Compares the layout capture an update makes before and after its change - every value's path and byte range, then
///     the conditional-layout trace, entry for entry - between the interpreter (<c>CaptureUpdateLayout</c>) and the
///     compiled engine's debug program (<c>CaptureUpdateLayoutWithEngine</c>), over one input and source. An update of a
///     conditional root or of a terminated value accepts or rejects its change by this comparison, so the two must agree
///     exactly, failures included.
/// </summary>
internal static class EngineLayoutCapture
{
    /// <summary>
    ///     Asserts that both implementations capture the same layout of <paramref name="root"/> from
    ///     <paramref name="data"/> read through <paramref name="input"/> - the same entries, or the same failure, and the same
    ///     final stream position - and that the engine captured it whenever the root's debug program is eligible.
    /// </summary>
    /// <param name="name">The case, for failure messages.</param>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes; each side reads its own copy.</param>
    /// <param name="input">The stream form, placed where an update's traversal stream would be.</param>
    /// <param name="root">The root to capture.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/>.</param>
    /// <param name="read">The read settings.</param>
    /// <returns>The shared rendering.</returns>
    public static string AssertSame(string name, CStruct layout, byte[] data, EngineInput input, string root, IReadOnlyDictionary<string, int>? variables, ReadOptions read)
    {
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(read);
        LayoutVariableInput integers = LayoutVariableInput.FromIntegers(variables);
        bool eligible = layout.Compilation.GetRootDebugReadProgram(root).IsEligible;
        bool captured = false;
        string expected = Render(data, input, (stream, origin) => layout.CaptureUpdateLayout(stream, origin, root, integers, settings));
        string actual = Render(
            data,
            input,
            (stream, origin) =>
            {
                (string Path, long Start, long End)[]? layoutCapture = layout.CaptureUpdateLayoutWithEngine(stream, origin, root, integers, settings);
                captured = layoutCapture is not null;
                return layoutCapture ?? layout.CaptureUpdateLayout(stream, origin, root, integers, settings);
            });
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            Assert.Fail(name + " (" + input + "): the interpreter's (-) and the engine's (+) layout captures differ:\n" + EngineDifferential.Diff(expected, actual));
        }

        // A failure before the engine is asked (a stream that cannot seek) leaves nothing to check.
        if (eligible && !expected.StartsWith("failure", StringComparison.Ordinal))
        {
            Assert.IsTrue(captured, name + " (" + input + "): the engine did not capture an eligible root's layout");
        }

        return expected;
    }

    /// <summary>Runs one capture over a fresh stream and renders its entries, or its failure, then the stream's final position.</summary>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The stream form.</param>
    /// <param name="capture">The capture, given the stream and the root's position in it.</param>
    /// <returns>The rendering.</returns>
    private static string Render(byte[] data, EngineInput input, Func<Stream, long, (string Path, long Start, long End)[]> capture)
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
    }
}
