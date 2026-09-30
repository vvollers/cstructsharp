namespace CStructSharp.Tests;

using System.Globalization;
using CStructSharp.Compilation.Programs;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Fuzzing;
using CStructSharp.Reading;

/// <summary>
///     Checks the layout capture an update makes before and after its change - every value's path and byte range, then
///     the conditional-layout trace, entry for entry - by the compiled engine's debug program (<see cref="Capture"/>)
///     against the golden reference (<see cref="EngineGolden"/>), over one input and source. An update of a conditional root
///     or of a terminated value accepts or rejects its change by this comparison, so the capture must match exactly,
///     failures included.
/// </summary>
internal static class EngineLayoutCapture
{
    /// <summary>
    ///     Asserts that the engine captures the golden layout of <paramref name="root"/> from <paramref name="data"/> read
    ///     through <paramref name="input"/> - the same entries, or the same failure, and the same final stream position. A
    ///     root the layout does not declare has no layout: the capture reports it as an unknown root, and nothing is checked
    ///     against the golden reference.
    /// </summary>
    /// <param name="name">The case, which with the input keys it in the golden reference.</param>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes; each capture reads its own copy.</param>
    /// <param name="input">The stream form, placed where an update's traversal stream would be.</param>
    /// <param name="root">The root to capture.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/>.</param>
    /// <param name="read">The read settings.</param>
    /// <returns>The rendering of the capture, or <c>no layout</c> for a root the layout does not declare.</returns>
    /// <exception cref="AssertFailedException">The capture differs, or an undeclared root is not reported as unknown.</exception>
    public static string AssertSame(string name, CStruct layout, byte[] data, EngineInput input, string root, IReadOnlyDictionary<string, int>? variables, ReadOptions read)
    {
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(read);
        LayoutVariableInput integers = LayoutVariableInput.FromIntegers(variables);
        string key = name + " (" + input + ")";
        if (!layout.Compilation.ModelQueries.TryGetCompiledDeclaration(root, out _))
        {
            // An undeclared root has no layout to capture: the capture reports it as an unknown path.
            using Stream stream = EngineStreams.Open(input, data);
            Assert.Throws<Diagnostics.CStructPathException>(() => Capture(layout, stream, EngineStreams.StartOf(input), root, integers, settings), key);
            return "no layout";
        }

        string actual = Render(data, input, (stream, origin) => Capture(layout, stream, origin, root, integers, settings));
        EngineGolden.Check(key, actual);
        return actual;
    }

    /// <summary>
    ///     Captures a root's layout as an update captures it before and after its change, from the caller's variables: the
    ///     root is read with its debug program (<see cref="ReadEngine.CaptureLayout"/>), which gives every value's path and
    ///     byte range, then the conditional-layout trace.
    /// </summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="stream">The data, the original or a staged copy.</param>
    /// <param name="origin">The root's position.</param>
    /// <param name="root">The root's name.</param>
    /// <param name="variables">The operation's layout variables.</param>
    /// <param name="settings">The read settings.</param>
    /// <returns>Each value's path and byte range, then each conditional member's name, position and selection (1 or 0).</returns>
    /// <exception cref="Diagnostics.CStructPathException">The root is unknown.</exception>
    public static (string Path, long Start, long End)[] Capture(CStruct layout, Stream stream, long origin, string root, in LayoutVariableInput variables, in ReadOperationSettings settings)
    {
        using VariableSlots slots = VariableSlots.Create(layout.Compilation.SlotTable, variables);
        ReadProgram program = layout.Compilation.GetRootDebugReadProgram(root).Program ?? throw layout.Compilation.ModelQueries.UnknownRoot(root);
        return ReadEngine.CaptureLayout(layout, stream, origin, program, slots, settings);
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
